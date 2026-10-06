using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — removing links: the library item put back first, the link deleted after (R11), so a link never goes while the
// library still holds what the app wrote for it, and a library that can't be changed back keeps its link — the owner can
// try again.
public sealed partial class LibraryLinkService
{
    /// <summary>R11 — the restore failed; the sentence says why, and the owner can remove it again.</summary>
    private static string NotRemovedError(string name, string error) => $"“{name}” couldn't be changed back, so the link stays: {error}";

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
        if (links.Any(link => link.WriteState == LibraryLinkWriteState.Writing))
            return LibraryLinkRemoveResult.Not(LibraryLinkResult.Conflict(UnderWayError));

        var actor = await ActorAsync(ownerUserId, cancellationToken);
        var workTitle = await WorkTitleAsync(workId, cancellationToken);
        LibraryLinkResult? failure = null;
        var removed = 0;
        // Each link on its own: one library that can't be changed back keeps its link, and the others still go.
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
    /// One link removed (R11): claimed, its item put back, then deleted in one transaction with its audit rows — the
    /// removal, and what was sent back when something was. <c>null</c> when it went; the result saying why it stays
    /// otherwise, its state given back. Never cancelled once claimed: the library may already have been changed.
    /// </summary>
    private async Task<LibraryLinkResult?> RemoveOneAsync(LibraryItemLinkEntity link, AuditActor actor, string? workTitle)
    {
        if (await ClaimAsync(link) is not { } attemptId)
            return LibraryLinkResult.Conflict(UnderWayError);

        var restore = await writer.RestoreAsync(link.Id, attemptId, CancellationToken.None);
        switch (restore.Status)
        {
            case LibraryLinkRestoreStatus.SwitchedOff:
                await GiveBackAsync(link, attemptId, link.WriteError);
                return LibraryLinkResult.Conflict(restore.Error!);
            case LibraryLinkRestoreStatus.Failed:
                await GiveBackAsync(link, attemptId, restore.Error);
                var name = await db.IntegrationConnections
                    .Where(integration => integration.Id == link.IntegrationConnectionId)
                    .Select(integration => integration.Name)
                    .SingleOrDefaultAsync(CancellationToken.None) ?? "";
                return LibraryLinkResult.BadGateway(NotRemovedError(name, restore.Error!));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var deleted = await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteDeleteAsync(CancellationToken.None);
        if (deleted == 0)
            return LibraryLinkResult.Conflict(UnderWayError);

        var target = AuditTarget.LibraryLink(link.Id, workTitle ?? link.ItemTitle ?? link.CatalogTitle ?? link.SourceItemId);
        List<MediaIdentifier> linked = [new(link.Scheme, link.Value)];
        if (link.ImdbId is { } imdbId)
            linked.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));
        auditTrail.Record(AuditAction.LibraryLinkRemoved, actor, target, LibraryLinkEdits.AuditIdentifiers(linked, []));
        if (restore is { Status: LibraryLinkRestoreStatus.Restored, Write: { } write })
            auditTrail.Record(AuditAction.LibraryIdsRestored, actor, target, LibraryLinkEdits.AuditIdentifiers(write.Before, write.After));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        return null;
    }

    /// <summary>The claimed link back as it was, with <paramref name="error"/> — a removal that couldn't go through.</summary>
    private Task GiveBackAsync(LibraryItemLinkEntity link, Guid attemptId, string? error)
    {
        var storedError = error is null ? null : Cut(error, LibraryItemLinkEntity.ErrorMaximumLength);
        return db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.WriteState, link.WriteState)
                    .SetProperty(row => row.WriteError, storedError)
                    .SetProperty(row => row.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                CancellationToken.None);
    }
}
