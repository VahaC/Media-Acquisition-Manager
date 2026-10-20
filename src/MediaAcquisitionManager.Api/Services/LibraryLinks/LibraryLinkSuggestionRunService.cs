using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkSuggestionRunService"/>
/// <remarks>The <c>RecommendationRunService</c> shape: a connected metadata catalog, or nothing.</remarks>
public sealed class LibraryLinkSuggestionRunService(
    ApplicationDbContext db,
    LibraryLinkSuggestionRunQueue queue,
    ICatalogConnector catalogs,
    ILibraryLinkSuggestionMapper mapper,
    TimeProvider time,
    ILogger<LibraryLinkSuggestionRunService> logger) : ILibraryLinkSuggestionRunService
{
    /// <summary>Why a run the queue refused wasn't queued; the reconciler fails one with it too.</summary>
    internal static readonly Message QueueFullError = Message.Of("library-links.suggestions.queue-full");

    public Task<LibraryLinkSuggestionRunStartResult> StartAsync(Guid requestedByUserId, CancellationToken cancellationToken = default) =>
        QueueAsync(LibraryLinkSuggestionRunTrigger.Requested, requestedByUserId, cancellationToken);

    public Task<LibraryLinkSuggestionRunStartResult> EnsureQueuedAsync(LibraryLinkSuggestionRunTrigger trigger, CancellationToken cancellationToken = default) =>
        QueueAsync(trigger, requestedByUserId: null, cancellationToken);

    public async Task<LibraryLinkSuggestionRunDto?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        // The running one first — the page shows what is happening now, and a run queued behind it only says "and again
        // after this" — else the newest by when it was queued, a queued one included; then by id, so two queued in one
        // tick still give one answer.
        var run = await db.LibraryLinkSuggestionRuns
            .AsNoTracking()
            .OrderBy(row => row.Status == LibraryLinkSuggestionRunStatus.Running ? 0 : 1)
            .ThenByDescending(row => row.CreatedUtc)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return run is null ? null : mapper.ToRunDto(run);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<LibraryLinkSuggestionRunStartResult> QueueAsync(
        LibraryLinkSuggestionRunTrigger trigger, Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        // Nothing to search without a catalog, and the default is none: a sync queues nothing then, and the owner's
        // request is told where to connect one.
        if (!await catalogs.HasCatalogAsync(cancellationToken))
            return LibraryLinkSuggestionRunStartResult.NoCatalog(CatalogConnector.NoCatalogError);

        var requestedByUserName = requestedByUserId is { } userId
            ? await db.Users.Where(user => user.Id == userId).Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken)
            : null;

        // Joining the waiting run rather than queuing a second: it reads the library as it is when it starts, so a second
        // would only search the same again.
        if (await JoinAsync(trigger, requestedByUserId, requestedByUserName, cancellationToken) is { } joined)
            return LibraryLinkSuggestionRunStartResult.Queued(joined);

        var run = new LibraryLinkSuggestionRunEntity
        {
            Trigger = trigger,
            RequestedByUserId = requestedByUserName is null ? null : requestedByUserId,
            // Copied (the audit-row pattern): the run still says who asked after the user is deleted.
            RequestedByUserName = requestedByUserName,
            Status = LibraryLinkSuggestionRunStatus.Queued,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.LibraryLinkSuggestionRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two triggers can both find nothing queued (a sync finishing as the owner clicks); the partial unique index
            // (IX_LibraryLinkSuggestionRuns_OneRunPerActiveStatus) keeps one queued run, and this one joins it. Anything
            // else (a full disk) stays a failure.
            db.Entry(run).State = EntityState.Detached;
            if (await JoinAsync(trigger, requestedByUserId, requestedByUserName, cancellationToken) is { } winner)
            {
                logger.LogInformation(exception, "Another trigger queued TMDb suggestion run {RunId} first; this {Trigger} joined it.", winner.Id, trigger);
                return LibraryLinkSuggestionRunStartResult.Queued(winner);
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
            await db.LibraryLinkSuggestionRuns
                .Where(queued => queued.Id == run.Id && queued.Status == LibraryLinkSuggestionRunStatus.Queued)
                .MarkFailedAsync(QueueFullError, time.GetUtcNow().UtcDateTime);
            return LibraryLinkSuggestionRunStartResult.Unavailable(QueueFullError);
        }

        logger.LogInformation("TMDb suggestion run {RunId} queued ({Trigger}).", run.Id, trigger);
        return LibraryLinkSuggestionRunStartResult.Queued(mapper.ToRunDto(run));
    }

    /// <summary>
    /// The waiting run, joined; <c>null</c> when none waits. R21 — the owner's request searches every work again, so a
    /// waiting run a sync queued becomes the owner's when the owner joins it: otherwise the request would be lost in a run
    /// that keeps its cache.
    /// </summary>
    private async Task<LibraryLinkSuggestionRunDto?> JoinAsync(
        LibraryLinkSuggestionRunTrigger trigger, Guid? requestedByUserId, string? requestedByUserName, CancellationToken cancellationToken)
    {
        var waiting = await db.LibraryLinkSuggestionRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(run => run.Status == LibraryLinkSuggestionRunStatus.Queued, cancellationToken);
        if (waiting is null || trigger != LibraryLinkSuggestionRunTrigger.Requested || waiting.Trigger == LibraryLinkSuggestionRunTrigger.Requested)
            return waiting is null ? null : mapper.ToRunDto(waiting);

        // Conditional on Queued: a run the worker took meanwhile keeps its trigger, and this request queues one of its own
        // behind it.
        var taken = await db.LibraryLinkSuggestionRuns
            .Where(run => run.Id == waiting.Id && run.Status == LibraryLinkSuggestionRunStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Trigger, LibraryLinkSuggestionRunTrigger.Requested)
                    .SetProperty(run => run.RequestedByUserId, requestedByUserName is null ? null : requestedByUserId)
                    .SetProperty(run => run.RequestedByUserName, requestedByUserName),
                cancellationToken) == 1;
        if (!taken)
            return null;

        logger.LogInformation("The owner's request joined TMDb suggestion run {RunId}, which now searches every work again.", waiting.Id);
        var joined = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(run => run.Id == waiting.Id, cancellationToken);
        return mapper.ToRunDto(joined);
    }
}
