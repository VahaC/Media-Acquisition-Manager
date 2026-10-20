using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — the background half of a computation (Queue/Worker + reconciler), driven the way the host drives it: the
// worker's pass over the queue and the reconciler's startup pass, with runs asked for over real HTTP. The worker runs
// every queued run one after another and only a run that is still queued; a bug in one run fails that run and not the
// next; the catalog gone or refusing fails the run with its sentence; a shutdown leaves its run running, with the
// previous candidates untouched. The reconciler then fails a run a stop cut short and queues a queued run again.
public sealed class RecommendationRunWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations/runs";
    private const string AppErrorMessage = "The recommendations stopped computing because of an error in the app. Its log says more.";
    private const string RestartedError = "The app restarted before these recommendations were computed. The previous ones are kept.";
    private const string NoCatalogError = "Connect a metadata catalog under Integrations first.";
    private const string QueueFullError = "Too many recommendation runs are waiting. Try again in a minute.";
    private const string CatalogRemovedError = "The metadata catalog was removed while these recommendations were computed. The previous ones are kept.";

    private const string KeyRefusedError =
        "TMDb refused the key (401 Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API, not the shorter API key.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IRecommendationRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationRunWorkerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _runs = new RecommendationRunSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_takes_a_requested_run_to_succeeded_with_its_times_and_counts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var queuedAt = _factory.Time.GetUtcNow().UtcDateTime;
        var runId = await StartAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var ranAt = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        // An empty library seeds nothing, and every tab falls back to the catalog's best-rated works, of which this
        // catalog has none: a run that succeeded with nothing to recommend.
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal(
                (RecommendationRunStatus.Succeeded, queuedAt, ranAt, ranAt, 0, 0, (string?)null),
                (run.Status, run.CreatedUtc, run.StartedUtc, run.FinishedUtc, run.SeedCount, run.CandidateCount, TestTexts.En(run.Error)));
        });
        Assert.False(_factory.Resolve<RecommendationRunQueue>().Reader.TryRead(out _));
    }

    [Fact]
    public async Task RunQueued_with_a_run_that_throws_fails_that_run_with_the_app_error_and_still_runs_the_next()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var brokenRunId = await StartAsync(cancellationToken);
        Guid? nextRunId = null;
        // A bug, not an answer the adapter describes: nothing in the run catches it. The owner asks again while the
        // broken run is under way, so a run waits behind it.
        _tmdb.Intercept = _ =>
        {
            if (nextRunId is not null)
                return null;
            nextRunId = Task.Run(() => StartAsync(cancellationToken), cancellationToken).GetAwaiter().GetResult();
            throw new InvalidOperationException("A bug in the computation.");
        };
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        Assert.NotNull(nextRunId);
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.RecommendationRuns.AsNoTracking().SingleAsync(run => run.Id == brokenRunId, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, AppErrorMessage, now), (failed.Status, TestTexts.En(failed.Error), failed.FinishedUtc));
            // The next run in the queue still went.
            var next = await db.RecommendationRuns.AsNoTracking().SingleAsync(run => run.Id == nextRunId, cancellationToken);
            Assert.Equal(RecommendationRunStatus.Succeeded, next.Status);
        });
        // Error, so it ships off the box, with the exception for whoever reads the log.
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(RecommendationRunWorker).FullName);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains(brokenRunId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunQueued_with_a_dead_run_left_running_fails_it_with_the_app_error_and_runs_the_queued_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        // A run whose worker couldn't mark it failed: while the app keeps going, it still holds the one running slot.
        var dead = await _runs.Create(RecommendationRunStatus.Running, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5));
        var runId = await StartAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.RecommendationRuns.AsNoTracking().SingleAsync(run => run.Id == dead.Id, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, AppErrorMessage, now), (failed.Status, TestTexts.En(failed.Error), failed.FinishedUtc));
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(run => run.Id == runId, cancellationToken);
            Assert.Equal(RecommendationRunStatus.Succeeded, run.Status);
        });
    }

    [Fact]
    public async Task RunQueued_stopped_by_a_shutdown_leaves_the_run_running_and_the_previous_candidates_until_the_reconciler_fails_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync();
        var previous = await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Movie, "603", "Матриця", new DateOnly(1999, 3, 30), [28], catalog: catalog);
        await new RecommendationCandidateSeeder(_factory.Scope).Create(previous, RecommendationTab.Movies, score: 3.5);
        var runId = await StartAsync(cancellationToken);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // The app stops while the run reads the catalog's genres.
        _tmdb.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/genre/", StringComparison.Ordinal))
                shutdown.Cancel();
            return null;
        };

        //Act
        var exception = await Record.ExceptionAsync(() => _factory.Resolve<RecommendationRunWorker>().RunQueuedAsync(shutdown.Token));
        var runningAfterStop = await StatusOfAsync(runId, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(2));
        var restartedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.Resolve<RecommendationRunReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // A shutdown is not an error: the run stays running for the reconciler, and nothing it computed replaced the
        // candidates — they are replaced in one transaction at the very end.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(RecommendationRunStatus.Running, runningAfterStop);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(RecommendationRunWorker).FullName);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, RestartedError, restartedAt), (run.Status, TestTexts.En(run.Error), run.FinishedUtc));
            var kept = Assert.Single(await StoredRecommendation.ReadAsync(db, cancellationToken));
            Assert.Equal(("603", RecommendationTab.Movies, 3.5), (kept.CatalogId, kept.Tab, kept.Score));
        });
    }

    [Fact]
    public async Task Reconcile_queues_a_queued_run_again_and_the_worker_then_runs_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        // Queued before a stop: the run row survived, its id in the memory queue didn't.
        var queued = await _runs.Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced);

        //Act
        await _factory.Resolve<RecommendationRunReconciler>().ReconcileAsync(cancellationToken);
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        // Still the sync's run, computed now; the start queues no run of its own.
        Assert.Equal(RecommendationRunStatus.Succeeded, await StatusOfAsync(queued.Id, cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.RecommendationRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Reconcile_with_the_queue_full_fails_the_queued_run_with_the_queue_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var queued = await _runs.Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced);
        var queue = _factory.Resolve<RecommendationRunQueue>();
        for (var index = 0; index < RecommendationRunQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());

        //Act
        await _factory.Resolve<RecommendationRunReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // Left queued with no id, every later trigger would join it and nothing would run until the next start.
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(row => row.Id == queued.Id, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, QueueFullError), (run.Status, TestTexts.En(run.Error)));
        });
    }

    [Fact]
    public async Task RunQueued_after_the_catalog_was_switched_off_fails_the_run_with_the_sentence_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync();
        var runId = await StartAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == catalog.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));

        //Act
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, NoCatalogError, (int?)null), (run.Status, TestTexts.En(run.Error), run.CandidateCount));
        });
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task RunQueued_with_the_catalog_removed_mid_run_fails_the_run_with_a_sentence_not_as_an_app_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync();
        var runId = await StartAsync(cancellationToken);
        // MAM-33 — the owner removes the catalog while the run is asking it: the run passed its gates, and what the catalog
        // answers can't be stored as that catalog's any more.
        var removed = false;
        _tmdb.Intercept = request =>
        {
            if (!removed && request.RequestUri!.AbsolutePath.StartsWith("/3/genre/", StringComparison.Ordinal))
            {
                removed = true;
                Task.Run(
                        () => _factory.WithDbAsync(db => db.IntegrationConnections.Where(integration => integration.Id == catalog.Id).ExecuteDeleteAsync(cancellationToken)),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }

            return null;
        };

        //Act
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, CatalogRemovedError, (int?)null), (run.Status, TestTexts.En(run.Error), run.CandidateCount));
        });
        Assert.True(removed);
        // No bug of the app: nothing of its own at Error (EF Core logs the failed save itself), and the runner's Warning
        // names the status alone.
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Level >= LogLevel.Error && entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal));
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(RecommendationRunner).FullName && entry.Level == LogLevel.Warning);
        Assert.Contains("CatalogRemoved", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunQueued_with_the_key_refused_fails_with_the_catalogs_sentence_after_the_connection_test_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync(apiKey: "not-the-tmdb-token-0001");
        var runId = await StartAsync(cancellationToken);

        //Act
        await _factory.RunQueuedRecommendationsAsync(cancellationToken);

        //Assert
        // The connection test is the gate, as for a library sync: nothing else is asked of a catalog that refuses.
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, KeyRefusedError), (run.Status, TestTexts.En(run.Error)));
        });
        var request = Assert.Single(_factory.TmdbHttp.Requests);
        Assert.Equal("/3/authentication", request.Uri.AbsolutePath);
        // Warning with the status only: never the sentence, the address or a title.
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(RecommendationRunner).FullName && entry.Level == LogLevel.Warning);
        Assert.Contains("AuthenticationFailed", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb.test", warning.Message, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync(string apiKey = FakeTmdbServer.Token) =>
        new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: apiKey);

    private async Task<Guid> StartAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        var run = await response.Content.ReadFromJsonAsync<RecommendationRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        return run!.Id;
    }

    private async Task<RecommendationRunStatus> StatusOfAsync(Guid runId, CancellationToken cancellationToken)
    {
        RecommendationRunStatus status = default;
        await _factory.WithDbAsync(async db =>
            status = (await db.RecommendationRuns.AsNoTracking().SingleAsync(run => run.Id == runId, cancellationToken)).Status);
        return status;
    }
}
