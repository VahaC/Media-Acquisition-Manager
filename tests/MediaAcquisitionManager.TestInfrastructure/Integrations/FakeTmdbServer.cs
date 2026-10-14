using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-33 — TMDb behind a <see cref="RecordingHttpHandler"/>: its API at <see cref="BaseUrl"/> and its image
/// host, answering every request the TMDb adapter makes from a catalog a test builds — works
/// (<see cref="AddWork"/>), collections, people, searches, IMDb ids and discovery pages, and MAM-34's episodes
/// (<see cref="AddEpisode"/>) — in the shape TMDb really answers (<c>VendorSamples/Tmdb</c>, whose field names it
/// copies).
/// </summary>
/// <remarks>
/// <para>
/// As TMDb does: a request to the API without <c>Authorization: Bearer {Token}</c> answers the recorded
/// 401 (<see cref="TmdbSamples.WrongKeyResponseFile"/>); a work, collection or person the catalog doesn't
/// have answers TMDb's 404 (<see cref="NotFoundBody"/>), as does an episode nobody added and any path TMDb doesn't
/// serve; a search, an
/// IMDb id or a discovery page nobody added answers an empty list. The genre lists are the recorded ones
/// (<see cref="TmdbSamples.GenresMovieFile"/>, <see cref="TmdbSamples.GenresTvFile"/>). A poster at
/// <c>https://image.tmdb.org/t/p/{size}/{file}</c> answers a small JPEG (<see cref="PosterJpeg"/>), whatever
/// the file; anything else on that host 404. A request to any other host answers 503, like an unarmed
/// handler.
/// </para>
/// <para>
/// A work, a collection, a search, an IMDb id or a page that lists a work id no <see cref="AddWork"/> added
/// fails the request with an <see cref="InvalidOperationException"/> naming it, so a test's typo is not
/// mistaken for an empty answer. A director or creator no <see cref="AddPerson"/> named is called
/// "Person {id}". Anything else — a 429, a slow answer, a malformed body — is <see cref="Intercept"/>'s.
/// </para>
/// <para>
/// Thread safe: a recommendation page asks for several works at once. A test builds the catalog before
/// the calls; an <see cref="Intercept"/> it sets must be safe to call concurrently itself.
/// </para>
/// </remarks>
public sealed class FakeTmdbServer
{
    /// <summary>The address an integration stores for this TMDb; a fake name, never the real API.</summary>
    public const string BaseUrl = "http://tmdb.test";

    /// <summary>The API Read Access Token this TMDb takes; any other is refused with the recorded 401.</summary>
    public const string Token = "fake-tmdb-read-access-token-0001";

    /// <summary>What TMDb answers, with 404, a work, collection or person it doesn't have.</summary>
    public const string NotFoundBody =
        """{"success":false,"status_code":34,"status_message":"The resource you requested could not be found."}""";

    /// <summary>TMDb's image host, as the adapter asks it for posters.</summary>
    public const string ImageHost = "image.tmdb.org";

    // TMDb sends its text as UTF-8, not as \u escapes.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly Uri Api = new(BaseUrl);

    private static readonly Lazy<Dictionary<int, string>> MovieGenreNames = new(() => GenreNames(TmdbSamples.GenresMovie()));
    private static readonly Lazy<Dictionary<int, string>> TvGenreNames = new(() => GenreNames(TmdbSamples.GenresTv()));

    private readonly Lock _gate = new();
    private readonly Dictionary<(MediaWorkKind Kind, int Id), FakeTmdbWork> _works = [];
    private readonly Dictionary<int, (string Name, int[] MovieIds)> _collections = [];
    private readonly Dictionary<int, string> _people = [];
    private readonly Dictionary<(MediaWorkKind Kind, string Query, int? Year), int[]> _searches = [];
    private readonly Dictionary<string, List<(MediaWorkKind Kind, int Id)>> _finds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(MediaWorkKind Kind, int Page), int[]> _discoveries = [];
    private readonly Dictionary<(int ShowId, int Season, int Episode), FakeTmdbEpisode> _episodes = [];

