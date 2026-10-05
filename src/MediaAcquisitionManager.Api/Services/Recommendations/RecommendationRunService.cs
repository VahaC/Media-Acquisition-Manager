using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationRunService"/>
/// <remarks>The <c>FileInspectionService</c> shape, with a connected metadata catalog where the inspection needs an
/// allowed folder.</remarks>
public sealed class RecommendationRunService(
    ApplicationDbContext db,
    RecommendationRunQueue queue,
    ICatalogConnector catalogs,
    IRecommendationMapper mapper,
    TimeProvider time,
    ILogger<RecommendationRunService> logger) : IRecommendationRunService
{
    /// <summary>The sentence of a run the queue refused; the reconciler fails one with it too.</summary>
    internal const string QueueFullError = "Too many recommendation runs are waiting. Try again in a minute.";

    public Task<RecommendationRunStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default) =>
        QueueAsync(RecommendationRunTrigger.Requested, requestedByUserId, cancellationToken);

    public Task<RecommendationRunStartResult> EnsureQueuedAsync(
        RecommendationRunTrigger trigger, Guid? requestedByUserId = null, CancellationToken cancellationToken = default) =>
        QueueAsync(trigger, requestedByUserId, cancellationToken);

    public async Task<RecommendationRunDto?> GetLatestAsync(Guid callerUserId, CancellationToken cancellationToken = default)
    {
        // The running one first — the page shows what is happening now, and a run queued behind it only says "and
        // again after this" — else the newest by when it was queued, a queued one included; then by id, so two
        // queued in one tick still give one answer.
        var run = await db.RecommendationRuns
            .AsNoTracking()
            .OrderBy(row => row.Status == RecommendationRunStatus.Running ? 0 : 1)
            .ThenByDescending(row => row.CreatedUtc)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return run is null ? null : mapper.ToDto(run, forOwner: await db.IsInstallationOwnerAsync(callerUserId, cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<RecommendationRunStartResult> QueueAsync(
        RecommendationRunTrigger trigger, Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        // MAM-33 — nothing to compute from without a catalog, and the default is none: a sync queues nothing then,
        // and the owner's request is told where to connect one.
        if (!await catalogs.HasCatalogAsync(cancellationToken))
            return RecommendationRunStartResult.NoCatalog(CatalogConnector.NoCatalogError);
        // Joining the waiting run rather than queuing a second: it reads the library, the catalog and every user's
        // tabs as they are when it starts, so a second would only compute the same again.
        if (await QueuedRunAsync(cancellationToken) is { } waiting)
            return RecommendationRunStartResult.Queued(mapper.ToDto(waiting, forOwner: true));

        var requestedByUserName = requestedByUserId is { } userId
            ? await db.Users.Where(user => user.Id == userId).Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var run = new RecommendationRunEntity
        {
            Trigger = trigger,
            RequestedByUserId = requestedByUserName is null ? null : requestedByUserId,
            // Copied (the audit-row pattern): the run still says who asked after the user is deleted.
            RequestedByUserName = requestedByUserName,
            Status = RecommendationRunStatus.Queued,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.RecommendationRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two triggers can both find nothing queued (a sync finishing as the owner clicks); the partial unique
            // index (IX_RecommendationRuns_OneRunPerActiveStatus) keeps one queued run, and this one joins it.
            // Anything else (a full disk) stays a failure.
            db.Entry(run).State = EntityState.Detached;
            if (await QueuedRunAsync(cancellationToken) is { } winner)
            {
                logger.LogInformation(exception, "Another trigger queued recommendation run {RunId} first; this {Trigger} joined it.", winner.Id, trigger);
                return RecommendationRunStartResult.Queued(mapper.ToDto(winner, forOwner: true));
            }

            throw;
        }
        finally
        {
            // Leaves nothing of this run tracked on a context the caller may go on saving with (an integration's save).
            if (db.Entry(run).State != EntityState.Detached)
                db.Entry(run).State = EntityState.Detached;
        }

        if (!queue.TryEnqueue(run.Id))
        {
            // The run stays as history of the attempt, failed with the same sentence the owner gets.
            await db.RecommendationRuns
                .Where(queued => queued.Id == run.Id && queued.Status == RecommendationRunStatus.Queued)
                .MarkFailedAsync(QueueFullError, time.GetUtcNow().UtcDateTime);
            return RecommendationRunStartResult.Unavailable(QueueFullError);
        }

        logger.LogInformation("Recommendation run {RunId} queued ({Trigger}).", run.Id, trigger);
        return RecommendationRunStartResult.Queued(mapper.ToDto(run, forOwner: true));
    }

    private Task<RecommendationRunEntity?> QueuedRunAsync(CancellationToken cancellationToken) =>
        db.RecommendationRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Status == RecommendationRunStatus.Queued, cancellationToken);
}
