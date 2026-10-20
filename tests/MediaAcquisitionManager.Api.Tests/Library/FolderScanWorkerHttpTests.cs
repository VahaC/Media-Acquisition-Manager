using System.Data.Common;
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

// MAM-18 — the background half of a scan of the media folders (Queue/Worker + reconciler), driven the way
// the host drives it: the worker's pass over the queue and the reconciler's startup pass, with scans asked
// for over real HTTP. The worker runs only a scan that is still queued, and one at a time; a running scan
// left behind by a worker that couldn't settle it is failed by the next claim; a bug in one scan fails that
// scan and not the next; a shutdown — while walking or while saving — leaves its scan running with nothing
// stored, and a scan whose outcome can't be written takes back every name it had saved. The reconciler then fails a scan a stop cut short and queues a queued one again, and never queues
// a scan of its own: a walk of every folder is the owner's to start.
public sealed class FolderScanWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/scans";
    private const string MediaRoot = "/media";
    private const string AppErrorMessage = "The scan stopped because of an error in the app. Its log says more.";
    private const string RestartedError = "The app restarted before this scan finished, so nothing it found was stored. Scan the folders again.";
    private const string QueueFullError = "Too many folder scans are waiting. Try again in a minute.";

    /// <summary>MAM-47 — the worker's and the reconciler's reasons as they store them: key and values (ADR 0042).</summary>
    private static readonly Message AppError = Message.Of("files.scan.app-error");

    private static readonly Message Restarted = Message.Of("files.scan.restarted");
    private const string InterstellarPath = "/media/downloads/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv";
    private const string BirthdayPath = "/media/home/Birthday 2019.mkv";
    private const string GonePath = "/media/downloads/Gone (2001)/Gone (2001).mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IFolderScanRunSeeder _runs = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;

    public FolderScanWorkerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        _runs = new FolderScanRunSeeder(_factory.Scope);
        _physicalFiles = new PhysicalFileDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_with_a_scan_that_throws_fails_that_scan_with_the_app_error_and_still_runs_the_next()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        var brokenRunId = await StartAsync(cancellationToken);
        Guid? nextRunId = null;
        // A bug, not an entry the inventory describes: nothing in the scan catches it. The owner asks again
        // while the broken scan is under way, so a scan waits behind it.
        _factory.Files.DuringListing = async (_, _) =>
        {
            if (nextRunId is not null)
                return;
            nextRunId = await StartAsync(cancellationToken);
            throw new InvalidOperationException("A bug in the walk.");
        };
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);

        //Assert
        Assert.NotNull(nextRunId);
        Assert.NotEqual(brokenRunId, nextRunId);
        Assert.Equal(2, _factory.Files.ListedRoots.Count);
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.FolderScanRuns.AsNoTracking().SingleAsync(run => run.Id == brokenRunId, cancellationToken);
            Assert.Equal((FolderScanStatus.Failed, AppErrorMessage, now, null), (failed.Status, TestTexts.En(failed.Error), failed.FinishedUtc, failed.FilesUnreported));
            // The next scan in the queue still went, and stored the name the broken one couldn't.
            var next = await db.FolderScanRuns.AsNoTracking().SingleAsync(run => run.Id == nextRunId, cancellationToken);
            Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (next.Status, next.FilesFound, next.FilesUnreported));
            Assert.Equal(InterstellarPath, (await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken)).ServicePath);
        });
        // Error, so it ships off the box, with the exception for whoever reads the log.
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanWorker).FullName);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains(brokenRunId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunQueued_with_a_dead_scan_left_running_fails_it_with_the_app_error_and_runs_the_queued_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        // A scan whose worker couldn't mark it failed, the database refusing that too: while the app keeps
        // going, it still holds the one running slot the claim needs.
        var dead = await _runs.Create(FolderScanStatus.Running, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5), filesFound: 50);
        var runId = await StartAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);

        //Assert
        // The worker runs one scan at a time, so the other running one is dead: failed with the app error,
        // and the queued scan claims the slot and walks, without restarting the app.
        await _factory.WithDbAsync(async db =>
        {
            var failed = await db.FolderScanRuns.AsNoTracking().SingleAsync(run => run.Id == dead.Id, cancellationToken);
            Assert.Equal((FolderScanStatus.Failed, AppErrorMessage, now, 50), (failed.Status, TestTexts.En(failed.Error), failed.FinishedUtc, failed.FilesFound));
            var run = await db.FolderScanRuns.AsNoTracking().SingleAsync(run => run.Id == runId, cancellationToken);
            Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (run.Status, run.FilesFound, run.FilesUnreported));
        });
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanWorker).FullName);
    }

    [Fact]
    public async Task RunQueued_with_the_same_scan_queued_twice_runs_it_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        var runId = await StartAsync(cancellationToken);
        // The reconciler queues a queued scan's id again; the request had queued it already.
        _factory.Resolve<FolderScanQueue>().TryEnqueue(runId);

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);

        //Assert
        // The atomic claim: only a queued scan turns running, so the second id finds nothing to do.
        Assert.Single(_factory.Files.ListedRoots);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FolderScanRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((runId, FolderScanStatus.Succeeded, 1, 1), (run.Id, run.Status, run.FilesFound, run.FilesUnreported));
            Assert.Equal(1, await db.PhysicalFiles.CountAsync(cancellationToken));
            // One scan, one inspection after it.
            Assert.Equal(1, await db.FileInspectionRuns.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData(FolderScanStatus.Running)]
    [InlineData(FolderScanStatus.Succeeded)]
    [InlineData(FolderScanStatus.Failed)]
    public async Task RunQueued_of_a_scan_that_is_no_longer_queued_leaves_it_and_every_name_as_they_are(FolderScanStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var error = status == FolderScanStatus.Failed ? RestartedError : null;
        var settled = await _runs.Create(status, _owner.User, at, filesFound: 7, filesUnreported: status == FolderScanStatus.Succeeded ? 1 : null, error: error is null ? null : Restarted);
        // Its id still waits in the queue: taken by another pass, or settled before the worker reached it.
        _factory.Resolve<FolderScanQueue>().TryEnqueue(settled.Id);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);

        //Assert
        // Only a queued scan is claimed: nothing is walked, and the scan is never reopened or restarted.
        Assert.Empty(_factory.Files.ListedRoots);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FolderScanRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((status, at, error, 7), (run.Status, run.StartedUtc, TestTexts.En(run.Error), run.FilesFound));
            Assert.Equal(settled.FinishedUtc, run.FinishedUtc);
            Assert.Equal((gone.Id, gone.ObservedUtc), Assert.Single((await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken)).Select(file => (file.Id, file.ObservedUtc))));
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task RunQueued_stopped_by_a_shutdown_mid_walk_leaves_the_scan_running_with_nothing_stored_until_the_reconciler_fails_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        var runId = await StartAsync(cancellationToken);
        using var shutdown = new CancellationTokenSource();
        // The host stops while the second file is listed: the walk is cancelled under the worker.
        _factory.Files.DuringListing = (entry, token) =>
        {
            if (entry.AppPath != BirthdayPath)
                return Task.CompletedTask;
            shutdown.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var stoppedAt = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var stopped = await Record.ExceptionAsync(() => _factory.Resolve<FolderScanWorker>().RunQueuedAsync(shutdown.Token));
        var afterStop = await RunRowAsync(runId, cancellationToken);
        await _factory.Resolve<FolderScanReconciler>().ReconcileAsync(cancellationToken);
        var afterRestart = await _factory.GetHttpRequest($"{BaseUrl}/{runId}", _owner.AccessToken);

        //Assert
        // A shutdown is not a bug: no error logged, no outcome invented; the scan is left running, and as it
        // stores only once its walk is over, nothing it found was stored.
        Assert.IsAssignableFrom<OperationCanceledException>(stopped);
        Assert.Equal((FolderScanStatus.Running, null, null, null), (afterStop.Status, afterStop.FinishedUtc, afterStop.Error, afterStop.FilesUnreported));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal([(gone.Id, GonePath)], await NamesAsync(cancellationToken));
        // The next start's reconciler settles it, saying nothing was stored; no scan of its own, no inspection.
        var run = (await afterRestart.Content.ReadFromJsonAsync<FolderScanRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((FolderScanStatus.Failed, stoppedAt, RestartedError), (run.Status, run.FinishedUtc, run.Error));
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.FolderScanRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task RunQueued_stopped_by_a_shutdown_while_saving_leaves_the_scan_running_with_nothing_stored()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        var runId = await StartAsync(cancellationToken);
        using var shutdown = new CancellationTokenSource();
        // The walk is over and the replacement — the new names in, the gone one out — is about to be saved when the
        // host stops.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<PhysicalFileEntity>().Any(entry => entry.State == EntityState.Added),
            token =>
            {
                shutdown.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        //Act
        var stopped = await Record.ExceptionAsync(() => _factory.Resolve<FolderScanWorker>().RunQueuedAsync(shutdown.Token));
        var afterStop = await RunRowAsync(runId, cancellationToken);

        //Assert
        // The save is cancelled before it sends a command, so nothing was written: the names stay as they were, and
        // a shutdown being no bug, the scan is left running for the reconciler, with no outcome and no inspection.
        Assert.IsAssignableFrom<OperationCanceledException>(stopped);
        Assert.Equal((FolderScanStatus.Running, 2, null), (afterStop.Status, afterStop.FilesFound, afterStop.FilesUnreported));
        Assert.Equal([(gone.Id, GonePath)], await NamesAsync(cancellationToken));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task RunQueued_whose_outcome_cannot_be_written_rolls_back_every_name_it_had_saved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        var runId = await StartAsync(cancellationToken);
        // The replacement is saved — the two new names in, the gone one out — and then marking the scan succeeded
        // fails, as a full disk or a broken database would fail it: the last step of the one transaction. EF sends the
        // status as a parameter, so the command is told by its table and that parameter.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("FolderScanRuns", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, nameof(FolderScanStatus.Succeeded))),
            _ => throw new InvalidOperationException("The database refused the scan's outcome."));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);

        //Assert
        // Everything the scan had saved is rolled back with its outcome: the gone name is still there with its id, and
        // neither new name was stored.
        Assert.Equal([(gone.Id, GonePath)], await NamesAsync(cancellationToken));
        var run = await RunRowAsync(runId, cancellationToken);
        Assert.Equal(
            (FolderScanStatus.Failed, AppErrorMessage, now, 2, null),
            (run.Status, TestTexts.En(run.Error), run.FinishedUtc, run.FilesFound, run.FilesUnreported));
        // Nothing new was stored, so nothing waits for an inspection.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanWorker).FullName);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task Reconcile_fails_the_running_scan_with_the_restart_sentence_leaves_finished_ones_and_queues_the_queued_one_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var earlier = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var succeeded = await _runs.Create(FolderScanStatus.Succeeded, _owner.User, earlier, filesFound: 4, filesUnreported: 1);
        var failed = await _runs.Create(FolderScanStatus.Failed, _owner.User, earlier.AddMinutes(1), error: AppError);
        var running = await _runs.Create(FolderScanStatus.Running, _owner.User, earlier.AddMinutes(2), filesFound: 50);
        // Queued before the stop: the row survived, its id in the memory queue didn't.
        var queued = await _runs.Create(FolderScanStatus.Queued, _owner.User, earlier.AddMinutes(3));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.Resolve<FolderScanReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // Queued again, once, and still the owner's; no scan of the reconciler's own beside it.
        Assert.Equal([queued.Id], DrainQueue());
        Assert.Empty(_factory.Files.ListedRoots);
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.FolderScanRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            Assert.Equal(4, rows.Count);
            // Died with the process: failed, its progress kept as the record of how far it got.
            Assert.Equal(
                (FolderScanStatus.Failed, RestartedError, now, 50, null),
                (rows[running.Id].Status, TestTexts.En(rows[running.Id].Error), rows[running.Id].FinishedUtc, rows[running.Id].FilesFound, rows[running.Id].FilesUnreported));
            // Finished scans are history: their outcome stays as it was.
            Assert.Equal((FolderScanStatus.Succeeded, null, succeeded.FinishedUtc), (rows[succeeded.Id].Status, rows[succeeded.Id].Error, rows[succeeded.Id].FinishedUtc));
            Assert.Equal((FolderScanStatus.Failed, AppErrorMessage, failed.FinishedUtc), (rows[failed.Id].Status, TestTexts.En(rows[failed.Id].Error), rows[failed.Id].FinishedUtc));
            Assert.Equal(
                (FolderScanStatus.Queued, _owner.User.Id, null),
                (rows[queued.Id].Status, rows[queued.Id].RequestedByUserId!.Value, rows[queued.Id].StartedUtc));
            // Nothing was stored, so nothing new waits for an inspection.
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken));
        });
        var settled = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanReconciler).FullName);
        Assert.Equal(
            (LogLevel.Information, "1 folder scans were running when the app stopped; they are marked failed."),
            (settled.Level, settled.Message));
    }

    [Fact]
    public async Task Reconcile_then_RunQueued_runs_the_scan_queued_before_the_stop()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        var queued = await _runs.Create(FolderScanStatus.Queued, _owner.User, _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        //Act
        await _factory.Resolve<FolderScanReconciler>().ReconcileAsync(cancellationToken);
        await _factory.RunQueuedScansAsync(cancellationToken);

        //Assert
        // The owner asked for it, and it walks the folders as they are when it starts.
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FolderScanRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((queued.Id, FolderScanStatus.Succeeded, 1, 1), (run.Id, run.Status, run.FilesFound, run.FilesUnreported));
            Assert.Equal(InterstellarPath, (await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken)).ServicePath);
        });
    }

    [Fact]
    public async Task Reconcile_with_nothing_under_way_queues_no_scan_of_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        var finished = await _runs.Create(FolderScanStatus.Succeeded, _owner.User, _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1), filesFound: 1, filesUnreported: 1);

        //Act
        await _factory.Resolve<FolderScanReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // A folder allowed and files on the disk: still nothing, as the owner chose that a scan runs only when
        // asked (ADR 0021), never at the app's start.
        Assert.Empty(DrainQueue());
        Assert.Empty(_factory.Files.ListedRoots);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FolderScanRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((finished.Id, FolderScanStatus.Succeeded), (run.Id, run.Status));
        });
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanReconciler).FullName);
    }

    [Fact]
    public async Task Reconcile_with_the_queue_full_fails_the_queued_scan_with_the_queue_full_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var queued = await _runs.Create(FolderScanStatus.Queued, _owner.User, _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1));
        var queue = _factory.Resolve<FolderScanQueue>();
        for (var index = 0; index < FolderScanQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.Resolve<FolderScanReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        // Left queued with no id, every later Scan folders would join it and nothing would run until the next
        // start: failed instead, with the sentence a refused request gets.
        Assert.DoesNotContain(queued.Id, DrainQueue());
        var run = await RunRowAsync(queued.Id, cancellationToken);
        Assert.Equal((FolderScanStatus.Failed, QueueFullError, now), (run.Status, TestTexts.En(run.Error), run.FinishedUtc));
    }

    // The owner's request over HTTP; the queued (or joined) scan's id.
    private async Task<Guid> StartAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderScanRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Id;
    }

    private async Task<FolderScanRunEntity> RunRowAsync(Guid runId, CancellationToken cancellationToken)
    {
        FolderScanRunEntity? run = null;
        await _factory.WithDbAsync(async db => run = await db.FolderScanRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken));
        return run!;
    }

    // Every stored file name, by id and path.
    private async Task<List<(Guid Id, string ServicePath)>> NamesAsync(CancellationToken cancellationToken)
    {
        var names = new List<(Guid Id, string ServicePath)>();
        await _factory.WithDbAsync(async db => names = (await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken))
            .Select(file => (file.Id, file.ServicePath))
            .ToList());
        return names;
    }

    // Every scan id waiting for the worker, in queue order; the queue is empty afterwards.
    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<FolderScanQueue>();
        var runIds = new List<Guid>();
        while (queue.Reader.TryRead(out var runId))
            runIds.Add(runId);
        return runIds;
    }
}