    private FakeTmdbServer()
    {
    }

    /// <summary>Answers a request before TMDb does — a 429, a failure, a malformed body — or returns
    /// <c>null</c> to let TMDb answer.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>A new, empty TMDb answering every request <paramref name="handler"/> receives.</summary>
    public static FakeTmdbServer Arm(RecordingHttpHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var server = new FakeTmdbServer();
        handler.Handler = (request, _) => server.Answer(request);
        return server;
    }

    /// <summary>A fresh copy of the JPEG every poster answers.</summary>
    public static byte[] PosterJpeg() => TestImages.Jpeg();

    /// <summary>Adds a film or a show; adding one of the same kind and id again replaces it.</summary>
    public void AddWork(FakeTmdbWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
            _works[(work.Kind, work.Id)] = work;
    }

    /// <summary>A collection of the films <paramref name="movieIds"/>, in that order; each must be added as
    /// a film too.</summary>
    public void AddCollection(int id, string name, params int[] movieIds)
    {
        lock (_gate)
            _collections[id] = (name, movieIds);
    }

    /// <summary>
    /// A person TMDb knows. Their works are the catalog's: every film listing them in
    /// <see cref="FakeTmdbWork.DirectorIds"/> as its director, every show as its creator.
    /// </summary>
    public void AddPerson(int id, string name)
    {
        lock (_gate)
            _people[id] = name;
    }

