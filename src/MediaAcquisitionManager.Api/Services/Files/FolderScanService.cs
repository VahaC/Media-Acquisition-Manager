using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IFolderScanService"/>
public sealed class FolderScanService(
    ApplicationDbContext db,
    FolderScanQueue queue,
    ILibraryMapper mapper,
    TimeProvider time,
    ILogger<FolderScanService> logger) : IFolderScanService
{
    /// <summary>The sentence the owner's Scan folders gets with no folder allowed; the page offers the way
    /// to the settings next to it.</summary>
    internal const string NoAllowedRootsError =
        "No media folders are allowed yet. Add the folders the app may read under Media folders in the user menu, then scan again.";

    /// <summary>The sentence of a run the queue refused; the reconciler fails one with it too.</summary>
    internal const string QueueFullError = "Too many folder scans are waiting. Try again in a minute.";

    public async Task<FolderScanStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default)
    {
        // MAM-18 — nothing to walk without a root, and the default is none: the owner is told how to allow one.
        if (!await db.AllowedRoots.AnyAsync(cancellationToken))
            return FolderScanStartResult.NoAllowedRoots(NoAllowedRootsError);
        // Joining the waiting scan rather than queuing a second: it walks every folder there is when it
        // starts, so a second would only walk them all again.
        if (await QueuedRunAsync(cancellationToken) is { } waiting)
            return FolderScanStartResult.Queued(mapper.ToDto(waiting, forOwner: true));

        var requestedByUserName = await db.Users
            .Where(user => user.Id == requestedByUserId)
            .Select(user => user.UserName)
            .SingleAsync(cancellationToken);
        var run = new FolderScanRunEntity
        {
            RequestedByUserId = requestedByUserId,
            // Copied (the audit-row pattern): the run still says who asked after the user is deleted.
            RequestedByUserName = requestedByUserName,
            Status = FolderScanStatus.Queued,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.FolderScanRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // MAM-18 — two clicks can both find nothing queued; the partial unique index
            // (IX_FolderScanRuns_OneRunPerActiveStatus) keeps one queued scan, and this request joins it.
            // Anything else (a full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (await QueuedRunAsync(cancellationToken) is { } winner)
            {
                logger.LogInformation(exception, "Another request queued folder scan {RunId} first; this one joined it.", winner.Id);
                return FolderScanStartResult.Queued(mapper.ToDto(winner, forOwner: true));
            }

            throw;
        }

        if (!queue.TryEnqueue(run.Id))
        {
            // The run stays as history of the attempt, failed with the same sentence the owner gets.
            await db.FolderScanRuns
                .Where(queued => queued.Id == run.Id && queued.Status == FolderScanStatus.Queued)
                .MarkFailedAsync(QueueFullError, time.GetUtcNow().UtcDateTime);
            return FolderScanStartResult.Unavailable(QueueFullError);
        }

        logger.LogInformation("Folder scan {RunId} queued by user {UserId}.", run.Id, requestedByUserId);
        // Only the owner may start one, so the owner's view of it.
        return FolderScanStartResult.Queued(mapper.ToDto(run, forOwner: true));
    }

    public async Task<FolderScanRunDto?> GetRunAsync(Guid runId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var run = await db.FolderScanRuns.AsNoTracking().SingleOrDefaultAsync(row => row.Id == runId, cancellationToken);
        return run is null ? null : mapper.ToDto(run, forOwner: await db.IsInstallationOwnerAsync(callerUserId, cancellationToken));
    }

    public async Task<FolderScanOverviewDto> GetOverviewAsync(bool forOwner, CancellationToken cancellationToken = default)
    {
        // Running before queued: the page shows what is happening now, and a queued scan behind a running
        // one only says "and again after this" (HasQueuedRun).
        var active = await db.FolderScanRuns
            .AsNoTracking()
            .Where(run => run.Status == FolderScanStatus.Running || run.Status == FolderScanStatus.Queued)
            .OrderBy(run => run.Status == FolderScanStatus.Running ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);
        var lastFinished = await db.FolderScanRuns
            .AsNoTracking()
            .Where(run => run.Status == FolderScanStatus.Succeeded || run.Status == FolderScanStatus.Failed)
            .OrderByDescending(run => run.CreatedUtc)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var hasQueuedRun = await db.FolderScanRuns.AnyAsync(run => run.Status == FolderScanStatus.Queued, cancellationToken);
        // MAM-18, ADR 0021 — the names no library name leads to now, in path order, so "the first problem" is the same
        // one on every poll. MAM-42 — the ignored ones leave the counts but not the room: they still take it. MAM-44 —
        // and so do the extras.
        var unreported = await UnreportedFileReader.LoadSetAsync(db, cancellationToken);
        var counted = unreported.Counted;
        return new FolderScanOverviewDto(
            active is null ? null : mapper.ToDto(active, forOwner),
            lastFinished is null ? null : mapper.ToDto(lastFinished, forOwner),
            hasQueuedRun,
            FileInspectionCounts.Of(
                [.. counted.Select(name => (name.InspectionStatus, name.InspectionError, UnreportedFileReader.HasAppPathOf(name)))],
                forOwner),
            // MAM-19 — the room the files behind those same names take, by the same rule.
            FileSpace.Of(unreported.All.Select(name => (name.DeviceId, name.Inode, name.ReadSizeBytes))),
            unreported.Ignored.Count,
            unreported.Extras.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<FolderScanRunEntity?> QueuedRunAsync(CancellationToken cancellationToken) =>
        db.FolderScanRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Status == FolderScanStatus.Queued, cancellationToken);
}
