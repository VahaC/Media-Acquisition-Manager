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

// MAM-33 — reading the release sources' newest releases ("New on Toloka"), each pass the app's own feed reader against
// Toloka's real answers faked at the network: one general search with no text per source, in the categories the users'
// tabs want; nothing at all with every tab off or no metadata catalog; each release kept once by its source's id, a
// second read adding none and keeping when it was first seen and resolved; a source that fails leaving the others'
// entries; the entries no source listed for 30 days forgotten; and the reader's own loop. Resolving the entries to
// catalog works is RecommendationFeedResolutionHttpTests'.
public sealed class RecommendationFeedHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string MazepaAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";

    // The categories of Toloka's caps sample ReleaseSearchPlanning.ForLatest asks (ADR 0020: TV/Anime and
    // TV/Documentary hold films too).
    private const string FilmCategories = "2000,2020,2040,2070,5070,5080";
    private const string ShowCategories = "5000,5040,5050,5060,5070,5080";
    private const string AllCategories = "2000,2020,2040,2070,5000,5040,5050,5060,5070,5080";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private UserEntity _anna = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _mazepa = null!;

    public RecommendationFeedHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _anna = await new UserSeeder(_factory.Scope).Create("anna");
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey) { SearchXml = TorznabSamples.Feed() };
        _mazepa = new FakeTorznabServer(MazepaAddress, ApiKey) { SearchXml = TorznabSamples.Feed() };
        FakeTorznabServer.Arm(_factory.TorznabHttp, _toloka, _mazepa);
        // A catalog that finds nothing: what it is asked is RecommendationFeedResolutionHttpTests'.
        FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Read_asks_each_source_once_for_its_newest_releases_without_text_in_the_categories_the_tabs_want()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Mazepa", MazepaAddress, apiKey: ApiKey);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        // Every tab is on by default: films and shows both. One query each, no q and no offset — Toloka answers its
        // latest 90 and pages nothing further (VendorSamples).
        Assert.Equal(
            new[]
            {
                $"{MazepaAddress}?t=caps&apikey={ApiKey}",
                $"{MazepaAddress}?t=search&apikey={ApiKey}&cat={AllCategories}&limit=100",
                $"{TolokaAddress}?t=caps&apikey={ApiKey}",
                $"{TolokaAddress}?t=search&apikey={ApiKey}&cat={AllCategories}&limit=100",
            }.Order(StringComparer.Ordinal),
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(RecommendationTab.Movies, FilmCategories)]
    [InlineData(RecommendationTab.Series, ShowCategories)]
    [InlineData(RecommendationTab.Cartoons, AllCategories)]
    [InlineData(RecommendationTab.Documentaries, AllCategories)]
    public async Task Read_asks_only_the_categories_of_the_kinds_the_tabs_on_hold(RecommendationTab tab, string expectedCategories)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_anna, [tab]);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var latest = Assert.Single(_factory.TorznabHttp.Requests, request => request.Uri.Query.StartsWith("?t=search", StringComparison.Ordinal));
        Assert.Equal($"{TolokaAddress}?t=search&apikey={ApiKey}&cat={expectedCategories}&limit=100", latest.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Read_does_nothing_at_all_when_every_tab_is_off_for_everyone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_anna, []);
        // A user who can't sign in sees no list: their tabs, on by default, don't count.
        await new UserSeeder(_factory.Scope).Create("borys", isDisabled: true);
        _toloka.SearchXml = TorznabSamples.LatestMovies(TolokaAddress, ApiKey);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.TmdbHttp.Requests);
        Assert.Empty(await EntriesAsync(cancellationToken));
    }

    [Fact]
    public async Task Read_does_nothing_at_all_without_a_metadata_catalog()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token, isEnabled: false);
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.LatestMovies(TolokaAddress, ApiKey);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.TmdbHttp.Requests);
        Assert.Empty(await EntriesAsync(cancellationToken));
    }

    [Fact]
    public async Task Read_keeps_each_release_once_and_a_second_read_adds_none_and_keeps_when_it_was_first_seen()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.LatestMovies(TolokaAddress, ApiKey);
        var firstReadUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.ReadRecommendationFeedAsync(cancellationToken);
        var catalogCallsOfTheFirstRead = _factory.TmdbHttp.Requests.Count;

        //Act
        _factory.Time.Advance(TimeSpan.FromHours(3));
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var entries = await EntriesAsync(cancellationToken);
        Assert.Equal(TorznabSamples.LatestMoviesItemCount, entries.Count);
        var first = Assert.Single(entries, entry => entry.SourceItemId == "https://toloka.to/t700691");
        Assert.Equal(
            (ProvenanceSourceKind.ReleaseSearch, (Guid?)toloka.Id, "Toloka", ReleaseKind.Movie, "2040,100096"),
            (first.SourceKind, first.SourceConnectionId, first.SourceName, first.Kind, first.CategoryIds));
        Assert.Equal("Those Who Wish Me Dead (2021) BDRip 1080p 2xUkr/Eng | Sub Ukr/Eng", first.Title);
        Assert.Equal("Ті, хто бажають моєї смерті / Those Who Wish Me Dead (2021) BDRip 1080p 2xUkr/Eng | Sub Ukr/Eng", first.Description);
        // "Mon, 05 Oct 2026 00:00:00 +0300".
        Assert.Equal(new DateTime(2026, 10, 4, 21, 0, 0), first.PublishedUtc);
        // First seen and resolved — to nothing, the catalog knows none of them — at the first read; last seen at the
        // second, which looked nothing up again.
        Assert.All(entries, entry => Assert.Equal(
            (firstReadUtc, (DateTime?)firstReadUtc, (Guid?)null, _factory.Time.GetUtcNow().UtcDateTime),
            (entry.CreatedUtc, entry.ResolvedUtc, entry.CatalogWorkId, entry.ObservedUtc)));
        Assert.True(catalogCallsOfTheFirstRead > 0);
        Assert.Equal(catalogCallsOfTheFirstRead, _factory.TmdbHttp.Requests.Count);
    }

    [Fact]
    public async Task Read_keeps_what_the_other_sources_listed_when_one_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var mazepa = await _integrations.Create("torznab", "Mazepa", MazepaAddress, apiKey: ApiKey);
        _toloka.Intercept = request => request.RequestUri!.Query.StartsWith("?t=search", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;
        _mazepa.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Runner (2026) WEB-DL 1080p", $"{TorznabSamples.DownloadBaseFor(MazepaAddress)}download?file=1", "mazepa-1", extra: "<category>2040</category>"));

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var entry = Assert.Single(await EntriesAsync(cancellationToken));
        Assert.Equal((mazepa.Id, "mazepa-1"), (entry.SourceConnectionId!.Value, entry.SourceItemId));
        // The status only, never the sentence, which can carry the address.
        Assert.Contains(_factory.Logs.Entries, log => log.Level == LogLevel.Information && log.Message.Contains($"integration {toloka.Id} were not read: ServiceError.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_skips_a_source_removed_during_the_read_and_keeps_what_the_others_listed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        // Mazepa first by name, so its entries are saved before Toloka's.
        var mazepa = await _integrations.Create("torznab", "Mazepa", MazepaAddress, apiKey: ApiKey);
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        // MAM-33 — the owner removes Mazepa while it is answering: the pass read it as a source, and it is gone by the
        // time its entries are saved.
        _mazepa.Intercept = request =>
        {
            if (request.RequestUri!.Query.StartsWith("?t=search", StringComparison.Ordinal))
            {
                Task.Run(
                        () => _factory.WithDbAsync(db => db.IntegrationConnections.Where(integration => integration.Id == mazepa.Id).ExecuteDeleteAsync(cancellationToken)),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }

            return null;
        };
        _mazepa.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Runner (2026) WEB-DL 1080p", $"{TorznabSamples.DownloadBaseFor(MazepaAddress)}download?file=1", "mazepa-1", extra: "<category>2040</category>"));
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Runner (2026) WEB-DL 2160p", $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1", "toloka-1", extra: "<category>2040</category>"));
        _factory.Logs.Clear();

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        // Toloka's entry is kept and the pass went on to resolve it; Mazepa's is not, and that is no error of the app's
        // own (EF Core logs the failed save itself).
        var entry = Assert.Single(await EntriesAsync(cancellationToken));
        Assert.Equal((toloka.Id, "toloka-1"), (entry.SourceConnectionId!.Value, entry.SourceItemId));
        Assert.NotNull(entry.ResolvedUtc);
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            log => log.Level >= LogLevel.Warning && log.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal));
        // The id only, never a sentence, which can carry the address.
        Assert.Contains(
            _factory.Logs.Entries,
            log => log.Level == LogLevel.Information && log.Message.Contains($"integration {mazepa.Id} were not stored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_forgets_the_entries_no_source_listed_for_30_days()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var work = await new CatalogWorkSeeder(_factory.Scope).Create(MediaWorkKind.Movie, "603", "Матриця", new DateOnly(1999, 3, 30));
        var feed = new SourceFeedEntrySeeder(_factory.Scope);
        var old = await feed.Create(toloka, "Old Film (2000) DVDRip", work, seenUtc: now - RecommendationFeedPass.FeedEntryLifetime - TimeSpan.FromMinutes(1));
        var recent = await feed.Create(toloka, "Recent Film (2000) DVDRip", work, seenUtc: now - RecommendationFeedPass.FeedEntryLifetime + TimeSpan.FromMinutes(1));
        // First seen long ago, and listed still: last seen now, so never forgotten while a source lists it.
        var listed = await feed.Create(
            toloka, "Listed Film (2000) DVDRip", work, seenUtc: now - TimeSpan.FromDays(40), sourceItemId: "toloka-1");
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Listed Film (2000) DVDRip", $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1", "toloka-1", extra: "<category>2000</category>"));

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var entries = (await EntriesAsync(cancellationToken)).ToDictionary(entry => entry.Id);
        Assert.False(entries.ContainsKey(old.Id));
        Assert.True(entries.ContainsKey(recent.Id));
        Assert.Equal((listed.CreatedUtc, now), (entries[listed.Id].CreatedUtc, entries[listed.Id].ObservedUtc));
    }

    [Fact]
    public async Task Reader_runs_a_pass_as_soon_as_it_starts_and_stops_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Runner (2026) WEB-DL 1080p", $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?file=1", "toloka-1", extra: "<category>2040</category>"));
        var reader = _factory.Resolve<RecommendationFeedReader>();
        _factory.Logs.Clear();

        //Act
        // The host's own start and stop, which the test host leaves to the test.
        await reader.StartAsync(cancellationToken);
        var read = await WaitForResolvedEntryAsync(cancellationToken);
        await reader.StopAsync(cancellationToken);

        //Assert
        // The first read right after the start, without waiting PassInterval; the stop ends the wait for the next one,
        // which is no error. On .NET 10 a stop can also win the race before the loop runs at all.
        Assert.True(read);
        Assert.True(reader.ExecuteTask is { Status: TaskStatus.RanToCompletion or TaskStatus.Canceled }, $"{reader.ExecuteTask?.Status}");
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    private async Task<List<SourceFeedEntryEntity>> EntriesAsync(CancellationToken cancellationToken)
    {
        List<SourceFeedEntryEntity> entries = [];
        await _factory.WithDbAsync(async db => entries = await db.SourceFeedEntries.AsNoTracking().ToListAsync(cancellationToken));
        return entries;
    }

    /// <summary>Whether an entry is stored and resolved within five seconds, read every 50 ms: for the one test that
    /// lets the reader's own loop run.</summary>
    private async Task<bool> WaitForResolvedEntryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await EntriesAsync(cancellationToken)).Exists(entry => entry.ResolvedUtc is not null))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        return false;
    }
}
