using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IFileInspectionService"/>
public sealed class FileInspectionService(
    ApplicationDbContext db,
    FileInspectionQueue queue,
    ILibraryMapper mapper,
    TimeProvider time,
    ILogger<FileInspectionService> logger) : IFileInspectionService
{
    /// <summary>What the owner's Inspect files gets with no folder allowed; the page offers the way to the settings
    /// next to it.</summary>
    internal static readonly Message NoAllowedRootsError = Message.Of("files.inspection.no-allowed-roots");

    /// <summary>Why the queue refused a run; the reconciler fails one with it too.</summary>
    internal static readonly Message QueueFullError = Message.Of("files.inspection.queue-full");

    public async Task<FileInspectionStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default)
    {
        var requestedByUserName = await db.Users
            .Where(user => user.Id == requestedByUserId)
            .Select(user => user.UserName)
            .SingleAsync(cancellationToken);
        return await QueueAsync(FileInspectionTrigger.Requested, requestedByUserId, requestedByUserName, cancellationToken);
    }

    public Task<FileInspectionStartResult> EnsureQueuedAsync(FileInspectionTrigger trigger, CancellationToken cancellationToken = default) =>
        QueueAsync(trigger, requestedByUserId: null, requestedByUserName: null, cancellationToken);

    public async Task<FileInspectionRunDto?> GetRunAsync(Guid runId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var run = await db.FileInspectionRuns.AsNoTracking().SingleOrDefaultAsync(row => row.Id == runId, cancellationToken);
        return run is null ? null : mapper.ToDto(run, forOwner: await db.IsInstallationOwnerAsync(callerUserId, cancellationToken));
    }

    public async Task<FileInspectionOverviewDto> GetOverviewAsync(bool forOwner, CancellationToken cancellationToken = default)
    {
        var hasAllowedRoots = await db.AllowedRoots.AnyAsync(cancellationToken);
        // Running before queued: the page shows what is happening now, and a queued run behind a running
        // one only says "and again after this" (HasQueuedRun).
        var active = await db.FileInspectionRuns
            .AsNoTracking()
            .Where(run => run.Status == FileInspectionRunStatus.Running || run.Status == FileInspectionRunStatus.Queued)
            .OrderBy(run => run.Status == FileInspectionRunStatus.Running ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);
        var lastFinished = await db.FileInspectionRuns
            .AsNoTracking()
            .Where(run => run.Status == FileInspectionRunStatus.Succeeded || run.Status == FileInspectionRunStatus.Failed)
            .OrderByDescending(run => run.CreatedUtc)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var hasQueuedRun = await db.FileInspectionRuns.AnyAsync(run => run.Status == FileInspectionRunStatus.Queued, cancellationToken);
        // MAM-19 — every stored name, as a run looks at every one (ADR 0021): a file a library and the scan,
        // or two libraries, both name counts once (ADR 0018).
        var names = await db.PhysicalFiles
            .AsNoTracking()
            .Select(file => new { file.DeviceId, file.Inode, file.ReadSizeBytes })
            .ToListAsync(cancellationToken);
        return new FileInspectionOverviewDto(
            hasAllowedRoots,
            active is null ? null : mapper.ToDto(active, forOwner),
            lastFinished is null ? null : mapper.ToDto(lastFinished, forOwner),
            hasQueuedRun,
            FileSpace.Of(names.Select(name => (name.DeviceId, name.Inode, name.ReadSizeBytes))));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<FileInspectionStartResult> QueueAsync(
        FileInspectionTrigger trigger, Guid? requestedByUserId, string? requestedByUserName, CancellationToken cancellationToken)
    {
        // MAM-9 — nothing to read without a root, and the default is none: a sync, a folder scan or the app's start
        // queues nothing then, and the owner's request is told how to allow one.
        if (!await db.AllowedRoots.AnyAsync(cancellationToken))
            return FileInspectionStartResult.NoAllowedRoots(NoAllowedRootsError);
        // Joining the waiting run rather than queuing a second: it reads every file there is when it
        // starts, so a second would only read them all again.
        if (await QueuedRunAsync(cancellationToken) is { } waiting)
            return FileInspectionStartResult.Queued(mapper.ToDto(waiting, forOwner: true));

        var run = new FileInspectionRunEntity
        {
            Trigger = trigger,
            RequestedByUserId = requestedByUserId,
            // Copied (the audit-row pattern): the run still says who asked after the user is deleted.
            RequestedByUserName = requestedByUserName,
            Status = FileInspectionRunStatus.Queued,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.FileInspectionRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // MAM-9 — two triggers can both find nothing queued (a sync finishing as the owner clicks);
            // the partial unique index (IX_FileInspectionRuns_OneRunPerActiveStatus) keeps one queued run,
            // and this request joins it. Anything else (a full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (await QueuedRunAsync(cancellationToken) is { } winner)
            {
                logger.LogInformation(exception, "Another trigger queued file inspection {RunId} first; this {Trigger} joined it.", winner.Id, trigger);
                return FileInspectionStartResult.Queued(mapper.ToDto(winner, forOwner: true));
            }

            throw;
        }

        if (!queue.TryEnqueue(run.Id))
        {
            // The run stays as history of the attempt, failed with the same sentence the owner gets.
            await db.FileInspectionRuns
                .Where(queued => queued.Id == run.Id && queued.Status == FileInspectionRunStatus.Queued)
                .MarkFailedAsync(QueueFullError, time.GetUtcNow().UtcDateTime);
            return FileInspectionStartResult.Unavailable(QueueFullError);
        }

        logger.LogInformation("File inspection {RunId} queued ({Trigger}).", run.Id, trigger);
        return FileInspectionStartResult.Queued(mapper.ToDto(run, forOwner: true));
    }

    private Task<FileInspectionRunEntity?> QueuedRunAsync(CancellationToken cancellationToken) =>
        db.FileInspectionRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Status == FileInspectionRunStatus.Queued, cancellationToken);
}
