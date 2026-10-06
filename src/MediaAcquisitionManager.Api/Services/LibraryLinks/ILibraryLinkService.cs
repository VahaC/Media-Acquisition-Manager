using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the owner's TMDb links of a library work: its dialog, setting or replacing a link, removing one, writing
/// one into the library again, the TMDb page's lists, the links "Accept all confident" sets, and the catch-up of the
/// links set before the app wrote series and episodes and asked for the library's lookup.
/// </summary>
/// <remarks>
/// <para>
/// One link per library item, not per work (a work can hold several items — the two "Чужі"): a change applies to the
/// work's items, each with its own row, its own audit row and its own write, so a later sync can split them apart again
/// and each library is written on its own.
/// </para>
/// <para>
/// The ticket's order of a change: the link and its audit row are saved first, in one transaction (the link counts in
/// the app from then on, whatever the library does); then each item is written into its library through
/// <see cref="ILibraryLinkWriter"/>, which records what it sent and settles the link's state. R9 — while any link of the
/// work is being written, a change answers <see cref="LibraryLinkStatus.Conflict"/>; a link waiting for the background
/// writer is taken over.
/// </para>
/// <para>
/// Owner-only, every member: who may call is the endpoints' business (<c>LibraryLinksAreOwnerOnlyConvention</c>), so
/// what these return carries the owner's fields — who set a link, why the library doesn't hold it.
/// </para>
/// </remarks>
public interface ILibraryLinkService
{
    /// <summary>The work <paramref name="workId"/>'s TMDb dialog; <see cref="LibraryLinkStatus.NotFound"/> without such a
    /// work.</summary>
    Task<LibraryLinkResult> ReadDialogAsync(Guid workId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links the work's items (every one, or those <paramref name="request"/> names, R17) to the film or show it names —
    /// a new link, or a replaced one — and writes each into its library unless it is kept in the app. A save that changes
    /// nothing records nothing and writes nothing. Queues a recommendation run when something changed.
    /// </summary>
    Task<LibraryLinkResult> SetAsync(Guid workId, LibraryLinkRequest? request, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every link of the work, each after its library item is put back as the app found it (R11): a link whose
    /// library can't be changed back stays, and the result says why. <see cref="LibraryLinkStatus.NothingToRemove"/> when
    /// the work has no link.
    /// </summary>
    Task<LibraryLinkRemoveResult> RemoveAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Removes the one link <paramref name="linkId"/> the same way — the TMDb page's Unattached list, whose links
    /// have no item left to put back. Succeeds with no dialog.</summary>
    Task<LibraryLinkResult> RemoveLinkAsync(Guid linkId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Writes every link of the work into its library again (Retry, Write again), and asks the library to look
    /// each item up again — a write that finds the ids in place still asks: a link kept in the app (an episode item's in
    /// Jellyfin, an episode's link that writes no id of its own) has nothing to write.</summary>
    Task<LibraryLinkResult> WriteAgainAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Writes the one link <paramref name="linkId"/> into its library again — Retry or Write again for one item,
    /// the library's lookup asked again with it; the dialog of its work.</summary>
    Task<LibraryLinkResult> WriteLinkAsync(Guid linkId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-34 — what the catch-up would send (Vasyl's decision of 2026-10-06): the links that write ids of their own and
    /// Jellyfin doesn't hold yet — kept in the app under the first rules (a series', an episode's with its numbers on a
    /// film item), or not written —, and the written links whose item the library was never asked to look up. Attached links in
    /// libraries switched on only; an episode's whose IMDb id is still to be asked only while a catalog is connected.
    /// </summary>
    Task<LibraryLinkCatchUpResponse> ReadCatchUpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-34 — the catch-up: queues those links for the background writer (R13), which writes each — a write that finds
    /// the ids in place sends nothing — and asks the library to look its item up, one at a time, each with its own audit
    /// rows. What it queued; nothing is sent before the writer takes them.
    /// </summary>
    Task<LibraryLinkCatchUpResponse> StartCatchUpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the links of <paramref name="filter"/> — <c>Unattached</c> (the library no longer reports the item),
    /// <c>Pending</c> (waiting or being written), <c>Conflict</c> (the library holds a third TMDb id), <c>NotWritten</c>,
    /// <c>Lost</c> — newest first; <see cref="LibraryLinkStatus.Invalid"/> for a missing or unknown filter or a bad page.
    /// </summary>
    Task<LibraryLinkListResult> ListAsync(string? filter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The links "Accept all confident" set in the click <paramref name="batchId"/>, work by work, with their
    /// states as they are now; <c>null</c> when no link has that batch.</summary>
    Task<LibraryLinkBatchResponse?> ReadBatchAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Accept all confident": links each of <paramref name="picks"/> as <see cref="SetAsync"/> would — the same link rows
    /// and audit rows, every link of the click sharing one new batch id — but leaves the writes to the background writer
    /// (decision 6): the writable links are saved <c>Queued</c> and the writer is woken once. A pick with a skip reason,
    /// a work linked or changed meanwhile, or one that can't be linked now is skipped with the sentence why, and the
    /// others still go. Queues one recommendation run when anything was linked.
    /// </summary>
    Task<LibraryLinkBatchResponse> LinkBatchAsync(IReadOnlyList<LibraryLinkBatchPick> picks, Guid ownerUserId, CancellationToken cancellationToken = default);
}
