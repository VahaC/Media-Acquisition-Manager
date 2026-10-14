using System.Globalization;
using System.Net;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — which catalog work a release of the feed most likely is, each read the app's own feed reader against
// Toloka's real answers and a fake TMDb: the titles the description lists first — Toloka's "Ukrainian / Original
// (year)" — then the title's, each with the year it names, until one gives exactly one work of that title and year;
// an entry nothing resolves marked resolved all the same; a film read as a film and a show as a show; a run queued only
// once an entry turned out to be a work; at most 100 entries a pass, newest first; and a catalog that fails leaving the
// rest for the next pass.
public sealed class RecommendationFeedResolutionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    /// <summary>The Runner releases of the newest-movies sample: "Перевізник / Runner (2026) …".</summary>
    private static readonly string[] RunnerReleases = ["https://toloka.to/t700686", "https://toloka.to/t700679", "https://toloka.to/t700678"];

    /// <summary>The Lanterns releases of the newest-TV sample: "Ліхтарі (Сезон 1) / Lanterns (Season 1) (2026) …".</summary>
    private static readonly string[] LanternsReleases = ["https://toloka.to/t700692", "https://toloka.to/t699714"];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IntegrationConnectionEntity _tolokaSource = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationFeedResolutionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        await new UserSeeder(_factory.Scope).Create("anna");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tolokaSource = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey) { SearchXml = TorznabSamples.Feed() };
        _toloka.Arm(_factory.TorznabHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Read_resolves_an_entry_by_the_original_title_its_description_lists_with_its_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.LatestMovies(TolokaAddress, ApiKey);
        // TMDb knows it by its Ukrainian title too, but its search for that title finds nothing here.
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1290001, "Перевізник", "Runner", "2026-08-01", [28]));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Runner", 2026, 1290001);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var runner = await CatalogWorkAsync(MediaWorkKind.Movie, "1290001", cancellationToken);
        var entries = await EntriesAsync(cancellationToken);
        Assert.Equal(TorznabSamples.LatestMoviesItemCount, entries.Count);
        Assert.Equal(
            RunnerReleases.Order(StringComparer.Ordinal),
            entries.Where(entry => entry.CatalogWorkId == runner.Id).Select(entry => entry.SourceItemId!).Order(StringComparer.Ordinal));
        // Every other entry is resolved too — to nothing: none is looked up again.
        Assert.All(entries, entry => Assert.Equal(_factory.Time.GetUtcNow().UtcDateTime, entry.ResolvedUtc));
        // The description's Ukrainian title first, then its original one, each with the year; the title's "Runner"
        // was the description's already. Each asked once for the three releases, and the work's details once.
        Assert.Equal(1, Searches("movie", "Перевізник", "2026"));
        Assert.Equal(1, Searches("movie", "Runner", "2026"));
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/movie/1290001");
        Assert.Equal((MediaWorkKind.Movie, "Перевізник", "Runner"), (runner.Kind, runner.Title, runner.OriginalTitle));
    }

    [Fact]
    public async Task Read_resolves_a_title_only_its_description_names_by_the_year_the_description_gives()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Prowlarr's title keeps nothing of a Ukrainian-only title but its tail; Toloka's own is in the description.
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "(2026) WEB-DL 1080p Ukr",
                $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1",
                "toloka-1",
                extra: "<category>2040</category><description>Самотня злодійка (2026) WEB-DL 1080p Ukr</description>"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1300001, "Самотня злодійка", "The Isolate Thief", "2026-09-20"));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Самотня злодійка", 2026, 1300001);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var work = await CatalogWorkAsync(MediaWorkKind.Movie, "1300001", cancellationToken);
        var entry = Assert.Single(await EntriesAsync(cancellationToken));
        Assert.Equal(work.Id, entry.CatalogWorkId);
        // The title alone names no title to look up.
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/search/movie");
    }

    [Fact]
    public async Task Read_resolves_a_show_by_its_seasons_listing_as_a_show()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.LatestTv(TolokaAddress, ApiKey);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 250001, "Ліхтарі", "Lanterns", "2026-08-17"));
        _tmdb.AddSearch(MediaWorkKind.Series, "Ліхтарі", 2026, 250001);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        // "Ліхтарі (Сезон 1) / Lanterns (Season 1) (2026)": its titles without the seasons, asked of TMDb's TV search.
        var lanterns = await CatalogWorkAsync(MediaWorkKind.Series, "250001", cancellationToken);
        var entries = await EntriesAsync(cancellationToken);
        Assert.Equal(
            LanternsReleases.Order(StringComparer.Ordinal),
            entries.Where(entry => entry.CatalogWorkId == lanterns.Id).Select(entry => entry.SourceItemId!).Order(StringComparer.Ordinal));
        Assert.All(entries, entry => Assert.Equal(ReleaseKind.Tv, entry.Kind));
        Assert.Equal(0, Searches("movie", "Ліхтарі", "2026"));
    }

    [Fact]
    public async Task Read_resolves_a_series_filed_with_episodes_alone_by_its_titles_without_the_episodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-33 — Toloka's TV/Anime way (VendorSamples README): episodes without a season, in a category of films too.
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "Death Note (E1-31 of 37) (2006) BDRip 1080p",
                $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1",
                "toloka-1",
                extra: "<category>5070</category><description>Зошит смерті (серії 1-31 з 37) / Death Note (E1-31 of 37) (2006) BDRip 1080p</description>"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 13916, "Зошит смерті", "デスノート", "2006-10-04", [16]));
        _tmdb.AddSearch(MediaWorkKind.Series, "Зошит смерті", 2006, 13916);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        // A show by its episodes, looked up by its Ukrainian title without them — never "Зошит смерті (серії 1-31 з 37)".
        var deathNote = await CatalogWorkAsync(MediaWorkKind.Series, "13916", cancellationToken);
        var entry = Assert.Single(await EntriesAsync(cancellationToken));
        Assert.Equal((ReleaseKind.Tv, (Guid?)deathNote.Id), (entry.Kind, entry.CatalogWorkId));
        Assert.Equal(1, Searches("tv", "Зошит смерті", "2006"));
    }

    [Fact]
    public async Task Read_resolves_a_later_season_pack_to_the_one_show_of_its_title_begun_by_the_packs_first_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=";
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "Yellowstone (S5) (2022-2024) WEB-DL 1080p",
                link + "1",
                "toloka-5",
                extra: "<category>5040</category><description>Єллоустоун (Сезон 5) / Yellowstone (Season 5) (2022-2024) WEB-DL 1080p</description>"),
            // Its second season aired in 2016: a show begun by then, which neither of TMDb's is.
            TorznabSamples.Item(
                "Yellowstone (S2) (2016) WEB-DL 1080p",
                link + "2",
                "toloka-2",
                extra: "<category>5040</category><description>Єллоустоун (Сезон 2) / Yellowstone (Season 2) (2016) WEB-DL 1080p</description>"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 73586, "Єллоустоун", "Yellowstone", "2018-06-20"));
        // A show of the same title begun after the pack's first year: no candidate for it.
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 999001, "Єллоустоун", "Yellowstone", "2025-03-01"));
        _tmdb.AddSearch(MediaWorkKind.Series, "Єллоустоун", null, 73586, 999001);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var show = await CatalogWorkAsync(MediaWorkKind.Series, "73586", cancellationToken);
        var entries = (await EntriesAsync(cancellationToken)).ToDictionary(entry => entry.SourceItemId!);
        Assert.Equal(show.Id, entries["toloka-5"].CatalogWorkId);
        Assert.Equal((true, (Guid?)null), (entries["toloka-2"].ResolvedUtc is not null, entries["toloka-2"].CatalogWorkId));
        // Searched without a year — the pack's is its seasons' — once per pack: each is cached by the year it allows.
        var searches = _factory.TmdbHttp.Requests.Where(request => request.Uri.AbsolutePath == "/3/search/tv").ToList();
        Assert.All(searches, request => Assert.Null(Parameter(request.Uri, "first_air_date_year")));
        Assert.Equal(2, searches.Count(request => Parameter(request.Uri, "query") == "Єллоустоун"));
    }

    [Fact]
    public async Task Read_looks_a_pack_with_season_1_up_by_the_year_it_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng",
                $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1",
                "toloka-1",
                extra: "<category>5000</category><description>Єллоустоун (Сезони 1-5) / Yellowstone (Seasons 1-5) (2018-2024) WEB-DLRip-AVC Ukr/Eng</description>"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 73586, "Єллоустоун", "Yellowstone", "2018-06-20"));
        _tmdb.AddSearch(MediaWorkKind.Series, "Єллоустоун", 2018, 73586);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        // With season 1, its first year is the year the show began: the exact-year lookup, as for a film.
        var show = await CatalogWorkAsync(MediaWorkKind.Series, "73586", cancellationToken);
        Assert.Equal(show.Id, Assert.Single(await EntriesAsync(cancellationToken)).CatalogWorkId);
        Assert.Equal(1, Searches("tv", "Єллоустоун", "2018"));
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/search/tv");
    }

    [Fact]
    public async Task Read_looks_a_title_up_without_the_bracket_its_reading_left_open()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // "Розслідування з Андрієм Данілевичем (Сезон 1, серії 1-25 з ?) (2026)": looked up by its title alone (MAM-30:
        // its marker takes the bracket, the total of episodes unknown). "Ранма 1/2 (Сезон 1,2, Сезон 3: серії 01 з 12)"
        // is still cut at the season marker inside its bracket, which keeps the bracket.
        _toloka.SearchXml = TorznabSamples.LatestTv(TolokaAddress, ApiKey);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 300001, "Розслідування з Андрієм Данілевичем", ReleaseDate: "2026-01-15"));
        _tmdb.AddSearch(MediaWorkKind.Series, "Розслідування з Андрієм Данілевичем", 2026, 300001);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var show = await CatalogWorkAsync(MediaWorkKind.Series, "300001", cancellationToken);
        var entry = Assert.Single(await EntriesAsync(cancellationToken), entry => entry.SourceItemId == "https://toloka.to/t696309");
        Assert.Equal(show.Id, entry.CatalogWorkId);
        Assert.Equal(1, Searches("tv", "Розслідування з Андрієм Данілевичем", "2026"));
        // Nor any other title of the sample the reading cut inside a bracket ("Ранма 1/2 (Сезон 1,2, …").
        Assert.DoesNotContain(_factory.TmdbHttp.Requests, request => Parameter(request.Uri, "query")?.TrimEnd().EndsWith('(') == true);
    }

    [Fact]
    public async Task Read_queues_a_run_only_once_an_entry_turned_out_to_be_a_catalog_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=";
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Unknown Film (2026) WEB-DL 1080p", link + "1", "toloka-1", extra: "<category>2040</category>"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1290001, "Перевізник", "Runner", "2026-08-01"));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Runner", 2026, 1290001);
        await _factory.ReadRecommendationFeedAsync(cancellationToken);
        var runsAfterNothingResolved = await RunsAsync(cancellationToken);

        //Act
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Runner (2026) WEB-DL 1080p", link + "2", "toloka-2", extra: "<category>2040</category>"));
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        Assert.Empty(runsAfterNothingResolved);
        var run = Assert.Single(await RunsAsync(cancellationToken));
        Assert.Equal((RecommendationRunTrigger.FeedRead, RecommendationRunStatus.Queued, (Guid?)null), (run.Trigger, run.Status, run.RequestedByUserId));
    }

    [Fact]
    public async Task Read_resolves_at_most_100_entries_a_pass_the_newest_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var feed = new SourceFeedEntrySeeder(_factory.Scope);
        var publishedFrom = _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1);
        var planted = new List<SourceFeedEntryEntity>();
        for (var minute = 0; minute <= RecommendationFeedPass.ResolvePerPass; minute++)
            planted.Add(await feed.Create(_tolokaSource, "Unknown Film (2020) DVDRip", resolvedTo: null, publishedUtc: publishedFrom.AddMinutes(minute)));

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);
        var unresolvedAfterOnePass = (await EntriesAsync(cancellationToken)).Where(entry => entry.ResolvedUtc is null).Select(entry => entry.Id).ToList();
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        // The oldest waits for the next pass; one lookup of the shared title served them all (cached).
        Assert.Equal([planted[0].Id], unresolvedAfterOnePass);
        Assert.All(await EntriesAsync(cancellationToken), entry => Assert.NotNull(entry.ResolvedUtc));
        Assert.Equal(1, Searches("movie", "Unknown Film", "2020"));
    }

    [Fact]
    public async Task Read_leaves_the_entries_for_the_next_pass_when_the_catalog_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "Runner (2026) WEB-DL 1080p", $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1", "toloka-1", extra: "<category>2040</category>"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1290001, "Перевізник", "Runner", "2026-08-01"));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Runner", 2026, 1290001);
        _tmdb.Intercept = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);
        var afterTheFailure = Assert.Single(await EntriesAsync(cancellationToken));
        _tmdb.Intercept = null;
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        Assert.Null(afterTheFailure.ResolvedUtc);
        Assert.Contains(_factory.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Resolving the newest releases stopped", StringComparison.Ordinal));
        var work = await CatalogWorkAsync(MediaWorkKind.Movie, "1290001", cancellationToken);
        Assert.Equal(work.Id, Assert.Single(await EntriesAsync(cancellationToken)).CatalogWorkId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>How many times TMDb's search of <paramref name="type"/> was asked for <paramref name="query"/> in
    /// <paramref name="year"/> (a film's <c>year</c>, a show's <c>first_air_date_year</c>).</summary>
    private int Searches(string type, string query, string year) =>
        _factory.TmdbHttp.Requests.Count(request =>
            request.Uri.AbsolutePath == $"/3/search/{type}"
            && Parameter(request.Uri, "query") == query
            && (Parameter(request.Uri, "year") ?? Parameter(request.Uri, "first_air_date_year")) == year);

    /// <summary>The value of the query parameter <paramref name="name"/>, unescaped; <c>null</c> without one.</summary>
    private static string? Parameter(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(pair[0], name, StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "")
            .FirstOrDefault();

    private async Task<CatalogWorkEntity> CatalogWorkAsync(MediaWorkKind kind, string catalogId, CancellationToken cancellationToken)
    {
        CatalogWorkEntity? work = null;
        await _factory.WithDbAsync(async db => work = await db.CatalogWorks.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Kind == kind && row.CatalogId == catalogId, cancellationToken));
        return work ?? throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"No catalog work {kind} {catalogId} was stored."));
    }

    private async Task<List<SourceFeedEntryEntity>> EntriesAsync(CancellationToken cancellationToken)
    {
        List<SourceFeedEntryEntity> entries = [];
        await _factory.WithDbAsync(async db => entries = await db.SourceFeedEntries.AsNoTracking().ToListAsync(cancellationToken));
        return entries;
    }

    private async Task<List<RecommendationRunEntity>> RunsAsync(CancellationToken cancellationToken)
    {
        List<RecommendationRunEntity> runs = [];
        await _factory.WithDbAsync(async db => runs = await db.RecommendationRuns.AsNoTracking().ToListAsync(cancellationToken));
        return runs;
    }
}
