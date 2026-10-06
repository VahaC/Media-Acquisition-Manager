using System.Collections.Concurrent;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — identifies the links every flow queued (Set, Replace, Retry, "Accept all confident" and the repair) in their
/// libraries in the background (Queue/Worker), each with its own audit row and its own result on the link, which the page
/// polls: the only caller of the library's Identify, so no request ever waits for it (a series takes minutes).
/// </summary>
/// <remarks>
/// <para>
/// R13 — the rows are the queue: woken by <see cref="LibraryLinkWriteQueue"/>, each lane claims the oldest <c>Queued</c> link
/// of its kind until none is left — the owner's own clicks (<c>BatchId</c> null) before the links of a bulk click, so a
/// Save in the dialog never sits behind the repair's two hundred.
/// </para>
/// <para>
/// Bounded parallelism: <see cref="FilmLaneCount"/> lanes for films and episode-movies and <see cref="SeriesLaneCount"/> for series, so
/// at most three Applies are in flight, and one series at a time. The lanes are long-lived and each woken by the signal on
/// its own — never one pass that waits for all of them, which would keep a film the owner saved while a series is inside a
/// 159-second Apply waiting until the series ends. A circuit breaker stops a batch whose Applies keep not confirming.
/// </para>
/// <para>
/// One scope and one try/catch per link — the next link's read and claim inside it too —, the other workers' rules: a bug
/// that throws out of one identification leaves that link not written — logged — and the next still goes; a database that
/// fails before a link is claimed ends that lane's run — logged — and leaves the links queued, never stopping the app.
/// </para>
/// </remarks>
public sealed class LibraryLinkWriteWorker(
    LibraryLinkWriteQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LibraryLinkWriteWorker> logger) : BackgroundService
{
    /// <summary>
    /// Lanes for films and episode-movies. They take two to twenty-one seconds each, mostly the provider and the image
    /// downloads, so two at once make the repair's ~210 of them take minutes instead of the better part of an hour. Two, not
    /// three: the live check ran three single Applies one after another and never several at once, and each Apply deletes the
    /// item's images before it fetches new ones, so a faster guess is not worth a systemic failure; the circuit breaker bounds
    /// the damage of one at two items. Raising it is a one-line change after a clean first batch.
    /// </summary>
    internal const int FilmLaneCount = 2;

    /// <summary>
    /// Lanes for series: one at a time. A series' Apply rewrites every episode (about a second each) and rescans its folder,
    /// so two seasons of one library fighting over the library's database and TMDb's rate would only slow both.
    /// </summary>
    internal const int SeriesLaneCount = 1;

    /// <summary>
    /// How many <c>NotConfirmed</c> results in a row stop a batch. One is a bad item; two in a row are a systemic failure
    /// (the library answering 5xx, its database locked, TMDb rate-limiting, a metadata fetcher that keeps overriding the
    /// name), and with no stop the worker would keep starting destructive Applies for the rest of the batch.
    /// </summary>
    internal const int BatchStopAfterNotConfirmed = 2;

    /// <summary>What a link a stopped batch never sent gets as its error.</summary>
    internal const string BatchStoppedError =
        "The app stopped this batch after Jellyfin failed to confirm two identifications in a row, and sent nothing for this item. Check Jellyfin, then Retry, or press Update Jellyfin again.";

    private readonly object _gate = new();

    // The dispatcher's lanes, FilmLaneCount films' then SeriesLaneCount series'. Guarded by _gate.
    private readonly Lane[] _lanes =
    [
        .. Enumerable.Range(0, FilmLaneCount).Select(_ => new Lane(LaneKind.Film)),
        .. Enumerable.Range(0, SeriesLaneCount).Select(_ => new Lane(LaneKind.Series)),
    ];

    // Per batch, how many NotConfirmed results came in a row; guarded by _gate. A link with no batch never counts.
    private readonly Dictionary<Guid, int> _notConfirmedInARow = [];

    // The batches this worker stopped; guarded by _gate. A batch's id is never queued again — the next click makes a new one,
    // and a Retry clears the link's batch — so the identifications of the batch that were already inside a call when it was
    // stopped finish unwatched, instead of counting up to a second stop (a second Warning for a batch already stopped).
    private readonly HashSet<Guid> _stoppedBatches = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                // The signals waiting are all answered by this wake-up.
                while (queue.Reader.TryRead(out _))
                {
                }

                Wake(stoppingToken);
            }
        }
        finally
        {
            // A shutdown stops the lanes between two links, never inside one; wait for the ones inside a claimed link.
            Task[] running;
            lock (_gate)
                running = [.. _lanes.Select(lane => lane.Task).OfType<Task>()];
            await Task.WhenAll(running);
        }
    }

    /// <summary>
    /// Identifies every link queued right now and returns once none is left: every lane drains its kind in parallel, as the
    /// dispatcher's lanes do. Tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    /// <remarks>
    /// A shutdown stops it between two links, never inside one: a claimed link's identification runs to its end
    /// (<see cref="CancellationToken.None"/>, ADR 0013), and the links still queued wait for the next start.
    /// </remarks>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        while (queue.Reader.TryRead(out _))
        {
        }

        var settled = new ConcurrentDictionary<LibraryLinkWriteStatus, int>();
        // Task.Run, as the dispatcher starts its lanes: a lane's work can run on the calling thread until its first real
        // wait (SQLite answers synchronously), which would make the next lane wait for it — lanes in name only.
        await Task.WhenAll(
            _lanes.Select(lane => Task.Run(() => DrainAsync(lane.Kind, settled, stoppingToken), CancellationToken.None)));
        LogSettled(settled);
    }

    // ── lanes ────────────────────────────────────────────────────────────────────────────────────

    // Starts every lane that isn't running; one that is, is told another signal came in (Dirty), so a link queued after its
    // last look is not left waiting for the next signal.
    private void Wake(CancellationToken stoppingToken)
    {
        lock (_gate)
        {
            foreach (var lane in _lanes)
            {
                if (lane.Running)
                {
                    lane.Dirty = true;
                    continue;
                }

                lane.Running = true;
                lane.Dirty = false;
                // Task.Run: the lane's first awaits must not run inside this lock.
                lane.Task = Task.Run(() => RunLaneAsync(lane, stoppingToken), CancellationToken.None);
            }
        }
    }

    private async Task RunLaneAsync(Lane lane, CancellationToken stoppingToken)
    {
        var settled = new ConcurrentDictionary<LibraryLinkWriteStatus, int>();
        var wentIdle = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (await TakeNextAsync(lane.Kind, settled, stoppingToken) == Taken.One)
                    continue;

                // Nothing found — or the database failed, which ends the run: either way this lane goes idle, unless a
                // signal came in since its query ran (Dirty): then it looks once more.
                lock (_gate)
                {
                    if (lane.Dirty)
                    {
                        lane.Dirty = false;
                        continue;
                    }

                    lane.Running = false;
                    wentIdle = true;
                }

                break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A shutdown: the links still queued wait for the next start.
        }
        finally
        {
            // Reset in a finally, so a lane that died on an unexpected exception — or stopped for the shutdown — is restarted
            // by the next signal. Not after the lane went idle on its own: from that moment a signal may have started the next
            // run, and clearing the flags here would mark that run "not running" — the next signal would then start a second
            // run of the same lane (two series at once) and a signal it was sent would be forgotten.
            if (!wentIdle)
            {
                lock (_gate)
                {
                    lane.Running = false;
                    lane.Dirty = false;
                }
            }

            LogSettled(settled);
        }
    }

    private async Task DrainAsync(LaneKind kind, ConcurrentDictionary<LibraryLinkWriteStatus, int> settled, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested && await TakeNextAsync(kind, settled, stoppingToken) == Taken.One)
            {
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A shutdown that cut the look for the next link: the links still queued wait for the next start, as in a lane.
        }
    }

    private enum Taken
    {
        /// <summary>A link was claimed and identified (or taken over meanwhile): go on.</summary>
        One,

        /// <summary>No link of this kind is queued.</summary>
        None,

        /// <summary>The next link couldn't be read or claimed; logged.</summary>
        Failed,
    }

    /// <summary>
    /// The lane's next link: the oldest <c>Queued</c> link of its kind — the owner's own clicks first —, claimed and
    /// identified in a scope of its own.
    /// </summary>
    private async Task<Taken> TakeNextAsync(LaneKind kind, ConcurrentDictionary<LibraryLinkWriteStatus, int> settled, CancellationToken stoppingToken)
    {
        Guid? claimedId = null;
        var attemptId = Guid.NewGuid();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var queued = db.LibraryItemLinks.AsNoTracking().Where(link => link.WriteState == LibraryLinkWriteState.Queued);
            queued = kind == LaneKind.Series
                ? queued.Where(link => link.ItemKind == MediaWorkKind.Series)
                : queued.Where(link => link.ItemKind != MediaWorkKind.Series);
            var next = await queued
                // The owner waiting at the dialog first: a bulk click queues hundreds of links at one instant and takes a
                // quarter of an hour, and his own Save or Retry must not sit behind them. False sorts first.
                .OrderBy(link => link.BatchId != null)
                .ThenBy(link => link.WriteStateUtc)
                .ThenBy(link => link.Id)
                .Select(link => new { link.Id, link.BatchId, link.SetByUserId, link.SetByUserName })
                .FirstOrDefaultAsync(stoppingToken);
            if (next is null)
                return Taken.None;

            // A link of a batch this worker just stopped, found before the update that settles the batch's links has landed
            // (it waits for SQLite's write lock like any other): the lane that finds it settles it itself, so the stop doesn't
            // depend on how fast that update lands — a lane would otherwise go on starting the batch's Applies meanwhile.
            if (next.BatchId is { } stoppedBatchId && IsStopped(stoppedBatchId))
            {
                await db.LibraryItemLinks
                    .Where(link => link.Id == next.Id && link.WriteState == LibraryLinkWriteState.Queued)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(link => link.WriteState, LibraryLinkWriteState.NotWritten)
                            .SetProperty(link => link.WriteError, BatchStoppedError)
                            .SetProperty(link => link.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                        CancellationToken.None);
                return Taken.One;
            }

            // R9 — the claim: only the worker that moves the link out of Queued identifies it; a link the owner replaced or
            // removed meanwhile is no longer Queued and is passed over. This is also what keeps two lanes — or two passes —
            // from ever sending one item two Applies. Never cancelled: a shutdown that cut the update after it was committed
            // would leave a link Writing that no one identifies — the restart would then mark it "cut while identifying" —, and
            // a lane that wins the claim goes on to identify the link, as a lane stops between links, never inside one.
            var claimed = await db.LibraryItemLinks
                .Where(link => link.Id == next.Id && link.WriteState == LibraryLinkWriteState.Queued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(link => link.WriteState, LibraryLinkWriteState.Writing)
                        .SetProperty(link => link.WriteAttemptId, attemptId)
                        .SetProperty(link => link.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                    CancellationToken.None) == 1;
            if (!claimed)
                return Taken.One;
            claimedId = next.Id;

            // R13 — the audit row of an identification names whoever queued the link.
            var actor = next.SetByUserId is { } userId ? new AuditActor(userId, next.SetByUserName) : null;
            var result = await scope.ServiceProvider.GetRequiredService<ILibraryLinkWriter>().WriteAsync(next.Id, attemptId, actor, CancellationToken.None);
            Count(settled, result.Status);
            if (next.BatchId is { } batchId)
                await WatchBatchAsync(batchId, result);
            return Taken.One;
        }
        // A shutdown is not an error: the links still queued wait for the next start. Anything else — a cancellation too,
        // when it isn't the shutdown's — is a bug or a database that failed, never a reason to stop the app.
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            if (claimedId is not { } linkId)
            {
                // The next link couldn't be read or claimed (a lock held past its timeout by a long sync): the links stay
                // queued for the next signal, or the reconciler's at the next start.
                logger.LogError(exception, "The background writer couldn't take the next queued TMDb link.");
                return Taken.Failed;
            }

            logger.LogError(exception, "TMDb link {LinkId} threw out of the background writer.", linkId);
            await MarkNotWrittenAsync(linkId, attemptId);
            Count(settled, LibraryLinkWriteStatus.NotWritten);
            return Taken.One;
        }
    }

    // ── the circuit breaker ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Counts <c>NotConfirmed</c> results in a row per batch: any other result resets the batch's count, and
    /// <see cref="BatchStopAfterNotConfirmed"/> in a row stop the batch.
    /// </summary>
    private async Task WatchBatchAsync(Guid batchId, LibraryLinkWriteResult result)
    {
        bool stop;
        lock (_gate)
        {
            if (_stoppedBatches.Contains(batchId))
                return;
            if (result.Outcome != LibraryIdentifyOutcome.NotConfirmed)
            {
                _notConfirmedInARow.Remove(batchId);
                return;
            }

            var inARow = _notConfirmedInARow.GetValueOrDefault(batchId) + 1;
            stop = inARow >= BatchStopAfterNotConfirmed;
            if (stop)
            {
                _notConfirmedInARow.Remove(batchId);
                _stoppedBatches.Add(batchId);
            }
            else
            {
                _notConfirmedInARow[batchId] = inARow;
            }
        }

        if (stop)
            await StopBatchAsync(batchId);
    }

    private bool IsStopped(Guid batchId)
    {
        lock (_gate)
            return _stoppedBatches.Contains(batchId);
    }

    /// <summary>
    /// Settles the batch's links still <c>Queued</c> as <c>NotWritten</c> with <see cref="BatchStoppedError"/>: nothing was
    /// sent for them — no audit row, their identified time kept — so the next repair click takes them again. The links of the
    /// batch that are <c>Writing</c> in another lane finish normally.
    /// </summary>
    private async Task StopBatchAsync(Guid batchId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var stopped = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibraryItemLinks
                .Where(link => link.BatchId == batchId && link.WriteState == LibraryLinkWriteState.Queued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(link => link.WriteState, LibraryLinkWriteState.NotWritten)
                        .SetProperty(link => link.WriteError, BatchStoppedError)
                        .SetProperty(link => link.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                    CancellationToken.None);
            // Warning, so it ships off the box: the owner's library keeps failing. Counts only.
            logger.LogWarning(
                "The background writer stopped batch {BatchId} after {Failures} identifications in a row that Jellyfin didn't confirm: {Stopped} queued links were left unsent.",
                batchId,
                BatchStopAfterNotConfirmed,
                stopped);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The links stay queued and the batch goes on; the next failures count again, and can stop it.
            lock (_gate)
                _stoppedBatches.Remove(batchId);
            logger.LogError(exception, "The background writer couldn't stop batch {BatchId}.", batchId);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static void Count(ConcurrentDictionary<LibraryLinkWriteStatus, int> settled, LibraryLinkWriteStatus status) =>
        settled.AddOrUpdate(status, 1, (_, count) => count + 1);

    // Information: counts only — where each link's identification left it, so a pass that did nothing doesn't read as done.
    private void LogSettled(ConcurrentDictionary<LibraryLinkWriteStatus, int> settled)
    {
        var total = settled.Values.Sum();
        if (total == 0)
            return;
        logger.LogInformation(
            "The background writer settled {Total} queued TMDb links: {Written} written, {NotWritten} not written, {Other} kept in the app or taken over meanwhile.",
            total,
            settled.GetValueOrDefault(LibraryLinkWriteStatus.Written),
            settled.GetValueOrDefault(LibraryLinkWriteStatus.NotWritten),
            settled.GetValueOrDefault(LibraryLinkWriteStatus.KeptInApp) + settled.GetValueOrDefault(LibraryLinkWriteStatus.ClaimLost));
    }

    // Best effort, in a fresh scope: the link's own context may be what broke. If even this fails, the reconciler turns
    // the link from Writing into NotWritten at the next start.
    private async Task MarkNotWrittenAsync(Guid linkId, Guid attemptId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibraryItemLinks
                .Where(link => link.Id == linkId && link.WriteState == LibraryLinkWriteState.Writing && link.WriteAttemptId == attemptId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(link => link.WriteState, LibraryLinkWriteState.NotWritten)
                        .SetProperty(link => link.WriteError, FileInspectionRunUpdates.Cut(LibraryLinkWriter.AppErrorMessage, LibraryItemLinkEntity.ErrorMaximumLength))
                        .SetProperty(link => link.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "TMDb link {LinkId} could not be marked not written.", linkId);
        }
    }

    /// <summary>What a lane identifies.</summary>
    private enum LaneKind
    {
        /// <summary>Films and episode-movies (every item that isn't a series).</summary>
        Film,

        /// <summary>Series.</summary>
        Series,
    }

    /// <summary>One long-lived lane of the dispatcher; its flags are guarded by the worker's lock.</summary>
    private sealed class Lane(LaneKind kind)
    {
        public LaneKind Kind { get; } = kind;

        /// <summary>Whether a run of this lane is under way.</summary>
        public bool Running { get; set; }

        /// <summary>Whether a signal came in while the lane was running: it looks once more before going idle.</summary>
        public bool Dirty { get; set; }

        public Task? Task { get; set; }
    }
}
