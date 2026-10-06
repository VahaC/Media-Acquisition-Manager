using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 — a preparation of the TMDb suggestions, run by its worker: every library work without a TMDb id is searched by
// its cleaned titles and its folder's name, with its year and without (R21), for the kinds of catalog work it could be;
// up to ten of the films and shows found are read through the catalog cache and stored as its candidates (their posters
// then serve). A work searched within a month by the same queries is kept — unless the owner asked, which searches
// again —, each work is saved as it is done, a work a sync deleted meanwhile is skipped, and a catalog failure ends the
// run with its sentence while the works done stay. The worker and the reconciler settle runs the RecommendationRun way.
public sealed class LibraryLinkSuggestionRunnerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string LandingId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string CalypsoId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string IdentifiedId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string TomAndJerryFolder = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)";
    private const string AppErrorMessage = "The TMDb suggestions stopped being prepared because of an error in the app. Its log says more.";
    private const string RestartedError = "The app restarted before these TMDb suggestions were prepared. The works searched so far are kept.";
    private const int LandingFilm = 223021;
    private const int LandingRemake = 5000;
    private const int TomAndJerryShow = 300;
    private const int IdentifiedFilm = 11134;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryLinkSuggestionRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;

    public LibraryLinkSuggestionRunnerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _runs = new LibraryLinkSuggestionRunSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, LandingFilm, "Посадка салаги", "Landing Stripling", "1962-04-20", GenreIds: [16, 35], PosterPath: "/landing.jpg", RuntimeMinutes: 6));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, LandingRemake, "Landing Stripling", ReleaseDate: "1999-01-01", RuntimeMinutes: 95));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, TomAndJerryShow, "Том і Джеррі", "Tom & Jerry", "1975-09-06", GenreIds: [16]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, IdentifiedFilm, "Том і Джеррі: Фільм", "Tom and Jerry: The Movie", "1992-07-30", GenreIds: [16]));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, LandingFilm);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", null, LandingFilm, LandingRemake);
        _tmdb.AddSearch(MediaWorkKind.Series, "Tom & Jerry", null, TomAndJerryShow);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Run_searches_each_work_without_an_id_by_its_titles_and_folder_and_stores_its_candidates()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing(), Identified());
        await ConnectCatalogAsync();
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var run = await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(
            (LibraryLinkSuggestionRunStatus.Succeeded, (int?)1, 1, (string?)null),
            (run.Status, run.WorksTotal, run.WorksDone, run.Error));
        // Its cleaned title and its folder's name (the "e001-e161" range and the years cut), films and shows (a film may be
        // an episode of a show), each with its year and without (R21). The work with a TMDb id is not searched.
        Assert.Equal(
            [
                ("movie", "Landing Stripling", (string?)"1962"), ("movie", "Landing Stripling", null),
                ("tv", "Landing Stripling", "1962"), ("tv", "Landing Stripling", null),
                ("movie", "Tom & Jerry", "1962"), ("movie", "Tom & Jerry", null),
                ("tv", "Tom & Jerry", "1962"), ("tv", "Tom & Jerry", null),
            ],
            Searches());
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (landingWorkId, "Movie,Series|1962|Title:Landing Stripling|FolderName:Tom & Jerry", now, (DateTime?)null),
                (search.MediaWorkId, search.QueryKey, search.SearchedUtc, search.NoneFitsUtc));
            Assert.Equal(
                [
                    (0, MediaWorkKind.Movie, "223021", LibraryLinkCandidateSource.Title),
                    (1, MediaWorkKind.Movie, "5000", LibraryLinkCandidateSource.Title),
                    (2, MediaWorkKind.Series, "300", LibraryLinkCandidateSource.FolderName),
                ],
                (await db.LibraryLinkCandidates.AsNoTracking().OrderBy(candidate => candidate.Position).ToListAsync(cancellationToken))
                    .Select(candidate => (candidate.Position, candidate.CatalogKind, candidate.CatalogId, candidate.Source)));
            // Each candidate's details read through the cache — its runtime, genres, directors are rows the evidence reads
            // —, and the library's identified work's too, for what the cartoons library mostly holds (decision 3).
            var details = await db.CatalogWorks.AsNoTracking().Where(work => work.DetailsUtc != null).ToListAsync(cancellationToken);
            Assert.Equal(["11134", "223021", "300", "5000"], details.Select(work => work.CatalogId).Order(StringComparer.Ordinal));
            Assert.Equal((int?)6, details.Single(work => work.CatalogId == "223021").RuntimeMinutes);
            // Nothing is written to the library's tables (ADR 0025).
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
        });
        var finished = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkSuggestionRunner).FullName);
        Assert.Equal(LogLevel.Information, finished.Level);
        Assert.DoesNotContain("Landing", finished.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_asks_the_catalog_a_folders_name_once_for_all_the_works_in_that_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two shorts of one folder: the folder's name is a query of each.
        await SyncAsync(cancellationToken, Landing(), Calypso());
        await ConnectCatalogAsync();

        //Act
        var run = await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibraryLinkSuggestionRunStatus.Succeeded, 2), (run.Status, run.WorksDone));
        Assert.Equal(
            [("movie", "1962"), ("movie", null), ("tv", "1962"), ("tv", null)],
            Searches().Where(search => search.Query == "Tom & Jerry").Select(search => (search.Kind, search.Year)));
        // Both works have the folder's candidate all the same.
        await _factory.WithDbAsync(async db => Assert.Equal(
            2,
            await db.LibraryLinkCandidates.CountAsync(
                candidate => candidate.CatalogId == "300" && candidate.Source == LibraryLinkCandidateSource.FolderName, cancellationToken)));
    }

    [Fact]
    public async Task Run_keeps_a_work_searched_lately_by_the_same_queries_and_asks_the_catalog_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var searched = Searches().Count;
        await QueueAsync(LibraryLinkSuggestionRunTrigger.LibrarySynced);

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        Assert.Equal(searched, Searches().Count);
        await _factory.WithDbAsync(async db =>
        {
            var latest = await db.LibraryLinkSuggestionRuns.AsNoTracking().OrderByDescending(run => run.CreatedUtc).ThenByDescending(run => run.Trigger).FirstAsync(cancellationToken);
            Assert.Equal((LibraryLinkSuggestionRunStatus.Succeeded, 1), (latest.Status, latest.WorksDone));
            Assert.Equal(3, await db.LibraryLinkCandidates.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Run_the_owner_asked_for_searches_every_work_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var searched = Searches().Count;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // R21 — the cache bypassed: TMDb is asked again, and the search stamped anew.
        Assert.Equal(2 * searched, Searches().Count);
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(_factory.Time.GetUtcNow().UtcDateTime, search.SearchedUtc);
            Assert.Equal(3, await db.LibraryLinkCandidates.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Run_searches_again_a_work_whose_search_is_older_than_a_month()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var searched = Searches().Count;
        _factory.Time.Advance(TimeSpan.FromDays(31));
        await QueueAsync(LibraryLinkSuggestionRunTrigger.LibrarySynced);

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        Assert.Equal(2 * searched, Searches().Count);
    }

    [Fact]
    public async Task Run_searches_again_a_work_whose_year_changed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        _jellyfin.Movies[0]["ProductionYear"] = 1963;
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var searched = Searches().Count;

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        // The sync queued a run (a catalog is connected now); the work's year is another key, so it is asked again.
        Assert.Equal(searched + 8, Searches().Count);
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal("Movie,Series|1963|Title:Landing Stripling|FolderName:Tom & Jerry", search.QueryKey);
        });
    }

    [Fact]
    public async Task Run_keeps_the_owners_none_fits_when_it_searches_a_work_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        var markedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.WithDbAsync(db => db.LibraryLinkSearches.ExecuteUpdateAsync(
            setters => setters.SetProperty(search => search.NoneFitsUtc, markedUtc).SetProperty(search => search.NoneFitsByUserId, _owner.User.Id),
            cancellationToken));

        //Act
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(((DateTime?)markedUtc, (Guid?)_owner.User.Id), (search.NoneFitsUtc, search.NoneFitsByUserId));
        });
    }

    [Fact]
    public async Task Run_a_catalog_failure_ends_with_its_sentence_and_keeps_the_works_searched_before_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Calypso(), Landing());
        await ConnectCatalogAsync();
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        // "120. Landing Stripling" comes first in title order; TMDb fails on "Calypso Cat".
        _tmdb.Intercept = request => request.RequestUri!.Query.Contains("Calypso", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;

        //Act
        var run = await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibraryLinkSuggestionRunStatus.Failed, (int?)2, 1), (run.Status, run.WorksTotal, run.WorksDone));
        Assert.False(string.IsNullOrWhiteSpace(run.Error));
        Assert.DoesNotContain(FakeTmdbServer.Token, run.Error, StringComparison.Ordinal);
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(landingWorkId, search.MediaWorkId);
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkSuggestionRunner).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task Run_skips_a_work_a_sync_deleted_while_it_was_searched()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var deleted = false;
        _tmdb.Intercept = request =>
        {
            if (!deleted && request.RequestUri!.AbsolutePath.StartsWith("/3/search/", StringComparison.Ordinal))
            {
                deleted = true;
                using var scope = _factory.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().MediaWorks
                    .Where(work => work.Id == landingWorkId)
                    .ExecuteDelete();
            }

            return null;
        };

        //Act
        var run = await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibraryLinkSuggestionRunStatus.Succeeded, 1), (run.Status, run.WorksDone));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.LibraryLinkSearches.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Run_without_a_catalog_any_more_fails_with_its_sentence_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        var catalog = await ConnectCatalogAsync();
        var runId = await QueueAsync(LibraryLinkSuggestionRunTrigger.LibrarySynced);
        await _factory.WithDbAsync(db => db.IntegrationConnections.Where(integration => integration.Id == catalog.Id).ExecuteDeleteAsync(cancellationToken));
        var sent = _factory.TmdbHttp.Requests.Count;

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal(
                (LibraryLinkSuggestionRunStatus.Failed, (string?)"Connect a metadata catalog under Integrations first."),
                (run.Status, run.Error));
        });
        Assert.Equal(sent, _factory.TmdbHttp.Requests.Count);
    }

    [Fact]
    public async Task Posters_of_the_candidates_are_served()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest("/api/recommendations/posters/landing.jpg", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RunQueued_fails_a_run_whose_runner_throws_and_logs_the_bug()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync(cancellationToken, Landing());
        await ConnectCatalogAsync();
        var runId = await QueueAsync(LibraryLinkSuggestionRunTrigger.LibrarySynced);
        _tmdb.Intercept = request => request.RequestUri!.AbsolutePath.StartsWith("/3/search/", StringComparison.Ordinal)
            ? throw new InvalidOperationException("A bug in the preparation.")
            : null;

        //Act
        await _factory.RunQueuedLinkSuggestionsAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal((LibraryLinkSuggestionRunStatus.Failed, (string?)AppErrorMessage), (run.Status, run.Error));
        });
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkSuggestionRunWorker).FullName);
        Assert.Equal(LogLevel.Error, error.Level);
    }

    [Fact]
    public async Task Reconcile_fails_the_running_run_and_queues_the_queued_one_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var running = await _runs.Create(LibraryLinkSuggestionRunStatus.Running, LibraryLinkSuggestionRunTrigger.LibrarySynced);
        var queued = await _runs.Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.CatalogConnected);

        //Act
        await _factory.Resolve<LibraryLinkSuggestionRunReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var runs = await db.LibraryLinkSuggestionRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            Assert.Equal((LibraryLinkSuggestionRunStatus.Failed, (string?)RestartedError), (runs[running.Id].Status, runs[running.Id].Error));
            Assert.Equal(LibraryLinkSuggestionRunStatus.Queued, runs[queued.Id].Status);
        });
        Assert.True(_factory.Resolve<LibraryLinkSuggestionRunQueue>().Reader.TryRead(out var requeued));
        Assert.Equal(queued.Id, requeued);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>"120. Landing Stripling" (1962) of the Tom &amp; Jerry folder, no ids.</summary>
    private static JsonObject Landing() =>
        JellyfinSamples.MovieWith(LandingId, "120. Landing Stripling", null, 1962, $"{TomAndJerryFolder}/120. Landing Stripling (1962).mkv");

    private static JsonObject Calypso() =>
        JellyfinSamples.MovieWith(CalypsoId, "Calypso Cat", null, 1962, $"{TomAndJerryFolder}/119. Calypso Cat (1962).mkv");

    /// <summary>A cartoon Jellyfin knows the TMDb id of: never searched for, but read for what its library holds.</summary>
    private static JsonObject Identified() =>
        JellyfinSamples.MovieWith(IdentifiedId, "Том і Джеррі: Фільм", null, 1992, "/media/cartoons/Tom and Jerry The Movie (1992).mkv", tmdbId: "11134");

    private async Task<IntegrationConnectionEntity> SyncAsync(CancellationToken cancellationToken, params JsonObject[] movies)
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _jellyfin.Movies.AddRange(movies);
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        return jellyfin;
    }

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    /// <summary>A run the app queued, in its queue as a trigger leaves it.</summary>
    private async Task<Guid> QueueAsync(LibraryLinkSuggestionRunTrigger trigger)
    {
        var run = await _runs.Create(LibraryLinkSuggestionRunStatus.Queued, trigger);
        _factory.Resolve<LibraryLinkSuggestionRunQueue>().TryEnqueue(run.Id);
        return run.Id;
    }

    private async Task<Guid> WorkOfAsync(string sourceItemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == sourceItemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    /// <summary>The catalog searches sent, in order: the kind's path, the query, the year.</summary>
    private List<(string Kind, string Query, string? Year)> Searches() =>
    [
        .. _factory.TmdbHttp.Requests
            .Where(request => request.Uri.AbsolutePath.StartsWith("/3/search/", StringComparison.Ordinal))
            .Select(request =>
            {
                var query = QueryHelpers.ParseQuery(request.Uri.Query);
                var year = query.TryGetValue("year", out var film) ? film.ToString()
                    : query.TryGetValue("first_air_date_year", out var show) ? show.ToString()
                    : null;
                return (request.Uri.AbsolutePath["/3/search/".Length..], query["query"].ToString(), year);
            }),
    ];
}
