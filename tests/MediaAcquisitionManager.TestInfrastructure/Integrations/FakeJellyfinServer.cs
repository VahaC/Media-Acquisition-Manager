using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// A Jellyfin server behind a <see cref="RecordingHttpHandler"/> (MAM-8): it answers the requests the
/// adapter makes — <c>/System/Info</c>, <c>/Library/VirtualFolders</c>, pages of <c>/Items</c> and
/// <c>/Videos/{id}/AdditionalParts</c>, and MAM-34's <c>GET /Users</c>, <c>GET /Items/{id}</c>,
/// <c>POST /Items/RemoteSearch/{Movie|Series}</c> and <c>POST /Items/RemoteSearch/Apply/{id}</c> — from the real samples
/// (<see cref="JellyfinSamples"/>) unless a test changes them.
/// </summary>
/// <remarks>
/// <para>
/// Pages are cut from <see cref="Movies"/> in list order, honouring <c>startIndex</c> and <c>limit</c>
/// exactly as Jellyfin does, with <see cref="Movies"/>' count as the total. A library that changes
/// while it is read is a test changing <see cref="Movies"/> in <see cref="BeforeItemsPage"/>, or
/// <see cref="TotalRecordCountFor"/> reporting another total; any other failure is an answer of
/// <see cref="Intercept"/>'s.
/// </para>
/// <para>
/// MAM-32 — <c>includeItemTypes</c> picks the list: <see cref="Movies"/>, <see cref="Series"/> or
/// <see cref="Episodes"/>. The series and episodes are empty until a test fills them, so a test about
/// movies reads the library it always did; <see cref="BeforeTypedItemsPage"/> and
/// <see cref="TotalRecordCountOf"/> change any list while it is read.
/// </para>
/// <para>
/// MAM-34 — one item is read as the live check recorded it: <c>GET /Items/{id}</c> answers 400 without a <c>userId</c>
/// (Jellyfin reads an item only as a user), 404 for an id none of the lists holds, and the stored item itself otherwise.
/// <c>/Items?ids=</c> lists the items of those ids, user-less, whoever may see them, while an item in
/// <see cref="HiddenFromAdministrator"/> answers the administrator's read with Jellyfin's 404, as one a parental limit
/// hides.
/// </para>
/// <para>
/// MAM-34 (0.2.1) — Jellyfin's Identify, as recorded live on 12.1.0 (2026-10-06, ADR 0026). A search answers the
/// <see cref="Matches"/> of its endpoint that carry the id under the exact key, in the language its body asks for: a
/// lower-case <c>tmdb</c> answers <c>[]</c>, an unknown id <c>[]</c>, and a TV id searched as a film the unrelated film that
/// has that number; a body without <c>SearchInfo</c> answers 500, as the real controller dereferences it, and a search for
/// a provider the item's library doesn't list among its fetchers answers <c>[]</c>. <c>ItemId</c> only picks that
/// library's providers: it does not pick the language, which is the body's, and falls back to the server's English.
/// Apply is synchronous and does to the stored item what the live check saw: its name and overview become the result's in
/// the language of the item's own library (<c>Фільми</c>: <c>uk</c>), its year and ids the result's — a TMDb result keeps the
/// <c>Tvdb</c> ids other providers re-add, an Open Movie Database one drops a held <c>Tmdb</c> —, its images are deleted and
/// its primary image (and, from TMDb, its backdrop) given fresh tags, its Etag is new, and a series' episodes lose their
/// <c>TvMaze</c> id; an item that is locked gets its ids and Etag changed only, as in Jellyfin's source. It carries no
/// language of its own. A POST of an item or of its refresh, Jellyfin's older ways to change one, answers 404 here, so a
/// regression that posts an item fails loudly.
/// </para>
/// <para>
/// One server per address: <see cref="Arm(RecordingHttpHandler, FakeJellyfinServer[])"/> gives one
/// handler several, each answering the requests to its own <see cref="BaseUrl"/>, so two integrations
/// synced side by side each read their own library. A request to no server's address answers 503, like
/// an unarmed handler. The identification calls are thread safe, as a background worker with several lanes makes them
/// from several threads at once: the recorders are concurrent queues, the stored items and <see cref="Matches"/> are only
/// touched under one lock, and the in-flight counters are guarded. The pages of the sync are not: a test reads one
/// library at a time from each server.
/// </para>
/// </remarks>
public sealed class FakeJellyfinServer
{
    /// <summary>Where an integration's data factory points by default: port 9 on the loopback address,
    /// which refuses at once should anything reach the real network.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:9";

