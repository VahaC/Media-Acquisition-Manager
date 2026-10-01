using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the background half of a sync (Queue/Worker + reconciler), driven the way the host drives it:
// the worker's pass over the queue and the reconciler's startup pass, with runs started over real HTTP.
// A bug in one sync fails that run and not the next; a shutdown leaves its run for the reconciler, which
// fails every run a restart cut short and nothing else.
public sealed class LibrarySyncWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";
    private const string AppErrorMessage = "The sync stopped because of an error in the app. Its log says more.";
    private const string RestartedError = "The app restarted before this sync finished. Start it again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeJellyfinServer _secondJellyfin = null!;

    public LibrarySyncWorkerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _secondJellyfin = new FakeJellyfinServer(SecondJellyfinAddress) { ApiKey = ApiKey };
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, _secondJellyfin);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_with_a_sync_that_throws_fails_that_run_with_the_app_error_and_still_runs_the_next()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var broken = await _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);
        var working = await _integrations.Create("jellyfin", "Bedroom", SecondJellyfinAddress, apiKey: ApiKey);
        // A bug, not a failure the adapter describes: nothing in the sync catches it.
        _jellyfin.Intercept = request => request.RequestUri!.AbsolutePath == "/Items"
            ? throw new InvalidOperationException("A bug in the sync.")
            : null;
        var brokenRun = await StartAsync(broken, cancellationToken);
        var workingRun = await StartAsync(working, cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.LibrarySyncRuns.SingleAsync(run => run.Id == brokenRun, cancellationToken);
            Assert.Equal((LibrarySyncStatus.Failed, AppErrorMessage, now), (failed.Status, failed.Error, failed.FinishedUtc));
            // The next run in the queue still went, and its library is all the snapshot holds.
            var succeeded = await db.LibrarySyncRuns.SingleAsync(run => run.Id == workingRun, cancellationToken);
            Assert.Equal((LibrarySyncStatus.Succeeded, 3), (succeeded.Status, succeeded.WorksCount));
            var snapshot = await StoredLibrarySnapshot.ReadAsync(db, cancellationToken);
            Assert.All(snapshot.Titles, title => Assert.Equal(working.Id, title.SourceConnectionId));
        });
        // Error, so it ships off the box, with the exception for whoever reads the log.
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibrarySyncWorker).FullName);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains(brokenRun.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunQueued_stopped_by_a_shutdown_mid_sync_leaves_the_run_running_until_the_reconciler_fails_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);
        var runId = await StartAsync(jellyfin, cancellationToken);
        using var shutdown = new CancellationTokenSource();
        // The host stops while the first page is being read: the request is cancelled under the worker.
        _jellyfin.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/Items")
                return null;
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var restartedAt = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var stopped = await Record.ExceptionAsync(() => _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(shutdown.Token));
        LibrarySyncRunEntity? afterStop = null;
        await _factory.WithDbAsync(async db => afterStop = await db.LibrarySyncRuns.AsNoTracking().SingleAsync(cancellationToken));
        await _factory.Resolve<LibrarySyncReconciler>().ReconcileAsync(cancellationToken);
        var afterRestart = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{runId}", _owner.AccessToken);

        //Assert
        // A shutdown is not a bug: no error logged, no outcome invented; the run is left running.
        Assert.IsAssignableFrom<OperationCanceledException>(stopped);
        Assert.Equal((LibrarySyncStatus.Running, null, null), (afterStop!.Status, afterStop.FinishedUtc, afterStop.Error));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        // The next start's reconciler settles it, with a sentence saying what to do.
        var run = await afterRestart.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal((LibrarySyncStatus.Failed, restartedAt, RestartedError), (run!.Status, run.FinishedUtc, run.Error));
        await _factory.WithDbAsync(async db =>
            Assert.Empty((await StoredLibrarySnapshot.ReadAsync(db, cancellationToken)).Rows()));
    }

    [Fact]
    public async Task RunQueued_with_a_run_that_is_no_longer_queued_leaves_it_as_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);
        var runId = await StartAsync(jellyfin, cancellationToken);
        // Settled before the worker reached it — its id is still in the queue.
        await _factory.Resolve<LibrarySyncReconciler>().ReconcileAsync(cancellationToken);

        //Act
        await _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        // Only a queued run is claimed: a finished one is never reopened, and nothing reaches Jellyfin.
        Assert.Empty(_factory.JellyfinHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibrarySyncRuns.SingleAsync(row => row.Id == runId, cancellationToken);
            Assert.Equal((LibrarySyncStatus.Failed, RestartedError, null), (run.Status, run.Error, run.StartedUtc));
            Assert.Empty((await StoredLibrarySnapshot.ReadAsync(db, cancellationToken)).Rows());
        });
    }

    [Fact]
    public async Task Reconcile_fails_every_queued_and_running_run_with_the_restart_sentence_and_leaves_finished_ones()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", SecondJellyfinAddress, apiKey: ApiKey);
        var runs = new LibrarySyncRunSeeder(_factory.Scope);
        var earlier = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var succeeded = await runs.Create(livingRoom, LibrarySyncStatus.Succeeded, _owner.User, earlier);
        var failed = await runs.Create(livingRoom, LibrarySyncStatus.Failed, _owner.User, earlier.AddMinutes(1), "Jellyfin answered 500 Internal Server Error. Its own log says why.");
        var queued = await runs.Create(livingRoom, LibrarySyncStatus.Queued, _owner.User, earlier.AddMinutes(2));
        var running = await runs.Create(bedroom, LibrarySyncStatus.Running, _owner.User, earlier.AddMinutes(3));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.Resolve<LibrarySyncReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.LibrarySyncRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            Assert.Equal((LibrarySyncStatus.Failed, RestartedError, now), (rows[queued.Id].Status, rows[queued.Id].Error, rows[queued.Id].FinishedUtc));
            Assert.Equal((LibrarySyncStatus.Failed, RestartedError, now), (rows[running.Id].Status, rows[running.Id].Error, rows[running.Id].FinishedUtc));
            // Finished runs are history: their outcome stays as it was.
            Assert.Equal((LibrarySyncStatus.Succeeded, null, succeeded.FinishedUtc), (rows[succeeded.Id].Status, rows[succeeded.Id].Error, rows[succeeded.Id].FinishedUtc));
            Assert.Equal((LibrarySyncStatus.Failed, failed.Error, failed.FinishedUtc), (rows[failed.Id].Status, rows[failed.Id].Error, rows[failed.Id].FinishedUtc));
        });
    }

    [Fact]
    public async Task Reconcile_frees_the_integration_for_a_new_sync()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySyncRunSeeder(_factory.Scope).Create(jellyfin, LibrarySyncStatus.Running, _owner.User, _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1));
        var blocked = await _factory.PostHttpRequest($"{BaseUrl}/sources/{jellyfin.Id}/syncs", _owner.AccessToken);

        //Act
        await _factory.Resolve<LibrarySyncReconciler>().ReconcileAsync(cancellationToken);
        var response = await _factory.PostHttpRequest($"{BaseUrl}/sources/{jellyfin.Id}/syncs", _owner.AccessToken);

        //Assert
        // A run a restart cut short would otherwise block its integration for good.
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // The owner's start over HTTP; the queued run's id.
    private async Task<Guid> StartAsync(IntegrationConnectionEntity integration, CancellationToken cancellationToken)
    {
        using var response = await _factory.PostHttpRequest($"{BaseUrl}/sources/{integration.Id}/syncs", _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Id;
    }
}
