using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — the catalog's single resources: a film's and a show's details, a collection and a person, from
// TMDb's real answers (VendorSamples/Tmdb) read field by field, edited copies of them for what they don't
// show, and a resource TMDb doesn't have. MAM-34 — the details' runtime, overview, alternative titles and a show's
// IMDb id, from the answers the live check recorded with them appended (Landing Stripling, Planet Earth).
public sealed class TmdbAdapterWorksTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ3b3JrcyJ9.c2lnbmF0dXJlLW9mLXdvcmtz";

    private const string NotTmdbError =
        "The service at this address answered, but not the way TMDb's API does. Check the address: TMDb's is https://api.themoviedb.org.";

    // MAM-46 — the language an upgraded installation's TMDb keeps, which every exact URL below was recorded with.
    private const string CatalogLanguage = "uk-UA";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token, CatalogLanguage);

    private readonly IntegrationAdapterHarness _harness = new();

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetWork_of_a_movie_asks_for_its_details_credits_and_recommendations_in_ukrainian()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsFile, TmdbSamples.MediaType);

        //Act
        await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        // One request for all five — MAM-34 appended the alternative titles and the external ids — as the live check
        // sent it (VendorSamples/Tmdb/movie-details-short.json).
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"{BaseUrl}/3/movie/118340?append_to_response=credits,recommendations,alternative_titles,external_ids&language=uk-UA",
            request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
        Assert.DoesNotContain(Token, request.Uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWork_of_a_movie_reads_the_details_sample()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var work = result.Value!;
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, "118340", "Вартові галактики", "Guardians of the Galaxy", "en", new DateOnly(2014, 7, 30),
                work.Summary.GenreIds, 7.908, 30396, "/esaQNJKjE8IDk5G0tmEOJL7AaEP.jpg"),
            work.Summary);
        // Details give genres as objects; their ids in TMDb's order.
        Assert.Equal([28, 878, 12], work.Summary.GenreIds);
        Assert.Equal(TmdbSamples.MovieDetailsImdbId, work.ImdbId);
        Assert.Equal(new CatalogReference(TmdbSamples.CollectionId, "Вартові галактики | Колекція"), work.Collection);
        // The crew's one Director job: James Gunn, in the catalog's language.
        Assert.Equal([new CatalogReference(TmdbSamples.DirectorId, "Джеймс Ґанн")], work.Directors);
        Assert.Empty(work.Seasons);
        Assert.Equal(20, work.Recommendations.Count);
        Assert.All(work.Recommendations, recommendation => Assert.Equal(MediaWorkKind.Movie, recommendation.Kind));
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, "447365", "Вартові галактики 3", "Guardians of the Galaxy Vol. 3", "en", new DateOnly(2023, 5, 3),
                work.Recommendations[0].GenreIds, 7.934, 8905, "/ebKQqIjplEhGtyi2XOfwaBIcpi2.jpg"),
            work.Recommendations[0]);
        Assert.Equal([878, 12, 28], work.Recommendations[0].GenreIds);
        Assert.Equal("1061474", work.Recommendations[^1].Id);
        // MAM-34 — its runtime and overview; recorded before the alternative titles were asked for, so none.
        Assert.Equal(121, work.RuntimeMinutes);
        Assert.StartsWith("Пітер Квіл стає жертвою мисливців за головами", work.Overview, StringComparison.Ordinal);
        Assert.Empty(work.AlternativeTitles!);
    }

    [Fact]
    public async Task GetWork_of_a_show_reads_the_tv_details_sample_with_its_seasons_and_creators()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.TvDetailsFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Series, TmdbSamples.TvDetailsId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(
            $"{BaseUrl}/3/tv/73586?append_to_response=credits,recommendations,alternative_titles,external_ids&language=uk-UA",
            request.Uri.AbsoluteUri);
        var work = result.Value!;
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Series, "73586", "Єллоустоун", "Yellowstone", "en", new DateOnly(2018, 6, 20),
                work.Summary.GenreIds, 8.274, 3360, "/b5SrNjmklwnXN2xXwb0coWu2tna.jpg"),
            work.Summary);
        Assert.Equal([37, 18], work.Summary.GenreIds);
        // TMDb's show details carry no IMDb id without external_ids — this sample was recorded before MAM-34 appended
        // them — and a show is in no collection.
        Assert.Null(work.ImdbId);
        // MAM-34 — episode_run_time is empty: the runtime is the latest episode's.
        Assert.Equal(88, work.RuntimeMinutes);
        Assert.StartsWith("Єллоустон — перший в світі національний парк", work.Overview, StringComparison.Ordinal);
        Assert.Empty(work.AlternativeTitles!);
        Assert.Null(work.Collection);
        Assert.Equal(
            [new CatalogReference("1215399", "Тейлор Шерідан"), new CatalogReference("69414", "Джон Лінсон")],
            work.Directors);
        Assert.Equal(
            [
                new CatalogSeason(1, new DateOnly(2018, 6, 20), 9),
                new CatalogSeason(2, new DateOnly(2019, 6, 19), 10),
                new CatalogSeason(3, new DateOnly(2020, 6, 21), 10),
                new CatalogSeason(4, new DateOnly(2021, 11, 7), 10),
                new CatalogSeason(5, new DateOnly(2022, 11, 13), 14),
            ],
            work.Seasons);
        Assert.Equal(20, work.Recommendations.Count);
        Assert.All(work.Recommendations, recommendation => Assert.Equal(MediaWorkKind.Series, recommendation.Kind));
        Assert.Equal(("299167", "Ранчо Даттонів"), (work.Recommendations[0].Id, work.Recommendations[0].Title));
        // A show without a Ukrainian title: TMDb's own fallback, the original.
        Assert.Contains(work.Recommendations, recommendation => recommendation is { Id: "122235", Title: "Joe Pickett" });
    }

    [Fact]
    public async Task GetWork_of_a_short_reads_its_runtime_and_imdb_id_and_no_overview_or_alternative_titles()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsShortFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(
            $"{BaseUrl}/3/movie/223021?append_to_response=credits,recommendations,alternative_titles,external_ids&language=uk-UA",
            request.Uri.AbsoluteUri);
        var work = result.Value!;
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, "223021", "Landing Stripling", "Landing Stripling", "en", new DateOnly(1962, 5, 18),
                work.Summary.GenreIds, 4.579, 19, "/ooZQbu20BBYxInF542jHonKz4Pl.jpg"),
            work.Summary);
        Assert.Equal([16, 35], work.Summary.GenreIds);
        // A Tom & Jerry short: 6 minutes, the runtime a library file's measured duration is compared with.
        Assert.Equal(6, work.RuntimeMinutes);
        Assert.Equal(TmdbSamples.MovieDetailsShortImdbId, work.ImdbId);
        Assert.Null(work.Collection);
        Assert.Equal([new CatalogReference("1058682", "Джин Діч")], work.Directors);
        // TMDb's "" — no Ukrainian overview — is none, and its alternative titles are an empty list.
        Assert.Null(work.Overview);
        Assert.Empty(work.AlternativeTitles!);
        Assert.Equal(20, work.Recommendations.Count);
        Assert.Equal(("73632", "The Million Dollar Cat"), (work.Recommendations[0].Id, work.Recommendations[0].Title));
    }

    [Fact]
    public async Task GetWork_of_a_show_reads_the_planet_earth_sample_with_its_runtime_imdb_id_and_each_alternative_title_once()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.TvDetailsPlanetEarthFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Series, TmdbSamples.TvDetailsPlanetEarthId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(
            $"{BaseUrl}/3/tv/1044?append_to_response=credits,recommendations,alternative_titles,external_ids&language=uk-UA",
            request.Uri.AbsoluteUri);
        var work = result.Value!;
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Series, "1044", "Планета Земля", "Planet Earth", "en", new DateOnly(2006, 3, 5),
                work.Summary.GenreIds, 8.639, 1294, "/ajNcvf2fvOmus8rxPdpoGrdL4OI.jpg"),
            work.Summary);
        Assert.Equal([99], work.Summary.GenreIds);
        // episode_run_time is [], so the latest episode's 53 minutes; the IMDb id only external_ids gives a show.
        Assert.Equal(53, work.RuntimeMinutes);
        Assert.Equal(TmdbSamples.TvDetailsPlanetEarthImdbId, work.ImdbId);
        Assert.Equal([new CatalogSeason(0, new DateOnly(2006, 3, 5), 24), new CatalogSeason(1, new DateOnly(2006, 3, 5), 11)], work.Seasons);
        Assert.Empty(work.Directors);
        // 16 recorded, "Planet Earth" three times (BA, HR, US): each once, in TMDb's order.
        Assert.Equal(
            [
                "Planet Earth", "Planeta Terra: A Terra como Você Nunca Viu", "地球脉动 第 1 季", "Zázračná planeta", "Planet Erde",
                "Planeta Tierra", "Planète Terre 2", "BBC Planet Earth", "Πλανήτης Γη", "살아있는 지구", "플래닛 어스", "Przyszłość Ziemi",
                "Zázračná planéta", "Planet Earth: The Complete Collection",
            ],
            work.AlternativeTitles);
        // The overview cleaned: TMDb's double spaces are one.
        Assert.StartsWith(
            "Творці серіалу «Блакитна планета» зняли новий документальний серіал, що показує Землю такою",
            work.Overview,
            StringComparison.Ordinal);
        Assert.DoesNotContain("  ", work.Overview, StringComparison.Ordinal);
        Assert.Equal(20, work.Recommendations.Count);
        Assert.Equal(("21145", "Світ природи"), (work.Recommendations[0].Id, work.Recommendations[0].Title));
    }

    [Theory]
    [InlineData("[50, 60]", 53, 50)]
    [InlineData("[0, 47]", 53, 47)]
    [InlineData("[]", 0, null)]
    [InlineData("[]", null, null)]
    [InlineData("\"53\"", null, null)]
    public async Task GetWork_of_a_show_takes_its_first_episode_run_time_else_its_latest_episodes_runtime(
        string episodeRunTime, int? latestEpisodeRuntime, int? expected)
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.TvDetailsPlanetEarth())!.AsObject();
        details["episode_run_time"] = JsonNode.Parse(episodeRunTime);
        if (latestEpisodeRuntime is { } runtime)
            details["last_episode_to_air"]!["runtime"] = runtime;
        else
            details["last_episode_to_air"] = null;
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Series, TmdbSamples.TvDetailsPlanetEarthId, TestContext.Current.CancellationToken);

        //Assert
        // TMDb's 0 is a runtime it doesn't know, never a zero-minute show.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(expected, result.Value!.RuntimeMinutes);
    }

    [Fact]
    public async Task GetWork_of_a_film_tmdb_knows_no_runtime_or_imdb_id_of_has_none()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetailsShort())!.AsObject();
        details["runtime"] = 0;
        details["imdb_id"] = null;
        details.Remove("external_ids");
        details["overview"] = null;
        details.Remove("alternative_titles");
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, TestContext.Current.CancellationToken);

        //Assert
        var work = result.Value!;
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(((int?)null, (string?)null, (string?)null), (work.RuntimeMinutes, work.ImdbId, work.Overview));
        Assert.Empty(work.AlternativeTitles!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nm0001234")]
    [InlineData("tt12")]
    public async Task GetWork_of_a_show_whose_external_ids_name_no_imdb_id_has_none(string? imdbId)
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.TvDetailsPlanetEarth())!.AsObject();
        details["external_ids"]!["imdb_id"] = imdbId;
        // A show's own imdb_id, were TMDb to send one, is not where a show's id is read from.
        details["imdb_id"] = TmdbSamples.TvDetailsPlanetEarthImdbId;
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Series, TmdbSamples.TvDetailsPlanetEarthId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Value!.ImdbId);
    }

    [Fact]
    public async Task GetWork_cleans_the_overview_and_alternative_titles_cuts_them_and_keeps_at_most_fifty_titles()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetailsShort())!.AsObject();
        details["overview"] = $"Том‮\r\n  і Джеррі {Token} " + new string('я', 3000);
        JsonNode?[] titles =
        [
            Title($"Посадка​ {Token}"),
            Title("   "),
            Title("LANDING STRIPLING"),
            Title("Landing Stripling"),
            JsonValue.Create("not a title"),
            new JsonObject { ["iso_3166_1"] = "US" },
            Title(new string('x', 600)),
            .. Enumerable.Range(1, 60).Select(number => Title($"Title {number}")),
        ];
        details["alternative_titles"] = new JsonObject { ["titles"] = new JsonArray(titles) };
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, TestContext.Current.CancellationToken);

        //Assert
        var work = result.Value!;
        // Masked, without control and format characters, one space for each run, cut to the column with "…".
        Assert.StartsWith("Том і Джеррі *** яя", work.Overview, StringComparison.Ordinal);
        Assert.Equal((2000, '…'), (work.Overview!.Length, work.Overview[^1]));
        // A blank title or an entry without one is no title; a title met before in another letter case is not
        // another; the rest are cut to a title's column and kept to 50, in TMDb's order.
        var alternativeTitles = work.AlternativeTitles!;
        Assert.Equal(TmdbAdapter.AlternativeTitleMaximumCount, alternativeTitles.Count);
        Assert.Equal(["Посадка ***", "LANDING STRIPLING"], alternativeTitles.Take(2));
        Assert.Equal((500, '…'), (alternativeTitles[2].Length, alternativeTitles[2][^1]));
        Assert.Equal(["Title 1", "Title 47"], new[] { alternativeTitles[3], alternativeTitles[^1] });
    }

    [Fact]
    public async Task GetWork_of_a_film_reads_no_alternative_titles_under_a_shows_name_for_them()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetailsShort())!.AsObject();
        // The two shapes differ (VendorSamples): a film's titles are under "titles", a show's under "results".
        details["alternative_titles"] = new JsonObject { ["results"] = new JsonArray(Title("Посадка")) };
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(
            Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!.AlternativeTitles!);
    }

    [Fact]
    public async Task GetWork_keeps_recommendations_of_its_own_kind_and_skips_an_entry_it_can_not_read()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetails())!.AsObject();
        var results = details["recommendations"]!["results"]!.AsArray();
        results[0]!["media_type"] = "tv";
        results[1]!.AsObject().Remove("media_type");
        results[2]!.AsObject().Remove("id");
        results[3]!["title"] = "";
        results[3]!["original_title"] = "";
        results.Add("not a work");
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        // A show among a film's recommendations is not one; a film without media_type is; an entry
        // without an id or any title can't be read, and costs the work nothing else.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var ids = result.Value!.Recommendations.Select(recommendation => recommendation.Id).ToList();
        Assert.Equal(17, ids.Count);
        Assert.Equal("609681", ids[0]);
        Assert.DoesNotContain("447365", ids);
        Assert.DoesNotContain("453395", ids);
    }

    [Fact]
    public async Task GetWork_lists_a_director_once_and_reads_no_other_crew_job()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetails())!.AsObject();
        var crew = details["credits"]!["crew"]!.AsArray();
        crew.Add(new JsonObject { ["id"] = 15218, ["name"] = "Джеймс Ґанн", ["department"] = "Writing", ["job"] = "Director" });
        crew.Add(new JsonObject { ["id"] = 7, ["name"] = "Co-director", ["department"] = "Directing", ["job"] = "Director" });
        crew.Add(new JsonObject { ["id"] = 8, ["name"] = "Nameless", ["job"] = "Director" });
        crew.Add(new JsonObject { ["id"] = 8, ["name"] = "", ["job"] = "Director" });
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [new CatalogReference("15218", "Джеймс Ґанн"), new CatalogReference("7", "Co-director"), new CatalogReference("8", "Nameless")],
            result.Value!.Directors);
    }

    [Fact]
    public async Task GetWork_of_a_film_not_released_and_not_rated_has_no_date_and_no_rating()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetails())!.AsObject();
        details["release_date"] = "";
        details["vote_average"] = 0;
        details["vote_count"] = 0;
        details["imdb_id"] = null;
        details["belongs_to_collection"] = null;
        details["poster_path"] = null;
        details["original_language"] = "English";
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        var work = result.Value!;
        Assert.Null(work.Summary.ReleaseDate);
        // Nobody rated it: no rating, rather than a 0 among the worst-rated.
        Assert.Null(work.Summary.Rating);
        Assert.Equal(0, work.Summary.VoteCount);
        Assert.Null(work.ImdbId);
        Assert.Null(work.Collection);
        Assert.Null(work.Summary.PosterPath);
        Assert.Null(work.Summary.OriginalLanguage);
    }

    [Theory]
    [InlineData("/../../etc/passwd.jpg")]
    [InlineData("https://evil.test/poster.jpg")]
    [InlineData("/poster.gif")]
    [InlineData("/poster.jpg?size=original")]
    public async Task GetWork_drops_a_poster_path_the_poster_fetch_would_refuse(string posterPath)
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetails())!.AsObject();
        details["poster_path"] = posterPath;
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Value!.Summary.PosterPath);
    }

    [Fact]
    public async Task GetWork_cleans_a_title_and_masks_a_token_it_echoes()
    {
        //Arrange
        var details = JsonNode.Parse(TmdbSamples.MovieDetails())!.AsObject();
        details["title"] = $"Вартові\u202E\r\n  галактики {Token}";
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(details.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, TmdbSamples.MovieDetailsId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Вартові галактики ***", result.Value!.Summary.Title);
    }

    [Theory]
    [InlineData(MediaWorkKind.Movie, "TMDb has no movie with the id 999999.")]
    [InlineData(MediaWorkKind.Series, "TMDb has no show with the id 999999.")]
    public async Task GetWork_of_a_work_tmdb_does_not_have_is_not_found(MediaWorkKind kind, string expectedError)
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);

        //Act
        var result = await Catalog.GetWorkAsync(settings, kind, "999999", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
        Assert.Single(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task GetWork_answered_404_without_tmdbs_body_is_an_unexpected_response_not_a_missing_work()
    {
        //Arrange
        // Something else at the address: no reason to say the work doesn't exist.
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text("<html>Not Found</html>", HttpStatusCode.NotFound, "text/html");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "118340", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("TMDb's API wasn't found at this address (404 Not Found). Use https://api.themoviedb.org.", TestTexts.En(result.Error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html></html>")]
    [InlineData("""{"id":""")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"id":"118340","title":"Вартові галактики"}""")]
    [InlineData("""{"id":118340}""")]
    [InlineData("""{"id":118340.5,"title":"Вартові галактики"}""")]
    [InlineData("""{"id":-1,"title":"Вартові галактики"}""")]
    public async Task GetWork_of_a_body_that_is_not_tmdbs_details_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "118340", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTmdbError, TestTexts.En(result.Error));
    }

    [Fact]
    public async Task GetWork_of_an_episode_throws_without_sending_anything()
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(
            () => Catalog.GetWorkAsync(Settings, MediaWorkKind.Episode, "118340", TestContext.Current.CancellationToken));

        //Assert
        // TMDb catalogs films and shows; an episode never stands alone in it (ADR 0017).
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tt2015381")]
    [InlineData("../118340")]
    [InlineData("118340?append_to_response=images")]
    [InlineData("118340/credits")]
    [InlineData("１１８")]
    [InlineData("1234567890123")]
    public async Task GetWork_with_an_id_that_is_not_tmdbs_throws_without_sending_anything(string id)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(
            () => Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, id, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task GetCollection_reads_the_collection_sample_and_its_three_parts()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.CollectionFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetCollectionAsync(Settings, TmdbSamples.CollectionId, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/collection/284433?language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var collection = result.Value!;
        Assert.Equal("284433", collection.Id);
        Assert.Equal("Вартові галактики | Колекція", collection.Name);
        Assert.Equal(
            [
                ("118340", "Вартові галактики", new DateOnly(2014, 7, 30)),
                ("283995", "Вартові галактики 2", new DateOnly(2017, 4, 25)),
                ("447365", "Вартові галактики 3", new DateOnly(2023, 5, 3)),
            ],
            collection.Parts.Select(part => (part.Id, part.Title, part.ReleaseDate!.Value)));
        Assert.All(collection.Parts, part => Assert.Equal(MediaWorkKind.Movie, part.Kind));
    }

    [Fact]
    public async Task GetCollection_tmdb_does_not_have_is_not_found()
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);

        //Act
        var result = await Catalog.GetCollectionAsync(settings, "284433", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal("TMDb has no collection with the id 284433.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task GetPerson_reads_the_films_directed_from_the_combined_credits_sample()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.PersonCombinedCreditsFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetPersonAsync(Settings, TmdbSamples.PersonId, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/person/24/combined_credits?language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var person = result.Value!;
        Assert.Equal("24", person.Id);
        // The answer names nobody, and a second request for the name is not made.
        Assert.Equal("", person.Name);
        // 31 films as Director; not the film he is credited as Creator of, nor the four shows he directed
        // episodes of, nor anything he produced, wrote or acted in.
        Assert.Equal(31, person.Works.Count);
        Assert.All(person.Works, work => Assert.Equal(MediaWorkKind.Movie, work.Kind));
        Assert.Equal(("105", "Назад у майбутнє", "Back to the Future"), (person.Works[0].Id, person.Works[0].Title, person.Works[0].OriginalTitle));
        Assert.Contains(person.Works, work => work.Id == "13");
        // Still to come: no date and no rating.
        var unreleased = Assert.Single(person.Works, work => work.Id == "1466158");
        Assert.Null(unreleased.ReleaseDate);
        Assert.Null(unreleased.Rating);
        Assert.Null(unreleased.PosterPath);
    }

    [Fact]
    public async Task GetPerson_adds_the_shows_created_and_lists_each_work_once()
    {
        //Arrange
        var credits = JsonNode.Parse(TmdbSamples.PersonCombinedCredits())!.AsObject();
        var crew = credits["crew"]!.AsArray();
        crew.Add(new JsonObject
        {
            ["id"] = 1026, ["name"] = "Дивовижні історії", ["original_name"] = "Amazing Stories", ["first_air_date"] = "1985-09-29",
            ["vote_average"] = 7.5, ["vote_count"] = 237, ["department"] = "Creator", ["job"] = "Creator", ["media_type"] = "tv",
        });
        // A film credited twice as Director, and the show of the same number as a film: two works, each once.
        crew.Add(new JsonObject { ["id"] = 105, ["title"] = "Назад у майбутнє", ["job"] = "Director", ["media_type"] = "movie" });
        crew.Add(new JsonObject { ["id"] = 105, ["name"] = "A show 105", ["job"] = "Creator", ["media_type"] = "tv" });
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(credits.ToJsonString(), HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.GetPersonAsync(Settings, TmdbSamples.PersonId, TestContext.Current.CancellationToken);

        //Assert
        var works = result.Value!.Works;
        Assert.Equal(33, works.Count);
        Assert.Single(works, work => work is { Kind: MediaWorkKind.Movie, Id: "105" });
        Assert.Single(works, work => work is { Kind: MediaWorkKind.Series, Id: "105" });
        var created = Assert.Single(works, work => work is { Kind: MediaWorkKind.Series, Id: "1026" });
        Assert.Equal(("Дивовижні історії", "Amazing Stories", new DateOnly(1985, 9, 29)), (created.Title, created.OriginalTitle, created.ReleaseDate!.Value));
    }

    [Fact]
    public async Task GetPerson_against_the_fake_lists_the_films_directed_and_the_shows_created()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddPerson(525, "Christopher Nolan");
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 157336, "Інтерстеллар", "Interstellar", "2014-11-05", DirectorIds: [525]));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 27205, "Початок", "Inception", "2010-07-15", DirectorIds: [525, 99]));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 1, "A show", DirectorIds: [525]));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 2, "Someone else's", DirectorIds: [99]));
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);

        //Act
        var result = await Catalog.GetPersonAsync(settings, "525", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [(MediaWorkKind.Movie, "27205"), (MediaWorkKind.Movie, "157336"), (MediaWorkKind.Series, "1")],
            result.Value!.Works.Select(work => (work.Kind, work.Id)));
    }

    [Fact]
    public async Task GetPerson_tmdb_does_not_have_is_not_found()
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);

        //Act
        var result = await Catalog.GetPersonAsync(settings, "24", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal("TMDb has no person with the id 24.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task GetWork_against_the_fake_reads_its_collection_directors_seasons_and_recommendations()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddPerson(15218, "James Gunn");
        tmdb.AddCollection(284433, "Guardians of the Galaxy Collection", 118340);
        tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, 118340, "Вартові галактики", "Guardians of the Galaxy", "2014-07-30", [28, 878], 7.9, 30396,
            "/poster.jpg", "tt2015381", 284433, [15218], RecommendationIds: [283995]));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 283995, "Вартові галактики 2"));
        tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, 118340, "Шоу", DirectorIds: [15218], Seasons: [new FakeTmdbSeason(0, null, 2), new FakeTmdbSeason(1, "2020-01-01", 8)]));
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var movie = await Catalog.GetWorkAsync(settings, MediaWorkKind.Movie, "118340", cancellationToken);
        var show = await Catalog.GetWorkAsync(settings, MediaWorkKind.Series, "118340", cancellationToken);

        //Assert
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, "118340", "Вартові галактики", "Guardians of the Galaxy", "en", new DateOnly(2014, 7, 30),
                movie.Value!.Summary.GenreIds, 7.9, 30396, "/poster.jpg"),
            movie.Value.Summary);
        Assert.Equal([28, 878], movie.Value.Summary.GenreIds);
        Assert.Equal("tt2015381", movie.Value.ImdbId);
        Assert.Equal(new CatalogReference("284433", "Guardians of the Galaxy Collection"), movie.Value.Collection);
        Assert.Equal([new CatalogReference("15218", "James Gunn")], movie.Value.Directors);
        Assert.Equal(["283995"], movie.Value.Recommendations.Select(recommendation => recommendation.Id));
        // The same number is another work among the shows (ADR 0017).
        Assert.Equal("Шоу", show.Value!.Summary.Title);
        Assert.Equal([new CatalogReference("15218", "James Gunn")], show.Value.Directors);
        Assert.Equal([new CatalogSeason(0, null, 2), new CatalogSeason(1, new DateOnly(2020, 1, 1), 8)], show.Value.Seasons);
    }

    [Fact]
    public async Task GetWork_against_the_fake_reads_the_runtime_overview_alternative_titles_and_a_shows_imdb_id()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, 223021, "Landing Stripling", ReleaseDate: "1962-05-18", ImdbId: "tt0056164", RuntimeMinutes: 6,
            Overview: "Том полює на пташеня.", AlternativeTitles: ["Посадка", "Посадка"]));
        tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, 1044, "Планета Земля", "Planet Earth", "2006-03-05", ImdbId: "tt0795176",
            Seasons: [new FakeTmdbSeason(1, "2006-03-05", 11)], RuntimeMinutes: 53, AlternativeTitles: ["Planet Earth", "Planet Erde"]));
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var film = await Catalog.GetWorkAsync(settings, MediaWorkKind.Movie, "223021", cancellationToken);
        var show = await Catalog.GetWorkAsync(settings, MediaWorkKind.Series, "1044", cancellationToken);

        //Assert
        Assert.Equal(
            (6, "tt0056164", "Том полює на пташеня."),
            (film.Value!.RuntimeMinutes!.Value, film.Value.ImdbId, film.Value.Overview));
        Assert.Equal(["Посадка"], film.Value.AlternativeTitles);
        Assert.Equal((53, "tt0795176", (string?)null), (show.Value!.RuntimeMinutes!.Value, show.Value.ImdbId, show.Value.Overview));
        Assert.Equal(["Planet Earth", "Planet Erde"], show.Value.AlternativeTitles);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>An entry of <c>alternative_titles</c> as TMDb lists one (VendorSamples).</summary>
    private static JsonObject Title(string title) => new() { ["iso_3166_1"] = "US", ["title"] = title, ["type"] = "" };
}