    /// <summary>The name Jellyfin's search gives the TMDb provider.</summary>
    public const string TmdbProvider = "TheMovieDb";

    /// <summary>The name Jellyfin's search gives the Open Movie Database provider.</summary>
    public const string OmdbProvider = "The Open Movie Database";

    // Jellyfin sends its text as UTF-8, not as \u escapes.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Uri _base;

    // The stored items and Matches are only touched under this lock (the Identify calls come from several threads).
    private readonly object _gate = new();

    // The in-flight counters of Apply, apart from _gate so a slow Apply doesn't hold the items.
    private readonly object _flightGate = new();
    private readonly Dictionary<string, int> _inFlightByType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _peakByType = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<FakeJellyfinSearch> _searches = new();
    private readonly ConcurrentQueue<FakeJellyfinApply> _applied = new();
    private int _inFlight;
    private int _peakInFlight;
    private int _applyCount;

    /// <param name="baseUrl">The address the server answers at, as an integration stores it (no
    /// trailing slash); a path is Jellyfin's base path.</param>
    public FakeJellyfinServer(string baseUrl = DefaultBaseUrl)
    {
        BaseUrl = baseUrl;
        _base = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    /// <summary>The address this server answers at.</summary>
    public string BaseUrl { get; }

    /// <summary>What <c>/System/Info</c> answers: the sample, Jellyfin 12.1.0. Set its
    /// <c>Version</c> for the version gate.</summary>
    public JsonObject SystemInfo { get; } = JellyfinSamples.SystemInfo();

    /// <summary>What <c>/Library/VirtualFolders</c> answers: the sample's five libraries.</summary>
    public JsonArray VirtualFolders { get; } = JellyfinSamples.VirtualFolders();

    /// <summary>The library's movies, paged in list order (the fake's stand-in for Jellyfin's sort
    /// order): the four samples until a test changes the list.</summary>
    public List<JsonObject> Movies { get; } = JellyfinSamples.Movies();

    /// <summary>The library's series (MAM-32): none until a test adds them.</summary>
    public List<JsonObject> Series { get; } = [];

    /// <summary>The library's episodes (MAM-32): none until a test adds them.</summary>
    public List<JsonObject> Episodes { get; } = [];

    /// <summary>MAM-34 — the ids of the items the administrator may not see (a parental limit, a blocked tag): its read of
    /// one answers 404, while the user-less lists still hold it.</summary>
    public HashSet<string> HiddenFromAdministrator { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The further parts of each stacked movie by its id, for
    /// <c>/Videos/{id}/AdditionalParts</c> (<see cref="JellyfinSamples.Part"/>); another id answers 404.</summary>
    public Dictionary<string, List<JsonObject>> AdditionalParts { get; } = new(StringComparer.Ordinal);

    /// <summary>MAM-34 — what <c>GET /Users</c> answers: the sample's member, then its enabled administrator. A test
    /// changes a user's <c>Policy</c> (<c>IsAdministrator</c>, <c>IsDisabled</c>) or the list.</summary>
    public JsonArray Users { get; } = JellyfinSamples.Users();

    /// <summary>
    /// MAM-34 — what the metadata providers hold for an id: the results a search answers and an Apply gives the item. A
    /// test adds them before the run (<see cref="AddFilm"/>, <see cref="AddShow"/>, <see cref="AddEpisodeMovie"/>) and does
    /// not touch the list while requests are being answered.
    /// </summary>
    public List<FakeJellyfinMatch> Matches { get; } = [];

    /// <summary>MAM-34 — every search received, oldest first (concurrent safe; read it with <c>ToArray()</c> after the
    /// run).</summary>
    public IReadOnlyCollection<FakeJellyfinSearch> RemoteSearches => _searches;

    /// <summary>MAM-34 — every Apply the server took, oldest first: whatever it then did to the item, and an Apply whose
    /// answer a test lost; not one that was refused (<see cref="ApplyRefusal"/>), nor one of an item it doesn't have.</summary>
    public IReadOnlyCollection<FakeJellyfinApply> Applied => _applied;

    /// <summary>MAM-34 — when set, a search answers this status with no body: a refused one (403 for a key that isn't an
    /// administrator's, 400, 500).</summary>
    public HttpStatusCode? SearchRefusal { get; set; }

    /// <summary>MAM-34 — when set, an Apply answers this status with no body and applies nothing: a refused one (403 for a
    /// key that isn't an administrator's, 400 for a body Jellyfin won't take).</summary>
    public HttpStatusCode? ApplyRefusal { get; set; }

    /// <summary>MAM-34 — when set, an Apply answers its 204 and applies nothing: one Jellyfin took that the read after it
    /// won't confirm.</summary>
    public bool IgnoresApply { get; set; }

    /// <summary>
    /// MAM-34 — when set, an Apply answers its 204 having done what a real one leaves when the metadata phase fails: the ids
    /// replaced, the name and year not updated, the images deleted (<c>replaceAllImages</c> deletes them before the
    /// providers run) and the Etag changed. An "untouched Etag" would hide the lost-poster case.
    /// </summary>
    public bool AppliesIdsOnly { get; set; }

    /// <summary>
    /// MAM-34 — runs once an Apply was applied, given the item's id as posted: it throws to lose the answer of one that
    /// landed (as a transport failure would), or returns an answer of its own — a stalled one, a 500 — instead of the
    /// recorded 204; <c>null</c> lets the server answer.
    /// </summary>
    public Func<string, HttpResponseMessage?>? AfterApply { get; set; }

    /// <summary>MAM-34 — how long an Apply takes before it applies: the handler's thread sleeps (a series took 159 s live),
    /// so a test sees Applies overlap. Zero when unset.</summary>
    public TimeSpan ApplyDelay { get; set; }

    /// <summary>MAM-34 — the most Applies that were being answered at once since the server was made.</summary>
    public int PeakAppliesInFlight
    {
        get
        {
            lock (_flightGate)
                return _peakInFlight;
        }
    }

    /// <summary>MAM-34 — the same by the type of the item applied (<c>Movie</c>, <c>Series</c>): a worker that identifies
    /// one series at a time shows 1 for <c>Series</c>.</summary>
    public IReadOnlyDictionary<string, int> PeakAppliesInFlightByType
    {
        get
        {
            lock (_flightGate)
                return new Dictionary<string, int>(_peakByType, StringComparer.Ordinal);
        }
    }

    /// <summary>When set, a request without <c>Authorization: MediaBrowser Token="{ApiKey}"</c> answers
    /// 401, as Jellyfin does for a key it doesn't know.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Answers a request before the server does — a failure, a redirect, a stalled body —
    /// or returns <c>null</c> to let the server answer.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>Runs before a page of <c>/Items</c> is cut, given its <c>startIndex</c>: where a test
    /// changes <see cref="Movies"/> between two pages, or removes the integration mid-sync.</summary>
    public Action<int>? BeforeItemsPage { get; set; }

    /// <summary>The <c>TotalRecordCount</c> a page reports, given its <c>startIndex</c>;
    /// <see cref="Movies"/>' count when unset.</summary>
    public Func<int, int>? TotalRecordCountFor { get; set; }

    /// <summary>MAM-32 — runs before a page of any list is cut, given the list's type
    /// (<c>Movie</c>, <c>Series</c>, <c>Episode</c>) and the page's <c>startIndex</c>.</summary>
    public Action<string, int>? BeforeTypedItemsPage { get; set; }

    /// <summary>MAM-32 — the <c>TotalRecordCount</c> a page of any list reports, given its type and
    /// <c>startIndex</c>; the list's count when unset or <c>null</c>.</summary>
    public Func<string, int, int?>? TotalRecordCountOf { get; set; }

    /// <summary>
    /// MAM-34 — the <c>LibraryOptions</c> of the library named <paramref name="libraryNameOrRoot"/> (its name, or one of its
    /// root folders such as <see cref="JellyfinSamples.CartoonsRoot"/>), as <c>/Library/VirtualFolders</c> answers them: the
    /// live object, so a test flips <c>SaveLocalMetadata</c>, the savers, the metadata language or the fetchers of its
    /// <c>TypeOptions</c> before the run.
    /// </summary>
    public JsonObject LibraryOptionsOf(string libraryNameOrRoot)
    {
        var folder = VirtualFolders.OfType<JsonObject>().FirstOrDefault(candidate =>
            string.Equals((string?)candidate["Name"], libraryNameOrRoot, StringComparison.Ordinal)
            || (candidate["Locations"] is JsonArray locations && locations.Any(location => string.Equals((string?)location, libraryNameOrRoot, StringComparison.OrdinalIgnoreCase))));
        return folder?["LibraryOptions"] as JsonObject
            ?? throw new InvalidOperationException($"The fake Jellyfin has no library “{libraryNameOrRoot}”.");
    }

    /// <summary>
    /// MAM-34 — adds what TMDb's film provider holds for a film: a result carrying <paramref name="tmdbId"/> (and
    /// <paramref name="imdbId"/> when given), found by a <c>Movie</c> search for <c>Tmdb</c>.
    /// </summary>
    /// <param name="tmdbId">The film's TMDb id.</param>
    /// <param name="name">The server-default (English) name.</param>
    /// <param name="year">The production year.</param>
    /// <param name="imdbId">The film's IMDb id TMDb gives; none when <c>null</c>.</param>
    /// <param name="overview">The overview; a sentence made from the name when <c>null</c>, none when empty.</param>
    /// <param name="namesByLanguage">The name in other languages (<c>uk</c>), for a library that has one.</param>
    /// <param name="overviewsByLanguage">The overview in other languages.</param>
    public FakeJellyfinMatch AddFilm(
        string tmdbId,
        string name,
        int year,
        string? imdbId = null,
        string? overview = null,
        IReadOnlyDictionary<string, string>? namesByLanguage = null,
        IReadOnlyDictionary<string, string>? overviewsByLanguage = null) =>
        AddMatch(new FakeJellyfinMatch(
            "Movie",
            TmdbProvider,
            "Tmdb",
            tmdbId,
            TmdbRow(name, year, overview, new JsonObject { ["Tmdb"] = tmdbId, ["Imdb"] = imdbId }, tmdbId),
            namesByLanguage,
            overviewsByLanguage));

    /// <summary>
    /// MAM-34 — adds what TMDb's show provider holds for a show: a result found by a <c>Series</c> search for <c>Tmdb</c>
    /// (a show's TMDb number is its own), with its IMDb and TVDB ids when given.
    /// </summary>
    public FakeJellyfinMatch AddShow(
        string tmdbId,
        string name,
        int year,
        string? imdbId = null,
        string? tvdbId = null,
        string? overview = null,
        IReadOnlyDictionary<string, string>? namesByLanguage = null,
        IReadOnlyDictionary<string, string>? overviewsByLanguage = null) =>
        AddMatch(new FakeJellyfinMatch(
            "Series",
            TmdbProvider,
            "Tmdb",
            tmdbId,
            TmdbRow(name, year, overview, new JsonObject { ["Tmdb"] = tmdbId, ["Imdb"] = imdbId, ["Tvdb"] = tvdbId }, tmdbId),
            namesByLanguage,
            overviewsByLanguage));

    /// <summary>
    /// MAM-34 — adds what the Open Movie Database holds for an episode Jellyfin lists as a film: a result found by a
    /// <c>Movie</c> search for <c>Imdb</c> through that provider, with an IMDb id only and no overview, as the live check
    /// recorded for Blitzkrieg.
    /// </summary>
    public FakeJellyfinMatch AddEpisodeMovie(string imdbId, string name, int year) =>
        AddMatch(new FakeJellyfinMatch(
            "Movie",
            OmdbProvider,
            "Imdb",
            imdbId,
            new JsonObject
            {
                ["Name"] = name,
                ["ProviderIds"] = new JsonObject { ["Imdb"] = imdbId },
                ["ProductionYear"] = year,
                ["PremiereDate"] = string.Create(CultureInfo.InvariantCulture, $"{year}-05-02T00:00:00.0000000Z"),
                ["ImageUrl"] = $"https://m.media-amazon.com/images/M/{imdbId}._V1_SX300.jpg",
                ["SearchProviderName"] = OmdbProvider,
                ["Artists"] = new JsonArray(),
            }));

    /// <summary>Makes <paramref name="handler"/> answer as this server.</summary>
    public void Arm(RecordingHttpHandler handler) => Arm(handler, this);

    /// <summary>Makes <paramref name="handler"/> answer as <paramref name="servers"/>, each at its own
    /// address; a request to none of them answers 503.</summary>
    public static void Arm(RecordingHttpHandler handler, params FakeJellyfinServer[] servers)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler.Handler = (request, body) =>
            Array.Find(servers, server => server.Serves(request.RequestUri!)) is { } server
                ? server.Answer(request, body)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>What this server answers <paramref name="request"/>, whose body, read as text, is
    /// <paramref name="body"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Intercept?.Invoke(request) is { } intercepted)
            return intercepted;

        var uri = request.RequestUri!;
        var path = Uri.UnescapeDataString(uri.AbsolutePath[_base.AbsolutePath.Length..]);
        var segments = path.Split('/');
        if (ApiKey is not null && !HasKey(request, ApiKey))
            return Unauthorized(segments);

        return path switch
        {
            "System/Info" => JsonAnswer(SystemInfo),
            "Library/VirtualFolders" => JsonAnswer(VirtualFolders),
            "Items" => ItemsPage(uri),
            "Users" when request.Method == HttpMethod.Get => JsonAnswer(Users),
            _ when segments is ["Items", var itemId] && request.Method == HttpMethod.Get => ItemOf(uri, itemId),
            _ when segments is ["Items", "RemoteSearch", "Apply", var itemId] && request.Method == HttpMethod.Post => Apply(uri, itemId, body),
            _ when segments is ["Items", "RemoteSearch", ("Movie" or "Series") and var endpoint] && request.Method == HttpMethod.Post => RemoteSearch(endpoint, body),
            _ when segments is ["Videos", var itemId, "AdditionalParts"] => PartsOf(itemId),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private bool Serves(Uri uri) =>
        Uri.Compare(uri, _base, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
        && uri.AbsolutePath.StartsWith(_base.AbsolutePath, StringComparison.Ordinal);

    // MAM-34 — the recorded 401 of the call a wrong key was sent to: the search's and Apply's differ from a read's.
    private static HttpResponseMessage Unauthorized(string[] segments) => segments switch
    {
        ["Items", "RemoteSearch", "Apply", _] => VendorResponses.Recorded(JellyfinSamples.RemoteSearchApplyWrongKeyResponseFile),
        ["Items", "RemoteSearch", _] => VendorResponses.Recorded(JellyfinSamples.RemoteSearchWrongKeyResponseFile),
        _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
    };

    private HttpResponseMessage ItemsPage(Uri uri)
    {
        var query = Query(uri);
        var startIndex = query.TryGetValue("startIndex", out var start) ? int.Parse(start, CultureInfo.InvariantCulture) : 0;
        var limit = query.TryGetValue("limit", out var size) ? int.Parse(size, CultureInfo.InvariantCulture) : int.MaxValue;
        var type = query.TryGetValue("includeItemTypes", out var types) ? types : "Movie";
        if (query.TryGetValue("ids", out var ids))
        {
            lock (_gate)
                return Listed([.. Movies.Concat(Series).Concat(Episodes).Where(item => ids.Split(',').Any(id => SameId((string?)item["Id"], id)))], startIndex, limit);
        }

        BeforeTypedItemsPage?.Invoke(type, startIndex);
        List<JsonObject> list;
        int? total;
        switch (type)
        {
            case "Series":
                list = Series;
                total = TotalRecordCountOf?.Invoke(type, startIndex);
                break;
            case "Episode":
                list = Episodes;
                total = TotalRecordCountOf?.Invoke(type, startIndex);
                break;
            default:
                BeforeItemsPage?.Invoke(startIndex);
                list = Movies;
                total = TotalRecordCountOf?.Invoke(type, startIndex) ?? TotalRecordCountFor?.Invoke(startIndex);
                break;
        }

        var page = new JsonArray([.. list.Skip(startIndex).Take(limit).Select(item => item.DeepClone())]);
        return JsonAnswer(new JsonObject
        {
            ["Items"] = page,
            ["TotalRecordCount"] = total ?? list.Count,
            ["StartIndex"] = startIndex,
        });
    }

    // MAM-34 — a page of a user-less list other than the sync's.
    private static HttpResponseMessage Listed(List<JsonObject> list, int startIndex, int limit) =>
        JsonAnswer(new JsonObject
        {
            ["Items"] = new JsonArray([.. list.Skip(startIndex).Take(limit).Select(item => item.DeepClone())]),
            ["TotalRecordCount"] = list.Count,
            ["StartIndex"] = startIndex,
        });

    // ── MAM-34: one item read ────────────────────────────────────────────────

    private HttpResponseMessage ItemOf(Uri uri, string itemId)
    {
        if (!Query(uri).TryGetValue("userId", out var userId) || userId.Length == 0)
            return VendorResponses.Recorded(JellyfinSamples.ItemReadWithoutUserResponseFile);

        lock (_gate)
        {
            return Stored(itemId) is { } item && !HiddenFromAdministrator.Any(hidden => SameId((string?)item["Id"], hidden))
                ? JsonAnswer(item.DeepClone())
                : VendorResponses.Recorded(JellyfinSamples.ItemReadUnknownItemResponseFile);
        }
    }

    // ── MAM-34: Identify — the search ────────────────────────────────────────

    private HttpResponseMessage RemoteSearch(string endpoint, string? body)
    {
        _searches.Enqueue(new FakeJellyfinSearch(endpoint, body ?? ""));
        if (SearchRefusal is { } refusal)
            return new HttpResponseMessage(refusal);
        // The real controller dereferences the body's SearchInfo: without it the answer is a 500.
        if (Posted(body) is not { } posted || posted["SearchInfo"] is not JsonObject info)
            return VendorResponses.Text("Error processing request.", HttpStatusCode.InternalServerError);

        var provider = (string?)posted["SearchProviderName"];
        var itemId = (string?)posted["ItemId"];
        var language = (string?)info["MetadataLanguage"];
        var wanted = info["ProviderIds"] is JsonObject providerIds ? providerIds.FirstOrDefault() : default;
        if (wanted.Key is null)
            return JsonAnswer(new JsonArray());

        lock (_gate)
        {
            // ItemId only picks the providers from the item's library options (IncludeDisabledProviders is false).
            IReadOnlyList<string>? fetchers = itemId is not null && Stored(itemId) is { } item ? FetchersOf(item) : null;
            if (provider is not null && fetchers is not null && !fetchers.Contains(provider, StringComparer.Ordinal))
                return JsonAnswer(new JsonArray());

            return JsonAnswer(new JsonArray([
                .. Matches
                    .Where(match => string.Equals(match.Endpoint, endpoint, StringComparison.Ordinal)
                        && string.Equals(match.Key, wanted.Key, StringComparison.Ordinal)
                        && string.Equals(match.Value, (string?)wanted.Value, StringComparison.Ordinal)
                        && (provider is null || string.Equals(match.Provider, provider, StringComparison.Ordinal))
                        && (provider is not null || fetchers is null || fetchers.Contains(match.Provider, StringComparer.Ordinal)))
                    .Select(match => (JsonNode)RowFor(match, language)),
            ]));
        }
    }

    // The result as the search answers it: the name and overview in the language the body asked for, else the server's.
    private static JsonObject RowFor(FakeJellyfinMatch match, string? language)
    {
        var row = match.Row.DeepClone().AsObject();
        if (ByLanguage(match.NamesByLanguage, language) is { } name)
            row["Name"] = name;
        if (ByLanguage(match.OverviewsByLanguage, language) is { } overview)
            row["Overview"] = overview;
        return row;
    }

    private static string? ByLanguage(IReadOnlyDictionary<string, string>? values, string? language) =>
        values is null || string.IsNullOrWhiteSpace(language)
            ? null
            : values.FirstOrDefault(pair => string.Equals(pair.Key, language, StringComparison.OrdinalIgnoreCase)).Value;

    // ── MAM-34: Identify — Apply ─────────────────────────────────────────────

    private HttpResponseMessage Apply(Uri uri, string itemId, string? body)
    {
        if (ApplyRefusal is { } refusal)
            return new HttpResponseMessage(refusal);
        if (Posted(body) is not { } row)
            return VendorResponses.Text("Error processing request.", HttpStatusCode.BadRequest);

        Query(uri).TryGetValue("replaceAllImages", out var replaceAllImagesText);
        var replaceAllImages = string.Equals(replaceAllImagesText, "true", StringComparison.OrdinalIgnoreCase);

        string itemType;
        lock (_gate)
        {
            if (Stored(itemId) is not { } found)
                return VendorResponses.Recorded(JellyfinSamples.RemoteSearchApplyUnknownItemResponseFile);
            itemType = (string?)found["Type"] ?? "";
        }

        var startedAt = Stopwatch.GetTimestamp();
        EnterFlight(itemType);
        try
        {
            // Apply is synchronous and slow (a series took 159 s live): the handler's thread is what waits.
            if (ApplyDelay > TimeSpan.Zero)
                Thread.Sleep(ApplyDelay);

            lock (_gate)
            {
                if (!IgnoresApply && Stored(itemId) is { } item)
                    ApplyTo(item, row, replaceAllImages);
            }
        }
        finally
        {
            LeaveFlight(itemType);
        }

        _applied.Enqueue(new FakeJellyfinApply(itemId, replaceAllImagesText, row, startedAt, Stopwatch.GetTimestamp()));
        // The recorded 204 of each kind of item.
        return AfterApply?.Invoke(itemId)
            ?? VendorResponses.Recorded(
                itemType == "Series" ? JellyfinSamples.SeriesRemoteSearchApplyResponseFile
                : string.Equals((string?)row["SearchProviderName"], OmdbProvider, StringComparison.Ordinal) ? JellyfinSamples.EpisodeMovieRemoteSearchApplyResponseFile
                : JellyfinSamples.ItemRemoteSearchApplyResponseFile);
    }

    /// <summary>What Apply does to the stored item, as the live check saw it (caller holds <c>_gate</c>).</summary>
    private void ApplyTo(JsonObject item, JsonObject row, bool replaceAllImages)
    {
        var number = ++_applyCount;
        var isTmdbRow = string.Equals((string?)row["SearchProviderName"], TmdbProvider, StringComparison.Ordinal);
        var match = MatchOf(row);
        var language = LanguageOf(item);
        item["Etag"] = string.Create(CultureInfo.InvariantCulture, $"applied-etag-{number}");

        // The ids are replaced by the result's, and the ones other providers add are put back: Tvdb for a TMDb result.
        item["ProviderIds"] = ProviderIdsAfter(item, row, isTmdbRow);

        // Recorded in Jellyfin's source: a locked item takes the ids and a new Etag, and nothing else.
        if (item["LockData"] is JsonValue lockData && lockData.TryGetValue<bool>(out var locked) && locked)
            return;

        if (AppliesIdsOnly)
        {
            // The images are deleted before the providers run, and the providers failed.
            item["ImageTags"] = new JsonObject();
            item["BackdropImageTags"] = new JsonArray();
            return;
        }

        if (!IsLockedField(item, "Name"))
            item["Name"] = ByLanguage(match?.NamesByLanguage, language) ?? (string?)match?.Row["Name"] ?? (string?)row["Name"];
        if (row["ProductionYear"] is JsonValue year && year.TryGetValue<int>(out var productionYear))
            item["ProductionYear"] = productionYear;

        // The overview is replaced when the result has one: an Open Movie Database episode result has none, and the item keeps
        // what it had (recorded).
        var overview = ByLanguage(match?.OverviewsByLanguage, language) ?? (string?)match?.Row["Overview"] ?? (string?)row["Overview"];
        if (!string.IsNullOrWhiteSpace(overview) && !IsLockedField(item, "Overview"))
            item["Overview"] = overview;

        if (replaceAllImages)
        {
            item["ImageTags"] = new JsonObject();
            item["BackdropImageTags"] = new JsonArray();
        }

        if (!string.IsNullOrWhiteSpace((string?)row["ImageUrl"]))
        {
            if (item["ImageTags"] is not JsonObject tags)
            {
                tags = new JsonObject();
                item["ImageTags"] = tags;
            }

            tags["Primary"] = string.Create(CultureInfo.InvariantCulture, $"applied-primary-{number}");
            // Only TMDb has backdrops: the Open Movie Database result left none (recorded).
            if (replaceAllImages && isTmdbRow)
                item["BackdropImageTags"] = new JsonArray(string.Create(CultureInfo.InvariantCulture, $"applied-backdrop-{number}"));
        }

        // Recorded: a series' episodes lost their TvMaze id.
        if (string.Equals((string?)item["Type"], "Series", StringComparison.Ordinal))
        {
            foreach (var episode in Episodes.Where(episode => SameId((string?)episode["SeriesId"], (string?)item["Id"] ?? "")))
            {
                if (episode["ProviderIds"] is JsonObject episodeIds)
                    episodeIds.Remove("TvMaze");
            }
        }
    }

    // The result's ids in its order, then — for a TMDb result — the Tvdb ids the item held, which other providers re-add.
    private static JsonObject ProviderIdsAfter(JsonObject item, JsonObject row, bool isTmdbRow)
    {
        var ids = new JsonObject();
        if (row["ProviderIds"] is JsonObject rowIds)
        {
            foreach (var pair in rowIds.Where(pair => pair.Value is not null))
                ids[pair.Key] = pair.Value!.DeepClone();
        }

        if (isTmdbRow && item["ProviderIds"] is JsonObject held)
        {
            foreach (var key in new[] { "Tvdb", "TvdbSlug" })
            {
                if (!ids.ContainsKey(key) && held[key] is { } value)
                    ids[key] = value.DeepClone();
            }
        }

        return ids;
    }

    private static bool IsLockedField(JsonObject item, string field) =>
        item["LockedFields"] is JsonArray fields && fields.Any(locked => string.Equals((string?)locked, field, StringComparison.OrdinalIgnoreCase));

    // The match the posted result is: found by its provider and the id it carries, so its names in other languages are known.
    private FakeJellyfinMatch? MatchOf(JsonObject row)
    {
        var provider = (string?)row["SearchProviderName"];
        return row["ProviderIds"] is JsonObject ids
            ? Matches.FirstOrDefault(match =>
                string.Equals(match.Provider, provider, StringComparison.Ordinal)
                && ids.TryGetPropertyValue(match.Key, out var value)
                && string.Equals((string?)value, match.Value, StringComparison.Ordinal))
            : null;
    }

    // ── MAM-34: libraries ────────────────────────────────────────────────────

    // The library whose location holds the item's path, found as the adapter finds it.
    private JsonObject? LibraryOf(JsonObject item)
    {
        if ((string?)item["Path"] is not { Length: > 0 } itemPath)
            return null;
        var path = itemPath.Replace('\\', '/').TrimEnd('/');
        return VirtualFolders.OfType<JsonObject>().FirstOrDefault(folder =>
            folder["Locations"] is JsonArray locations
            && locations.Select(location => (string?)location).OfType<string>().Any(location =>
            {
                var root = location.Replace('\\', '/').TrimEnd('/');
                return string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
            }));
    }

    // The metadata language of the item's library: what Apply refreshes the item in. Blank is the server's English.
    private string? LanguageOf(JsonObject item) => (string?)(LibraryOf(item)?["LibraryOptions"] as JsonObject)?["PreferredMetadataLanguage"];

    // The metadata fetchers the item's library enables for its type; null when it lists none for the type (all are on).
    private IReadOnlyList<string>? FetchersOf(JsonObject item)
    {
        var typeOptions = (LibraryOf(item)?["LibraryOptions"] as JsonObject)?["TypeOptions"] as JsonArray;
        var forType = typeOptions?.OfType<JsonObject>().FirstOrDefault(options => string.Equals((string?)options["Type"], (string?)item["Type"], StringComparison.Ordinal));
        return forType?["MetadataFetchers"] is JsonArray fetchers ? [.. fetchers.Select(fetcher => (string?)fetcher).OfType<string>()] : null;
    }

    // ── MAM-34: Apply's concurrency ──────────────────────────────────────────

    private void EnterFlight(string itemType)
    {
        lock (_flightGate)
        {
            _inFlight++;
            _peakInFlight = Math.Max(_peakInFlight, _inFlight);
            var inType = _inFlightByType.GetValueOrDefault(itemType) + 1;
            _inFlightByType[itemType] = inType;
            _peakByType[itemType] = Math.Max(_peakByType.GetValueOrDefault(itemType), inType);
        }
    }

    private void LeaveFlight(string itemType)
    {
        lock (_flightGate)
        {
            _inFlight--;
            _inFlightByType[itemType]--;
        }
    }

    // ── rows the test adds ───────────────────────────────────────────────────

    private FakeJellyfinMatch AddMatch(FakeJellyfinMatch match)
    {
        lock (_gate)
            Matches.Add(match);
        return match;
    }

    // A TMDb result as the live check recorded one; an id left null is left out, as TMDb leaves out what it doesn't know.
    private static JsonObject TmdbRow(string name, int year, string? overview, JsonObject providerIds, string tmdbId)
    {
        foreach (var key in providerIds.Where(pair => pair.Value is null).Select(pair => pair.Key).ToList())
            providerIds.Remove(key);

        var row = new JsonObject
        {
            ["Name"] = name,
            ["ProviderIds"] = providerIds,
            ["ProductionYear"] = year,
            ["PremiereDate"] = string.Create(CultureInfo.InvariantCulture, $"{year}-05-17T21:00:00.0000000Z"),
            ["ImageUrl"] = $"https://image.tmdb.org/t/p/original/{tmdbId}.jpg",
            ["SearchProviderName"] = TmdbProvider,
            ["Overview"] = overview ?? $"TMDb's overview of {name}.",
            ["Artists"] = new JsonArray(),
        };
        return row;
    }

    // The item of any list, by its id in any form Jellyfin takes ("N" or with dashes, any letter case). Caller holds _gate.
    private JsonObject? Stored(string itemId) =>
        Movies.Concat(Series).Concat(Episodes).FirstOrDefault(item => SameId((string?)item["Id"], itemId));

    private static bool SameId(string? stored, string requested) =>
        stored is not null
        && (Guid.TryParse(stored, out var storedGuid) && Guid.TryParse(requested, out var requestedGuid)
            ? storedGuid == requestedGuid
            : string.Equals(stored, requested, StringComparison.OrdinalIgnoreCase));

    private static JsonObject? Posted(string? body)
    {
        try
        {
            return body is null ? null : JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HttpResponseMessage PartsOf(string itemId)
    {
        if (!AdditionalParts.TryGetValue(itemId, out var parts))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        return JsonAnswer(new JsonObject
        {
            ["Items"] = new JsonArray([.. parts.Select(part => part.DeepClone())]),
            ["TotalRecordCount"] = parts.Count,
            ["StartIndex"] = 0,
        });
    }

    private static bool HasKey(HttpRequestMessage request, string apiKey) =>
        request.Headers.NonValidated.TryGetValues("Authorization", out var values)
        && values.Any(value => string.Equals(value, $"MediaBrowser Token=\"{apiKey}\"", StringComparison.Ordinal));

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "",
                StringComparer.OrdinalIgnoreCase);

    private static HttpResponseMessage JsonAnswer(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json") };
}
