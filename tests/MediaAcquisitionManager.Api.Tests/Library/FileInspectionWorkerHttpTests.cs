using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-9 — the background half of an inspection (Queue/Worker + reconciler), driven the way the host drives
// it: the worker's pass over the queue and the reconciler's startup pass, with runs asked for over real HTTP.
// The worker runs every queued run one after another, a run queued meanwhile included, and only a run that
// is still queued; a bug in one run fails that run and not the next; a shutdown leaves its run running, with
// what it had written on the files kept. The reconciler then fails a run a stop cut short, queues a queued
// run again, and queues the start's own run — or joins the waiting one — when a media folder is allowed.
public sealed class FileInspectionWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string MediaRoot = "/media";
    private const string AppErrorMessage = "The inspection stopped because of an error in the app. Its log says more.";
    private const string RestartedError = "The app restarted before this inspection finished. What it had read is kept.";

    /// <summary>MAM-47 — the worker's and the reconciler's reasons as they store them: key and values (ADR 0042).</summary>
    private static readonly Message AppError = Message.Of("files.inspection.app-error");

    private static readonly Message Restarted = Message.Of("files.inspection.restarted");
    private const string ContinentalPath = "/media/movies/The Continental (2023)/The Continental S01E01.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux.mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IFileInspectionRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;

    public FileInspectionWorkerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        _runs = new FileInspectionRunSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        // The library the planted file names come from; with no path mapping, the app looks for each where
        // the library says it is.
        _jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_runs_a_run_queued_during_another_after_it_and_that_one_reads_the_files_named_meanwhile()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await PlantFileAsync(_factory.Scope, ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        var firstRunId = await StartAsync(cancellationToken);
        Guid? secondRunId = null;
        FileInspectionRunStatus? firstWhenSecondRead = null;
        // While the first run reads its one file, a sync names a second and the owner asks again: the
        // first listed its files already, so only a run behind it reads the new one.
        _factory.Files.DuringRead = async (path, _) =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            if (path == ContinentalPath && secondRunId is null)
            {
                await PlantFileAsync(scope.ServiceProvider, TakenPath);
                _factory.Files.AddFile(TakenPath, TestMedia.File(inode: 102), TestMedia.Taken());
                _factory.Time.Advance(TimeSpan.FromMinutes(1));
                secondRunId = await StartAsync(cancellationToken);
            }
            else if (path == TakenPath)
            {
                firstWhenSecondRead = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().FileInspectionRuns
                    .AsNoTracking()
                    .SingleAsync(run => run.Id == firstRunId, cancellationToken)).Status;
            }
        };

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.NotNull(secondRunId);
        Assert.NotEqual(firstRunId, secondRunId);
        // One run at a time, in queue order: the first had finished before the second read anything.
        Assert.Equal(FileInspectionRunStatus.Succeeded, firstWhenSecondRead);
        Assert.Equal([ContinentalPath, TakenPath], _factory.Files.ReadPaths);
        Assert.False(_factory.Resolve<FileInspectionQueue>().Reader.TryRead(out _));
        await _factory.WithDbAsync(async db =>
        {
            var runs = await db.FileInspectionRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            var first = runs[firstRunId];
            var second = runs[secondRunId.Value];
            Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 1), (first.Status, first.FilesTotal, first.FilesChecked, first.FilesProbed));
            // The second looked at both names and read only the new one: the first's reading answered for
            // the other.
            Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (second.Status, second.FilesTotal, second.FilesChecked, second.FilesProbed));
            Assert.True(second.StartedUtc >= first.FinishedUtc);
            Assert.All(
                await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken),
                file => Assert.Equal(FileInspectionStatus.Inspected, file.InspectionStatus));
            Assert.Equal(2, await db.MediaInspections.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task RunQueued_with_a_run_that_throws_fails_that_run_with_the_app_error_and_still_runs_the_next()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await PlantFileAsync(_factory.Scope, ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        var brokenRunId = await StartAsync(cancellationToken);
        Guid? nextRunId = null;
        // A bug, not an answer the inventory describes: nothing in the run catches it. The owner asks again
        // while the broken run is under way, so a run waits behind it.
        _factory.Files.DuringRead = async (_, _) =>
        {
            if (nextRunId is not null)
                return;
            nextRunId = await StartAsync(cancellationToken);
            throw new InvalidOperationException("A bug in the inspection.");
        };
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.NotNull(nextRunId);
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.FileInspectionRuns.AsNoTracking().SingleAsync(run => run.Id == brokenRunId, cancellationToken);
            Assert.Equal((FileInspectionRunStatus.Failed, AppErrorMessage, now), (failed.Status, TestTexts.En(failed.Error), failed.FinishedUtc));
            // The next run in the queue still went, and read the file the broken one couldn't.
            var next = await db.FileInspectionRuns.AsNoTracking().SingleAsync(run => run.Id == nextRunId, cancellationToken);
            Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1), (next.Status, next.FilesChecked, next.FilesProbed));
            var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(FileInspectionStatus.Inspected, file.InspectionStatus);
            Assert.NotNull(file.MediaInspectionId);
        });
        // Error, so it ships off the box, with the exception for whoever reads the log.
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionWorker).FullName);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains(brokenRunId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunQueued_with_a_dead_run_left_running_fails_it_with_the_app_error_and_runs_the_queued_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await PlantFileAsync(_factory.Scope, ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        // A run whose worker couldn't mark it failed, the database refusing that too: while the app keeps
        // going, it still holds the one running slot the claim needs.
        var dead = await _runs.Create(
            FileInspectionRunStatus.Running, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5), filesTotal: 1);
        var runId = await StartAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        // The worker runs one run at a time, so the other running one is dead: failed with the app error,
        // and the queued run claims the slot and reads the file, without restarting the app.
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.FileInspectionRuns.AsNoTracking().SingleAsync(run => run.Id == dead.Id, cancellationToken);
            Assert.Equal((FileInspectionRunStatus.Failed, AppErrorMessage, now), (failed.Status, TestTexts.En(failed.Error), failed.FinishedUtc));
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(run => run.Id == runId, cancellationToken);
            Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1), (run.Status, run.FilesChecked, run.FilesProbed));
        });
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionWorker).FullName);
    }

    [Fact]
    public async Task RunQueued_stopped_by_a_shutdown_mid_read_leaves_the_run_running_and_keeps_what_it_wrote_until_the_reconciler_fails_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var continental = await PlantFileAsync(_factory.Scope, ContinentalPath);
        var taken = await PlantFileAsync(_factory.Scope, TakenPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        // The first inspection reads the one file on disk; the other isn't there yet.
        var firstRun = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var firstLookedAt = _factory.Time.GetUtcNow().UtcDateTime;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        _factory.Files.AddFile(TakenPath, TestMedia.File(inode: 102), TestMedia.Taken());
        var runId = await StartAsync(cancellationToken);
        using var shutdown = new CancellationTokenSource();
        // The host stops while the new file is being read: the read is cancelled under the worker.
        _factory.Files.DuringRead = (_, token) =>
        {
            shutdown.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var stoppedAt = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var stopped = await Record.ExceptionAsync(() => _factory.Resolve<FileInspectionWorker>().RunQueuedAsync(shutdown.Token));
        FileInspectionRunEntity? afterStop = null;
        await _factory.WithDbAsync(async db => afterStop = await db.FileInspectionRuns.AsNoTracking().SingleAsync(run => run.Id == runId, cancellationToken));
        await _factory.Resolve<FileInspectionReconciler>().ReconcileAsync(cancellationToken);
        var afterRestart = await _factory.GetHttpRequest($"{BaseUrl}/{runId}", _owner.AccessToken);

        //Assert
        Assert.Equal(FileInspectionRunStatus.Succeeded, firstRun.Status);
        // A shutdown is not a bug: no error logged, no outcome invented; the run is left running.
        Assert.IsAssignableFrom<OperationCanceledException>(stopped);
        Assert.Equal((FileInspectionRunStatus.Running, null, null), (afterStop!.Status, afterStop.FinishedUtc, afterStop.Error));
        Assert.Equal((2, 1, 0), (afterStop.FilesTotal, afterStop.FilesChecked, afterStop.FilesProbed));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        // The next start's reconciler settles it, with a sentence saying nothing it read is lost.
        var run = (await afterRestart.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((FileInspectionRunStatus.Failed, stoppedAt, RestartedError), (run.Status, run.FinishedUtc, run.Error));
        // ...and queues the start's own run, which carries on from what was written.
        var startRunId = Assert.Single(DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var startRun = await db.FileInspectionRuns.AsNoTracking().SingleAsync(row => row.Id == startRunId, cancellationToken);
            Assert.Equal((FileInspectionTrigger.AppStarted, FileInspectionRunStatus.Queued, null, stoppedAt), (startRun.Trigger, startRun.Status, startRun.RequestedByUserId, startRun.CreatedUtc));
            Assert.Equal(3, await db.FileInspectionRuns.CountAsync(cancellationToken));
            var files = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.Id, cancellationToken);
            var reading = await db.MediaInspections.AsNoTracking().SingleAsync(cancellationToken);
            // The name answered from the cache before the stop keeps what the stopped run wrote on it.
            Assert.Equal(
                (FileInspectionStatus.Inspected, stoppedAt, reading.Id, TestMedia.DeviceId, 101L),
                (files[continental.Id].InspectionStatus, files[continental.Id].InspectedUtc, files[continental.Id].MediaInspectionId, files[continental.Id].DeviceId, files[continental.Id].Inode));
            // The name whose read was cut short keeps what the first run found, nothing invented.
            Assert.Equal(
                (FileInspectionStatus.NotFound, firstLookedAt, null),
                (files[taken.Id].InspectionStatus, files[taken.Id].InspectedUtc, files[taken.Id].MediaInspectionId));
        });
    }

    [Fact]
    public async Task RunQueued_with_the_same_run_queued_twice_runs_it_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await PlantFileAsync(_factory.Scope, ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        var runId = await StartAsync(cancellationToken);
        // The reconciler queues a queued run's id again; the request had queued it already.
        _factory.Resolve<FileInspectionQueue>().TryEnqueue(runId);

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        // The atomic claim: only a queued run turns running, so the second id finds nothing to do.
        Assert.Equal([ContinentalPath], _factory.Files.DescribedPaths);
        Assert.Equal([ContinentalPath], _factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((runId, FileInspectionRunStatus.Succeeded, 1, 1), (run.Id, run.Status, run.FilesChecked, run.FilesProbed));
        });
    }

    [Theory]
    [InlineData(FileInspectionRunStatus.Running)]
    [InlineData(FileInspectionRunStatus.Succeeded)]
    [InlineData(FileInspectionRunStatus.Failed)]
    public async Task RunQueued_of_a_run_that_is_no_longer_queued_leaves_it_as_it_is(FileInspectionRunStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await PlantFileAsync(_factory.Scope, ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var error = status == FileInspectionRunStatus.Failed ? RestartedError : null;
        var settled = await _runs.Create(status, requestedBy: _owner.User, createdUtc: at, error: error is null ? null : Restarted, filesTotal: 1, filesChecked: 1);
        // Its id still waits in the queue: taken by another pass, or settled before the worker reached it.
        _factory.Resolve<FileInspectionQueue>().TryEnqueue(settled.Id);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        // Only a queued run is claimed: nothing is looked at, and the run is never reopened or restarted.
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((status, at, error, 1), (run.Status, run.StartedUtc, TestTexts.En(run.Error), run.FilesChecked));
            Assert.Equal(settled.FinishedUtc, run.FinishedUtc);
            Assert.Null((await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken)).InspectionStatus);
        });
    }

    [Fact]
    public async Task Reconcile_fails_the_running_run_with_the_restart_sentence_leaves_finished_ones_and_queues_the_start_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var earlier = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var succeeded = await _runs.Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.LibrarySynced, createdUtc: earlier, filesTotal: 4, filesChecked: 4, filesProbed: 4);
        var failed = await _runs.Create(FileInspectionRunStatus.Failed, requestedBy: _owner.User, createdUtc: earlier.AddMinutes(1), error: AppError);
        var running = await _runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.AppStarted, createdUtc: earlier.AddMinutes(2), filesTotal: 4, filesChecked: 2, filesProbed: 1);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.Resolve<FileInspectionReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        var startRunId = Assert.Single(DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.FileInspectionRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            Assert.Equal(4, rows.Count);
            // Died with the process: failed, its progress kept as the record of how far it got.
            Assert.Equal(
                (FileInspectionRunStatus.Failed, RestartedError, now, 2, 1),
                (rows[running.Id].Status, TestTexts.En(rows[running.Id].Error), rows[running.Id].FinishedUtc, rows[running.Id].FilesChecked, rows[running.Id].FilesProbed));
            // Finished runs are history: their outcome stays as it was.
            Assert.Equal((FileInspectionRunStatus.Succeeded, null, succeeded.FinishedUtc), (rows[succeeded.Id].Status, rows[succeeded.Id].Error, rows[succeeded.Id].FinishedUtc));
            Assert.Equal((FileInspectionRunStatus.Failed, AppErrorMessage, failed.FinishedUtc), (rows[failed.Id].Status, TestTexts.En(rows[failed.Id].Error), rows[failed.Id].FinishedUtc));
            Assert.Equal(
                (FileInspectionTrigger.AppStarted, FileInspectionRunStatus.Queued, null, now),
                (rows[startRunId].Trigger, rows[startRunId].Status, rows[startRunId].RequestedByUserId, rows[startRunId].CreatedUtc));
        });
    }

    [Fact]
    public async Task Reconcile_queues_a_queued_run_again_and_joins_it_instead_of_adding_the_start_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        // Queued before the stop: the row survived, its id in the memory queue didn't.
        var queued = await _runs.Create(FileInspectionRunStatus.Queued, requestedBy: _owner.User, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        //Act
        await _factory.Resolve<FileInspectionReconciler>().ReconcileAsync(cancellationToken);
        var queuedIds = DrainQueue();

        //Assert
        // Still worth doing — it asks for nothing but "look at the files" — and the start would queue one
        // anyway: one run, queued once, still the owner's.
        Assert.Equal([queued.Id], queuedIds);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (queued.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Queued, _owner.User.Id),
                (run.Id, run.Trigger, run.Status, run.RequestedByUserId));
        });
    }

    [Fact]
    public async Task Reconcile_without_an_allowed_root_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var finished = await _runs.Create(FileInspectionRunStatus.Succeeded, requestedBy: _owner.User, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1));

        //Act
        await _factory.Resolve<FileInspectionReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // No folder allowed, which is the default: the app's start reads nothing and queues no run.
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((finished.Id, FileInspectionRunStatus.Succeeded), (run.Id, run.Status));
        });
    }

    // The owner's request over HTTP; the queued (or joined) run's id.
    private async Task<Guid> StartAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Id;
    }

    // A file name the Living room library reported, in a work and version of its own, through the given
    // scope's context (a fresh one when planted while a run is under way).
    private async Task<PhysicalFileEntity> PlantFileAsync(IServiceProvider scope, string servicePath)
    {
        var db = scope.GetRequiredService<ApplicationDbContext>();
        var work = await new MediaWorkDataFactory(db, _factory.Time).Create();
        var version = await new MediaVersionDataFactory(db, _factory.Time).Create(
            work.Id, sourceItemId: Guid.NewGuid().ToString("N"), sourceConnectionId: _jellyfin.Id, sourceName: _jellyfin.Name);
        return await new PhysicalFileDataFactory(db, _factory.Time).Create(
            version.Id, servicePath, sourceConnectionId: _jellyfin.Id, sourceName: _jellyfin.Name);
    }

    // Every run id waiting for the worker, in queue order; the queue is empty afterwards.
    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<FileInspectionQueue>();
        var runIds = new List<Guid>();
        while (queue.Reader.TryRead(out var runId))
            runIds.Add(runId);
        return runIds;
    }
}
