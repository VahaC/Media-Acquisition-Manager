using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the owner's TMDb links of a library work: its dialog, setting or replacing a link, removing one, identifying
/// one in the library again, the TMDb page's lists, the links "Accept all confident" sets, and the repair of the links the
/// library was never confirmed to show.
/// </summary>
/// <remarks>
/// <para>
/// One link per library item, not per work (a work can hold several items — the two "Чужі"): a change applies to the
/// work's items, each with its own row, its own audit row and its own identification, so a later sync can split them apart
/// again and each library is identified in on its own.
/// </para>
/// <para>
/// The ticket's order of a change: the link and its audit row are saved first, in one transaction (the link counts in
/// the app from then on, whatever the library does), and each link the library identifies is saved <c>Queued</c>. No member
/// calls the library: the background writer identifies each queued item through <see cref="ILibraryLinkWriter"/>, which
/// records what it sent and settles the link's state, and the dialog's poll follows it (an Apply takes minutes for a
/// series). R9 — while any link of the work is being identified, a change answers <see cref="LibraryLinkStatus.Conflict"/>;
/// a link waiting for the background writer is taken over.
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
    /// a new link, or a replaced one — and queues each for identification in its library unless it is kept in the app. A
    /// save that changes nothing records nothing and queues nothing. Queues a recommendation run when something changed.
    /// </summary>
    Task<LibraryLinkResult> SetAsync(Guid workId, LibraryLinkRequest? request, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets every link of the work in the app only (ADR 0026): the library keeps what its Identify set, and is never
    /// called. A link being identified (<c>Writing</c>) stays and the result says so (409).
    /// <see cref="LibraryLinkStatus.NothingToRemove"/> when the work has no link.
    /// </summary>
    Task<LibraryLinkRemoveResult> RemoveAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Forgets the one link <paramref name="linkId"/> the same way — the TMDb page's Unattached list. Succeeds with
    /// no dialog.</summary>
    Task<LibraryLinkResult> RemoveLinkAsync(Guid linkId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Queues every link of the work for identification again (Retry, Write again) — the owner's explicit click,
    /// which also lifts the protection of the library's own ids a bulk batch put on a link. An item that already shows the
    /// match is found so with nothing sent. A link kept in the app (an episode item's in Jellyfin, an episode's link with no
    /// id of its own to identify by) has nothing to identify.</summary>
    Task<LibraryLinkResult> WriteAgainAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Queues the one link <paramref name="linkId"/> for identification again — Retry or Write again for one item;
    /// the dialog of its work.</summary>
    Task<LibraryLinkResult> WriteLinkAsync(Guid linkId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-34 — what the repair would identify (Vasyl's decisions of 2026-10-06): the attached links, in libraries switched
    /// on, that are identified by rule and never confirmed identified — and, for a refused or failed one, only if no Apply was
    /// ever sent for it —, with the progress of the batch under way. An episode's link whose IMDb id is still to be asked
    /// counts only while a catalog is connected.
    /// </summary>
    Task<LibraryLinkCatchUpResponse> ReadCatchUpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-34 — the repair: queues those links under one new batch for the background writer (R13), which identifies each —
    /// an item that already shows the match is found so with nothing sent — each with its own audit row, protecting the
    /// library's own ids. What it queued and the batch's progress; nothing is sent before the writer takes them.
    /// </summary>
    Task<LibraryLinkCatchUpResponse> StartCatchUpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the links of <paramref name="filter"/> — <c>Unattached</c> (the library no longer reports the item),
    /// <c>Pending</c> (waiting or being identified), <c>Conflict</c> (the library holds a third TMDb id), <c>NotWritten</c>
    /// (attached links only), <c>Lost</c> — newest first; <see cref="LibraryLinkStatus.Invalid"/> for a missing or unknown filter or a bad page.
    /// </summary>
    Task<LibraryLinkListResult> ListAsync(string? filter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The links "Accept all confident" set in the click <paramref name="batchId"/>, work by work, with their
    /// states as they are now; <c>null</c> when no link has that batch.</summary>
    Task<LibraryLinkBatchResponse?> ReadBatchAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Accept all confident": links each of <paramref name="picks"/> as <see cref="SetAsync"/> would — the same link rows
    /// and audit rows, every link of the click sharing one new batch id, which also protects the library's own ids — and
    /// queues each for identification, as Set does: the links identified by rule are saved <c>Queued</c> and the writer is
    /// woken. A pick with a skip reason,
    /// a work linked or changed meanwhile, or one that can't be linked now is skipped with the sentence why, and the
    /// others still go. Queues one recommendation run when anything was linked.
    /// </summary>
    Task<LibraryLinkBatchResponse> LinkBatchAsync(IReadOnlyList<LibraryLinkBatchPick> picks, Guid ownerUserId, CancellationToken cancellationToken = default);
}
