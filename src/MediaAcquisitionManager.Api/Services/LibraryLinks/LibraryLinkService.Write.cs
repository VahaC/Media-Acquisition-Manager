using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — Retry and Write again: a link to a film written into its library again, after a failed or unconfirmed
// write, or after the library lost it; a link the library doesn't write (an episode's, Jellyfin's series') has nothing
// to write. Only the owner's click sends it again; nothing retries by itself (house rule 8).
public sealed partial class LibraryLinkService
{
    private const string NothingLinkedError = "There is nothing to write: no item of this work is linked.";
    private const string NothingToWriteForEpisodeError = "There is nothing to write: an episode's link to its show stays in the app.";
    private const string NothingToWriteUnattachedError = "There is nothing to write: the library no longer reports this item.";

    private static string NothingToWriteWithoutWriterError(string name) =>
        $"There is nothing to write: “{name}” can't set ids, so the link stays in the app.";

    // {reason} is the library's own sentence (IMediaLibraryIdentifierWriter.UnwritableKindReason).
    private static string NothingToWriteForKindError(string reason) => $"There is nothing to write: {reason}";

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
        var toWork = links.Where(link => link.Target == LibraryLinkTarget.Work).ToList();
        if (toWork.Count == 0)
            return LibraryLinkResult.Conflict(NothingToWriteForEpisodeError);

        var connections = await ConnectionsAsync([.. toWork.Select(link => link.IntegrationConnectionId).Distinct()], cancellationToken);
        // Read before the first claim: from a claim on, nothing may take the request's token (R9), or a cancel would leave
        // the claimed links Writing — refusing every change of the work — until the next restart.
        var actor = await ActorAsync(ownerUserId, cancellationToken);
        var claims = new List<(Guid LinkId, Guid AttemptId)>();
        string? refusal = null;
        foreach (var link in toWork)
        {
            if (WriteRefusal(connections.GetValueOrDefault(link.IntegrationConnectionId), link.ItemKind) is { } error)
                refusal ??= error;
            else if (await ClaimAsync(link) is { } attemptId)
                claims.Add((link.Id, attemptId));
            else
                refusal ??= UnderWayError;
        }

        if (claims.Count == 0)
            return LibraryLinkResult.Conflict(refusal!);

        // Claimed: each write runs to its end (R9, ADR 0013).
        foreach (var (linkId, attemptId) in claims)
            await writer.WriteAsync(linkId, attemptId, actor, CancellationToken.None);
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
        if (link.Target != LibraryLinkTarget.Work)
            return LibraryLinkResult.Conflict(NothingToWriteForEpisodeError);

        var connections = await ConnectionsAsync([link.IntegrationConnectionId], cancellationToken);
        if (WriteRefusal(connections.GetValueOrDefault(link.IntegrationConnectionId), link.ItemKind) is { } refusal)
            return LibraryLinkResult.Conflict(refusal);
        // Read before the claim, as above.
        var actor = await ActorAsync(ownerUserId, cancellationToken);
        if (await ClaimAsync(link) is not { } attemptId)
            return LibraryLinkResult.Conflict(UnderWayError);

        await writer.WriteAsync(link.Id, attemptId, actor, CancellationToken.None);
        return await DialogAsync(workId, CancellationToken.None) is { } dialog ? LibraryLinkResult.Succeeded(dialog) : LibraryLinkResult.NotFound;
    }

    /// <summary>Why a link to the work itself on an item of <paramref name="itemKind"/> can't be written now: its library
    /// type can't set ids, or doesn't write that kind of item (Jellyfin: a series) — its links stay in the app, so there is
    /// nothing to write —, or, R12, the integration is switched off; <c>null</c> when it can.</summary>
    private string? WriteRefusal(IntegrationConnectionEntity? connection, MediaWorkKind itemKind)
    {
        // Removed with its links (decision 12); a link read a moment before is gone with it.
        if (connection is null)
            return UnderWayError;
        if (adapters.Find<IMediaLibraryIdentifierWriter>(connection.AdapterType) is not { } libraryWriter)
            return NothingToWriteWithoutWriterError(connection.Name);
        if (!libraryWriter.WritableKinds.Contains(itemKind))
            return NothingToWriteForKindError(libraryWriter.UnwritableKindReason(itemKind));
        return connection.IsEnabled ? null : LibraryLinkWriter.SwitchedOffError(connection.Name);
    }
}
