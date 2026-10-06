using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — removing links: the app forgets the link and nothing else (Vasyl's decision of 2026-10-06, ADR 0026). The
// library keeps what its Identify set — there is no undo —, so removing never calls it, needs no integration, and a switched
// off or deleted library no longer stands in the way.
public sealed partial class LibraryLinkService
{
    public async Task<LibraryLinkRemoveResult> RemoveAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        if (!await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken))
            return LibraryLinkRemoveResult.Not(LibraryLinkResult.NotFound);
        var links = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.MediaWorkId == workId)
            .OrderBy(link => link.SourceItemId)
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
            return LibraryLinkRemoveResult.Not(LibraryLinkResult.NothingToRemove);
        // Being identified: deleting the row under the worker would orphan its audit target and race its settle.
        if (links.Any(link => link.WriteState == LibraryLinkWriteState.Writing))
            return LibraryLinkRemoveResult.Not(LibraryLinkResult.Conflict(UnderWayError));

        var actor = await ActorAsync(ownerUserId, cancellationToken);
        var workTitle = await WorkTitleAsync(workId, cancellationToken);
        LibraryLinkResult? failure = null;
        var removed = 0;
        // Each link on its own: one the worker claimed between the read and the delete stays, and the others still go.
        foreach (var link in links)
        {
            if (await RemoveOneAsync(link, actor, workTitle) is { } refused)
                failure ??= refused;
            else
                removed++;
        }

        logger.LogInformation("Work {WorkId}: {Removed} TMDb links removed, {Kept} kept.", workId, removed, links.Count - removed);
        if (removed > 0)
            await QueueRecommendationsAsync(ownerUserId);
        if (failure is not null)
            return LibraryLinkRemoveResult.Not(failure);
        return await DialogAsync(workId, CancellationToken.None) is { } dialog
            ? LibraryLinkRemoveResult.Removed(dialog)
            : LibraryLinkRemoveResult.Not(LibraryLinkResult.NotFound);
    }

    public async Task<LibraryLinkResult> RemoveLinkAsync(Guid linkId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var link = await db.LibraryItemLinks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == linkId, cancellationToken);
        if (link is null)
            return LibraryLinkResult.NotFound;
        if (link.WriteState == LibraryLinkWriteState.Writing)
            return LibraryLinkResult.Conflict(UnderWayError);

        var actor = await ActorAsync(ownerUserId, cancellationToken);
        var workTitle = link.MediaWorkId is { } workId ? await WorkTitleAsync(workId, cancellationToken) : null;
        if (await RemoveOneAsync(link, actor, workTitle) is { } refused)
            return refused;

        logger.LogInformation("TMDb link {LinkId} removed.", link.Id);
        await QueueRecommendationsAsync(ownerUserId);
        return LibraryLinkResult.Succeeded(null);
    }

    /// <summary>
    /// One link forgotten: deleted — its file identities with it — in one transaction with its audit row. A link still
    /// <c>Queued</c> simply goes, and the worker's conditional claim finds nothing and passes it over. <c>null</c> when it
    /// went; the 409 otherwise — the worker claimed it between the read and the delete, so it is being identified and stays.
    /// Never cancelled once started: the delete and its audit row go together or not at all.
    /// </summary>
    private async Task<LibraryLinkResult?> RemoveOneAsync(LibraryItemLinkEntity link, AuditActor actor, string? workTitle)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var deleted = await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState != LibraryLinkWriteState.Writing)
            .ExecuteDeleteAsync(CancellationToken.None);
        if (deleted == 0)
            return LibraryLinkResult.Conflict(UnderWayError);

        List<MediaIdentifier> linked = [new(link.Scheme, link.Value)];
        if (link.ImdbId is { } imdbId)
            linked.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));
        auditTrail.Record(
            AuditAction.LibraryLinkRemoved,
            actor,
            AuditTarget.LibraryLink(link.Id, workTitle ?? link.ItemTitle ?? link.CatalogTitle ?? link.SourceItemId),
            LibraryLinkKeys.AuditIdentifiers(linked, []));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        return null;
    }
}
