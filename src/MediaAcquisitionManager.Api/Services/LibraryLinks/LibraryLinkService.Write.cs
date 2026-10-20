using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — Retry and Write again: a link queued for the background writer to identify again, after a refusal or a failure,
// after the library lost it, or to check an item that was identified before — an item that already shows the match is
// found so with nothing sent. A link kept in the app (an episode's without its numbers, an episode item's in Jellyfin, one
// of a library type that can't identify) has nothing to identify. Only the owner's click queues it again; nothing retries
// by itself (house rule 8). Never inside the request: an Apply takes minutes for a series.
public sealed partial class LibraryLinkService
{
    private static readonly Message NothingLinkedError = Message.Of("library-links.write.nothing-linked");
    private static readonly Message NothingToWriteUnattachedError = Message.Of("library-links.write.nothing-unattached");

    // {reason} is why the link stays in the app (LibraryLinkWritability), the library's own message among them: a whole
    // sentence nested in this one.
    private static Message NothingToWriteError(Message reason) => Message.Of("library-links.write.nothing-to-write", ("reason", reason));

    public async Task<LibraryLinkResult> WriteAgainAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        if (!await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken))
            return LibraryLinkResult.NotFound;
        var links = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.MediaWorkId == workId)
            .OrderBy(link => link.SourceItemId)
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
            return LibraryLinkResult.Conflict(NothingLinkedError);
        if (links.Any(link => link.WriteState == LibraryLinkWriteState.Writing))
            return LibraryLinkResult.Conflict(UnderWayError);

        var connections = await ConnectionsAsync([.. links.Select(link => link.IntegrationConnectionId).Distinct()], cancellationToken);
        var queued = false;
        Message? refusal = null;
        foreach (var link in links)
        {
            if (IdentifyRefusal(connections.GetValueOrDefault(link.IntegrationConnectionId), link) is { } error)
                refusal ??= error;
            else if (link.WriteState == LibraryLinkWriteState.Queued)
                // Already waiting for the worker: left as it is, and not an error.
                queued = true;
            else if (await QueueAsync(link))
                queued = true;
            else
                refusal ??= UnderWayError;
        }

        if (!queued)
            return LibraryLinkResult.Conflict(refusal!);

        writeQueue.Signal();
        return await DialogAsync(workId, CancellationToken.None) is { } dialog ? LibraryLinkResult.Succeeded(dialog) : LibraryLinkResult.NotFound;
    }

    public async Task<LibraryLinkResult> WriteLinkAsync(Guid linkId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var link = await db.LibraryItemLinks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == linkId, cancellationToken);
        if (link is null)
            return LibraryLinkResult.NotFound;
        if (link.MediaWorkId is not { } workId)
            return LibraryLinkResult.Conflict(NothingToWriteUnattachedError);
        if (link.WriteState == LibraryLinkWriteState.Writing)
            return LibraryLinkResult.Conflict(UnderWayError);

        var connections = await ConnectionsAsync([link.IntegrationConnectionId], cancellationToken);
        if (IdentifyRefusal(connections.GetValueOrDefault(link.IntegrationConnectionId), link) is { } refusal)
            return LibraryLinkResult.Conflict(refusal);
        if (link.WriteState != LibraryLinkWriteState.Queued && !await QueueAsync(link))
            return LibraryLinkResult.Conflict(UnderWayError);

        writeQueue.Signal();
        return await DialogAsync(workId, CancellationToken.None) is { } dialog ? LibraryLinkResult.Succeeded(dialog) : LibraryLinkResult.NotFound;
    }

    /// <summary>
    /// The conditional queueing of a link for a Retry (R9): only while it is as it was read — not <c>Writing</c> or
    /// <c>Queued</c>, under the same attempt — so of two callers (a worker's claim, another click) only one wins. Its error
    /// goes, and so does its batch: the owner's explicit click is his word, which lifts the protection a bulk batch put on
    /// the link (<c>BatchId</c> decides <c>ProtectHeldIds</c>). Its identified time is kept: only an identification changes it.
    /// <c>false</c> when the link changed meanwhile.
    /// </summary>
    private async Task<bool> QueueAsync(LibraryItemLinkEntity link) =>
        await db.LibraryItemLinks
            .Where(row => row.Id == link.Id
                && row.WriteState == link.WriteState
                && row.WriteState != LibraryLinkWriteState.Writing
                && row.WriteState != LibraryLinkWriteState.Queued
                && row.WriteAttemptId == link.WriteAttemptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.WriteState, LibraryLinkWriteState.Queued)
                    .SetProperty(row => row.WriteError, (Message?)null)
                    .SetProperty(row => row.WriteStateUtc, time.GetUtcNow().UtcDateTime)
                    .SetProperty(row => row.BatchId, (Guid?)null),
                CancellationToken.None) == 1;

    /// <summary>Why <paramref name="link"/> can't be identified now: it stays in the app — its library type can't identify,
    /// doesn't identify that kind of item (Jellyfin: an episode item), or it is an episode's link with no id of its own to
    /// identify by (<see cref="LibraryLinkWritability"/>) —, so there is nothing to do; or, R12, the integration is
    /// switched off; <c>null</c> when it can.</summary>
    private Message? IdentifyRefusal(IntegrationConnectionEntity? connection, LibraryItemLinkEntity link)
    {
        // Removed with its links (decision 12); a link read a moment before is gone with it.
        if (connection is null)
            return UnderWayError;
        if (LibraryLinkWritability.KeptReason(link, adapters.Find<IMediaLibraryItemIdentifier>(connection.AdapterType), connection.Name) is { } reason)
            return NothingToWriteError(reason);
        return connection.IsEnabled ? null : LibraryLinkWriter.SwitchedOffError(connection.Name);
    }
}