    /// <summary>
    /// What a search of <paramref name="kind"/> for <paramref name="query"/> answers: the works
    /// <paramref name="ids"/>, in that order. The query is compared in any letter case, surrounding
    /// spaces ignored; <paramref name="year"/> must be the search's year (<c>year</c> of a film's,
    /// <c>first_air_date_year</c> of a show's), and <c>null</c> answers a search without one.
    /// </summary>
    public void AddSearch(MediaWorkKind kind, string query, int? year, params int[] ids)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
            _searches[(kind, query.Trim().ToUpperInvariant(), year)] = ids;
    }

    /// <summary>Files the work <paramref name="id"/> of <paramref name="kind"/> under
    /// <paramref name="imdbId"/>, beside any filed there already.</summary>
    public void AddFind(string imdbId, MediaWorkKind kind, int id)
    {
        ArgumentNullException.ThrowIfNull(imdbId);
        lock (_gate)
        {
            if (!_finds.TryGetValue(imdbId, out var found))
                _finds[imdbId] = found = [];
            found.Add((kind, id));
        }
    }

    /// <summary>MAM-34 — adds an episode of a show; adding one of the same show, season and number again replaces it.
    /// The show itself needn't be added: TMDb answers an episode by its path alone.</summary>
    public void AddEpisode(FakeTmdbEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        lock (_gate)
            _episodes[(episode.ShowId, episode.SeasonNumber, episode.EpisodeNumber)] = episode;
    }

    /// <summary>What page <paramref name="page"/> of a discovery of <paramref name="kind"/> answers,
    /// whatever genres it asks for: the works <paramref name="ids"/>, in that order.</summary>
    public void AddDiscover(MediaWorkKind kind, int page, params int[] ids)
    {
        lock (_gate)
            _discoveries[(kind, page)] = ids;
    }

    /// <summary>What TMDb answers <paramref name="request"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Intercept?.Invoke(request) is { } intercepted)
            return intercepted;

        var uri = request.RequestUri!;
        if (string.Equals(uri.Host, ImageHost, StringComparison.OrdinalIgnoreCase))
            return Poster(uri);
        if (Uri.Compare(uri, Api, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0)
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (!HasToken(request))
            return VendorResponses.Recorded(TmdbSamples.WrongKeyResponseFile);
        if (!uri.AbsolutePath.StartsWith("/3/", StringComparison.Ordinal))
            return NotFound();

        var segments = uri.AbsolutePath[3..].Split('/');
        var query = Query(uri);
        lock (_gate)
        {
            return segments switch
            {
                ["authentication"] => JsonAnswer(new JsonObject { ["success"] = true }),
                ["movie", var id] => Details(MediaWorkKind.Movie, id, query),
                ["tv", var id] => Details(MediaWorkKind.Series, id, query),
                ["tv", var id, "season", var season, "episode", var episode] => Episode(id, season, episode, query),
                ["collection", var id] => Collection(id),
                ["person", var id, "combined_credits"] => CombinedCredits(id),
                ["search", var type] when KindOf(type) is { } kind => Search(kind, query),
                ["find", var imdbId] => Find(imdbId, query),
                ["discover", var type] when KindOf(type) is { } kind => Discover(kind, query),
                ["genre", "movie", "list"] => JsonText(TmdbSamples.GenresMovie()),
                ["genre", "tv", "list"] => JsonText(TmdbSamples.GenresTv()),
                _ => NotFound(),
            };
        }
    }

    // ── the API's answers ────────────────────────────────────────────────────

    private HttpResponseMessage Details(MediaWorkKind kind, string id, Dictionary<string, string> query)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || !_works.TryGetValue((kind, number), out var work))
            return NotFound();

        var details = kind == MediaWorkKind.Series ? ShowDetails(work) : MovieDetails(work);
        // In the language asked, when the work has its overview by language: TMDb's "" for one it has none in.
        if (work.OverviewsByLanguage is { } overviews)
            details["overview"] = query.TryGetValue("language", out var language) && overviews.TryGetValue(language, out var overview) ? overview : "";
        var appended = query.TryGetValue("append_to_response", out var append) ? append.Split(',') : [];
        if (appended.Contains("credits"))
            details["credits"] = Credits(work);
        if (appended.Contains("recommendations"))
            details["recommendations"] = Page(1, Entries(kind, work.RecommendationIds ?? [], $"{KindName(kind)} {work.Id}'s recommendations", withMediaType: true), 1);
        // MAM-34 — in the shapes the live check recorded (VendorSamples/Tmdb/movie-details-short.json,
        // tv-details-planet-earth.json): a film's titles under "titles", a show's under "results".
        if (appended.Contains("alternative_titles"))
            details["alternative_titles"] = new JsonObject { [kind == MediaWorkKind.Series ? "results" : "titles"] = AlternativeTitles(work) };
        if (appended.Contains("external_ids"))
            details["external_ids"] = ExternalIds(work);
        return JsonAnswer(details);
    }

    // MAM-34 — in the shape of TMDb's episode details (VendorSamples/Tmdb/tv-episode-wwii-s01e01.json).
    private HttpResponseMessage Episode(string id, string season, string episode, Dictionary<string, string> query)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var showId)
            || !int.TryParse(season, NumberStyles.None, CultureInfo.InvariantCulture, out var seasonNumber)
            || !int.TryParse(episode, NumberStyles.None, CultureInfo.InvariantCulture, out var episodeNumber)
            || !_episodes.TryGetValue((showId, seasonNumber, episodeNumber), out var found))
            return NotFound();

        var details = new JsonObject
        {
            ["air_date"] = found.AirDate,
            ["crew"] = new JsonArray(),
            ["episode_number"] = found.EpisodeNumber,
            ["episode_type"] = "standard",
            ["guest_stars"] = new JsonArray(),
            ["name"] = found.Title,
            ["overview"] = "",
            ["id"] = showId * 1000 + seasonNumber * 100 + episodeNumber,
            ["production_code"] = "",
            ["runtime"] = found.RuntimeMinutes,
            ["season_number"] = found.SeasonNumber,
            ["still_path"] = null,
            ["vote_average"] = 0.0,
            ["vote_count"] = 0,
        };
        if (query.TryGetValue("append_to_response", out var append) && append.Split(',').Contains("external_ids"))
        {
            details["external_ids"] = new JsonObject
            {
                ["imdb_id"] = found.ImdbId,
                ["freebase_mid"] = null,
                ["freebase_id"] = null,
                ["tvdb_id"] = null,
                ["tvrage_id"] = null,
                ["wikidata_id"] = null,
            };
        }

        return JsonAnswer(details);
    }

    private HttpResponseMessage Collection(string id)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || !_collections.TryGetValue(number, out var collection))
            return NotFound();

        return JsonAnswer(new JsonObject
        {
            ["id"] = number,
            ["name"] = collection.Name,
            ["original_language"] = "en",
            ["original_name"] = collection.Name,
            ["overview"] = "",
            ["poster_path"] = null,
            ["backdrop_path"] = null,
            ["parts"] = Entries(MediaWorkKind.Movie, collection.MovieIds, $"collection {number}", withMediaType: true),
        });
    }

    private HttpResponseMessage CombinedCredits(string id)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || !_people.ContainsKey(number))
            return NotFound();

        var crew = new JsonArray();
        foreach (var work in _works.Values.Where(work => (work.DirectorIds ?? []).Contains(number)).OrderBy(work => work.Kind).ThenBy(work => work.Id))
        {
            var entry = Entry(work, withMediaType: true);
            entry["credit_id"] = CreditId(work, number);
            // TMDb files a show's creators in a department of their own.
            entry["department"] = work.Kind == MediaWorkKind.Series ? "Creator" : "Directing";
            entry["job"] = work.Kind == MediaWorkKind.Series ? "Creator" : "Director";
            crew.Add(entry);
        }

        return JsonAnswer(new JsonObject { ["cast"] = new JsonArray(), ["crew"] = crew, ["id"] = number });
    }

    private HttpResponseMessage Search(MediaWorkKind kind, Dictionary<string, string> query)
    {
        var text = query.TryGetValue("query", out var value) ? value.Trim().ToUpperInvariant() : "";
        var yearParameter = kind == MediaWorkKind.Series ? "first_air_date_year" : "year";
        int? year = query.TryGetValue(yearParameter, out var yearText) ? int.Parse(yearText, CultureInfo.InvariantCulture) : null;
        var ids = _searches.TryGetValue((kind, text, year), out var found) ? found : [];
        return JsonAnswer(Page(1, Entries(kind, ids, $"the search for “{value}”", withMediaType: false), 1));
    }

    private HttpResponseMessage Find(string imdbId, Dictionary<string, string> query)
    {
        // What TMDb answers a find without the source of the id.
        if (!query.TryGetValue("external_source", out var source) || !string.Equals(source, "imdb_id", StringComparison.Ordinal))
            return JsonAnswer(
                new JsonObject { ["success"] = false, ["status_code"] = 5, ["status_message"] = "Invalid parameters: Your request parameters are incorrect." },
                HttpStatusCode.BadRequest);

        var found = _finds.TryGetValue(imdbId, out var works) ? works : [];
        return JsonAnswer(new JsonObject
        {
            ["movie_results"] = Entries(MediaWorkKind.Movie, [.. found.Where(work => work.Kind == MediaWorkKind.Movie).Select(work => work.Id)], imdbId, withMediaType: true),
            ["person_results"] = new JsonArray(),
            ["tv_results"] = Entries(MediaWorkKind.Series, [.. found.Where(work => work.Kind == MediaWorkKind.Series).Select(work => work.Id)], imdbId, withMediaType: true),
            ["tv_episode_results"] = new JsonArray(),
            ["tv_season_results"] = new JsonArray(),
        });
    }

    private HttpResponseMessage Discover(MediaWorkKind kind, Dictionary<string, string> query)
    {
        var page = query.TryGetValue("page", out var pageText) ? int.Parse(pageText, CultureInfo.InvariantCulture) : 1;
        var ids = _discoveries.TryGetValue((kind, page), out var found) ? found : [];
        var pages = _discoveries.Keys.Where(key => key.Kind == kind).Select(key => key.Page).DefaultIfEmpty(1).Max();
        return JsonAnswer(Page(page, Entries(kind, ids, $"discovery page {page}", withMediaType: false), pages));
    }

    private static HttpResponseMessage Poster(Uri uri) =>
        uri.AbsolutePath.Split('/') is ["", "t", "p", { Length: > 0 }, { Length: > 0 }]
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PosterJpeg()) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } },
            }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

    // ── the works, in TMDb's shapes ──────────────────────────────────────────

    private JsonObject MovieDetails(FakeTmdbWork work)
    {
        var details = Common(work);
        details["belongs_to_collection"] = work.CollectionId is { } collectionId
            ? new JsonObject
            {
                ["id"] = collectionId,
                ["name"] = _collections.TryGetValue(collectionId, out var collection) ? collection.Name : $"Collection {collectionId}",
                ["poster_path"] = null,
                ["backdrop_path"] = null,
            }
            : null;
        details["budget"] = 0;
        details["genres"] = Genres(work);
        details["homepage"] = "";
        details["id"] = work.Id;
        details["imdb_id"] = work.ImdbId;
        details["original_title"] = work.OriginalTitle ?? work.Title;
        details["release_date"] = work.ReleaseDate ?? "";
        details["revenue"] = 0;
        // TMDb's 0 for a runtime it doesn't know.
        details["runtime"] = work.RuntimeMinutes ?? 0;
        details["status"] = "Released";
        details["tagline"] = "";
        details["title"] = work.Title;
        details["video"] = false;
        details["vote_average"] = work.Rating;
        details["vote_count"] = work.VoteCount;
        return details;
    }

    private JsonObject ShowDetails(FakeTmdbWork work)
    {
        var seasons = work.Seasons ?? [];
        var details = Common(work);
        details["created_by"] = new JsonArray(
        [
            .. (work.DirectorIds ?? []).Select(personId => (JsonNode)new JsonObject
            {
                ["id"] = personId,
                ["credit_id"] = CreditId(work, personId),
                ["name"] = PersonName(personId),
                ["original_name"] = PersonName(personId),
                ["gender"] = 0,
                ["profile_path"] = null,
            }),
        ]);
        // MAM-34 — empty, as TMDb sends it today; a show's runtime is its latest episode's (tv-details-planet-earth.json).
        details["episode_run_time"] = new JsonArray();
        details["first_air_date"] = work.ReleaseDate ?? "";
        details["genres"] = Genres(work);
        details["homepage"] = "";
        details["id"] = work.Id;
        details["in_production"] = false;
        var lastSeason = seasons.LastOrDefault(season => season.Number > 0);
        details["last_episode_to_air"] = work.RuntimeMinutes is { } runtime
            ? new JsonObject
            {
                ["id"] = work.Id * 1000,
                ["name"] = "Епізод",
                ["overview"] = "",
                ["air_date"] = lastSeason?.AirDate,
                ["episode_number"] = lastSeason?.EpisodeCount ?? 1,
                ["episode_type"] = "finale",
                ["runtime"] = runtime,
                ["season_number"] = lastSeason?.Number ?? 1,
                ["show_id"] = work.Id,
            }
            : null;
        details["name"] = work.Title;
        details["number_of_episodes"] = seasons.Sum(season => season.EpisodeCount);
        details["number_of_seasons"] = seasons.Count(season => season.Number > 0);
        details["original_name"] = work.OriginalTitle ?? work.Title;
        details["seasons"] = new JsonArray(
        [
            .. seasons.Select(season => (JsonNode)new JsonObject
            {
                ["air_date"] = season.AirDate,
                ["episode_count"] = season.EpisodeCount,
                ["id"] = work.Id * 100 + season.Number,
                ["name"] = season.Number == 0 ? "Спецматеріали" : $"Сезон {season.Number}",
                ["overview"] = "",
                ["poster_path"] = null,
                ["season_number"] = season.Number,
                ["vote_average"] = 0,
            }),
        ]);
        details["status"] = "Returning Series";
        details["type"] = "Scripted";
        details["vote_average"] = work.Rating;
        details["vote_count"] = work.VoteCount;
        return details;
    }

    /// <summary>What a film's and a show's details share beside their own fields.</summary>
    private static JsonObject Common(FakeTmdbWork work) => new()
    {
        ["adult"] = false,
        ["backdrop_path"] = null,
        ["origin_country"] = new JsonArray("US"),
        ["original_language"] = "en",
        ["overview"] = work.Overview ?? "",
        ["popularity"] = 10.0,
        ["poster_path"] = work.PosterPath,
        ["production_companies"] = new JsonArray(),
        ["production_countries"] = new JsonArray(),
        ["spoken_languages"] = new JsonArray(),
    };

    /// <summary>
    /// A film's crew, its directors in it; a show's cast and crew are its latest season's, which never
    /// name its creators — TMDb lists those in <c>created_by</c>.
    /// </summary>
    private JsonObject Credits(FakeTmdbWork work) => new()
    {
        ["cast"] = new JsonArray(),
        ["crew"] = work.Kind == MediaWorkKind.Series
            ? new JsonArray()
            : new JsonArray(
            [
                .. (work.DirectorIds ?? []).Select(personId => (JsonNode)new JsonObject
                {
                    ["adult"] = false,
                    ["gender"] = 0,
                    ["id"] = personId,
                    ["known_for_department"] = "Directing",
                    ["name"] = PersonName(personId),
                    ["original_name"] = PersonName(personId),
                    ["popularity"] = 1.0,
                    ["profile_path"] = null,
                    ["credit_id"] = CreditId(work, personId),
                    ["department"] = "Directing",
                    ["job"] = "Director",
                }),
            ]),
    };

    /// <summary>The works <paramref name="ids"/> of <paramref name="kind"/> as a list lists them; an id
    /// no <see cref="AddWork"/> added fails the request, naming <paramref name="listedBy"/>.</summary>
    private JsonArray Entries(MediaWorkKind kind, int[] ids, string listedBy, bool withMediaType) =>
        new(
        [
            .. ids.Select(id => (JsonNode)Entry(
                _works.TryGetValue((kind, id), out var work)
                    ? work
                    : throw new InvalidOperationException($"FakeTmdbServer: {listedBy} lists the {KindName(kind)} {id}, which no AddWork added."),
                withMediaType)),
        ]);

    /// <summary>One work as TMDb lists it among others: <c>genre_ids</c>, not <c>genres</c>, and its
    /// <c>media_type</c> where TMDb gives one (recommendations, collections, finds, credits).</summary>
    private static JsonObject Entry(FakeTmdbWork work, bool withMediaType)
    {
        var isShow = work.Kind == MediaWorkKind.Series;
        var entry = new JsonObject
        {
            ["adult"] = false,
            ["backdrop_path"] = null,
            ["genre_ids"] = new JsonArray([.. (work.GenreIds ?? []).Select(id => (JsonNode)id)]),
            ["id"] = work.Id,
        };
        if (isShow)
        {
            entry["origin_country"] = new JsonArray("US");
            entry["original_language"] = "en";
            entry["original_name"] = work.OriginalTitle ?? work.Title;
            entry["overview"] = work.Overview ?? "";
            entry["popularity"] = 10.0;
            entry["poster_path"] = work.PosterPath;
            entry["first_air_date"] = work.ReleaseDate ?? "";
            entry["name"] = work.Title;
        }
        else
        {
            entry["title"] = work.Title;
            entry["original_language"] = "en";
            entry["original_title"] = work.OriginalTitle ?? work.Title;
            entry["overview"] = work.Overview ?? "";
            entry["popularity"] = 10.0;
            entry["poster_path"] = work.PosterPath;
            entry["release_date"] = work.ReleaseDate ?? "";
            entry["video"] = false;
        }

        entry["vote_average"] = work.Rating;
        entry["vote_count"] = work.VoteCount;
        if (withMediaType)
            entry["media_type"] = isShow ? "tv" : "movie";
        return entry;
    }

    // MAM-34 — each title as TMDb lists it: a country, the title, and a type often left blank.
    private static JsonArray AlternativeTitles(FakeTmdbWork work) =>
        new([.. (work.AlternativeTitles ?? []).Select(title => (JsonNode)new JsonObject { ["iso_3166_1"] = "US", ["title"] = title, ["type"] = "" })]);

    // MAM-34 — a film's and a show's external ids as TMDb answers them, every other registry unknown.
    private static JsonObject ExternalIds(FakeTmdbWork work)
    {
        var ids = new JsonObject { ["imdb_id"] = work.ImdbId };
        if (work.Kind == MediaWorkKind.Series)
        {
            ids["freebase_mid"] = null;
            ids["freebase_id"] = null;
            ids["tvdb_id"] = null;
            ids["tvrage_id"] = null;
        }

        ids["wikidata_id"] = null;
        ids["facebook_id"] = null;
        ids["instagram_id"] = null;
        ids["twitter_id"] = null;
        return ids;
    }

    private static JsonObject Page(int page, JsonArray results, int pages) => new()
    {
        ["page"] = page,
        ["results"] = results,
        ["total_pages"] = pages,
        ["total_results"] = results.Count,
    };

    /// <summary>Details' <c>genres</c>: each id with its name from the recorded lists.</summary>
    private static JsonArray Genres(FakeTmdbWork work)
    {
        var names = work.Kind == MediaWorkKind.Series ? TvGenreNames.Value : MovieGenreNames.Value;
        return new JsonArray(
        [
            .. (work.GenreIds ?? []).Select(id => (JsonNode)new JsonObject
            {
                ["id"] = id,
                ["name"] = names.TryGetValue(id, out var name) ? name : $"Genre {id}",
            }),
        ]);
    }

    private string PersonName(int personId) => _people.TryGetValue(personId, out var name) ? name : $"Person {personId}";

    // TMDb's credit ids are 24 hexadecimal digits; these only need to be unique and stable.
    private static string CreditId(FakeTmdbWork work, int personId) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)work.Kind:x2}{work.Id:x10}{personId:x12}");

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MediaWorkKind? KindOf(string type) => type switch
    {
        "movie" => MediaWorkKind.Movie,
        "tv" => MediaWorkKind.Series,
        _ => null,
    };

    private static string KindName(MediaWorkKind kind) => kind == MediaWorkKind.Series ? "show" : "movie";

    private static bool HasToken(HttpRequestMessage request) =>
        request.Headers.NonValidated.TryGetValues("Authorization", out var values)
        && values.Any(value => string.Equals(value, $"Bearer {Token}", StringComparison.Ordinal));

    private static Dictionary<int, string> GenreNames(string sample) =>
        JsonNode.Parse(sample)!["genres"]!.AsArray()
            .Select(genre => genre!.AsObject())
            .ToDictionary(genre => (int)genre["id"]!, genre => (string)genre["name"]!);

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "",
                StringComparer.Ordinal);

    private static HttpResponseMessage NotFound() => JsonText(NotFoundBody, HttpStatusCode.NotFound);

    private static HttpResponseMessage JsonAnswer(JsonNode body, HttpStatusCode status = HttpStatusCode.OK) =>
        JsonText(body.ToJsonString(Json), status);

    private static HttpResponseMessage JsonText(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
