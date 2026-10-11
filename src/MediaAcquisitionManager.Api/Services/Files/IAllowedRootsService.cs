using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// The allowed roots (MAM-9): the folders inside the app where it may read library files, kept in the
/// database and edited by the installation owner on the Media folders page. None by default, so a fresh
/// installation reads nothing until the owner names a folder. Expected failures come back as result
/// statuses, never as exceptions.
/// </summary>
/// <remarks>
/// MAM-24 — the same page's one Save also holds the owner's replacement gates (ADR 0033): the master switch, off by
/// default, and a tick per library folder, none by default.
/// </remarks>
public interface IAllowedRootsService
{
    /// <summary>Every allowed root in canonical form, in the order the owner entered them.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the allowed roots with <paramref name="paths"/>, the replacement switch with
    /// <paramref name="replacementEnabled"/> and the ticked library folders with <paramref name="replacementFolders"/>,
    /// in one transaction, with one audit row for <paramref name="actorUserId"/> per part that changed:
    /// <c>AllowedRootsChanged</c>, <c>ReplacementSwitchedOn</c> or <c>ReplacementSwitchedOff</c>, and
    /// <c>ReplacementFoldersChanged</c>. A save that is the stored state in canonical form — the ticks in any order —
    /// writes nothing. <see cref="AllowedRootsUpdateStatus.Invalid"/>, writing nothing, when the roots break a rule of
    /// <see cref="Core.Files.AllowedRootRules"/>, the ticks one of <see cref="Core.Files.ReplacementFolderRules"/>, or
    /// a tick is neither a library folder the mappings bring into the app nor a tick already stored — MAM-26 — or the new
    /// roots would leave a quarantine that holds a replacement's names outside every one of them (ADR 0030). Saving starts
    /// no inspection: the owner does that from the Library page.
    /// </summary>
    Task<AllowedRootsUpdateResult> UpdateAsync(
        IReadOnlyList<string?> paths,
        bool replacementEnabled,
        IReadOnlyList<string?> replacementFolders,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The response the settings page reads. MAM-26 — with, for every library folder whose tick counts, what keeps a rename
    /// into the quarantine from working there, from one look through the write contract at those folders alone, never
    /// their files; and the room each quarantine takes, from the database.
    /// </summary>
    Task<AllowedRootsResponse> GetAsync(CancellationToken cancellationToken = default);
}
