using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>
/// MAM-20 — the Duplicates list: the library's file names grouped into the four classes of specification §3.3 (one
/// file's names, byte-identical files, copies of one work, possible matches), worked out on every read from what the
/// app holds and stored nowhere (ADR 0028). Every signed-in user reads it, as the Library; the owner-only sentences
/// only the owner does.
/// </summary>
/// <remarks>
/// Read only: nothing here writes a row, removes a file or asks a download client (ADR 0018) — a file a torrent seeds
/// is classed and counted like any other. MAM-21 — the owner's decisions on possible matches are
/// <see cref="IDuplicateDecisionService"/>'s, stored by group key and only read here; which copy to keep is nobody's
/// here. MAM-40 — the owner's Remove this copy is <c>ICopyRemovalService</c>'s; here each file only carries its verdict, by
/// what is stored, and a name in the quarantine counts in no group.
/// </remarks>
public interface ILibraryDuplicateService
{
    /// <summary>
    /// One page of groups of <paramref name="duplicateClass"/> (a <c>DuplicateClass</c> by name, in any letter case;
    /// every class when blank), by class, then by the title of each group's first work in Ukrainian alphabetical order
    /// (MAM-21: an episode by its series' title, then its season and number, as its card is headed; a group of names no
    /// library reported by its first path), then by key, with how many there are in all. MAM-21 —
    /// with <paramref name="decision"/> (a <c>DuplicateMatchDecision</c> by name, in any letter case) only the groups
    /// the owner decided so — MAM-40: copies kept as they are are dismissed, while no new file has joined them —; when
    /// blank, every group but the dismissed ones; each group with the owner's decision on it, when it holds.
    /// A file's path problem, its inspection problem, the name of who set a link and the name of who decided only when
    /// <paramref name="callerUserId"/> is the installation owner, as on the Library page.
    /// <see cref="LibraryDuplicateListStatus.Invalid"/> for a page below 1, a page size outside 1 to 100, a class that
    /// isn't one, or a decision that isn't one — checked in that order.
    /// </summary>
    Task<LibraryDuplicateListResult> ListAsync(
        string? duplicateClass, string? decision, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-21 — the group <paramref name="key"/> names now, worked out as the list does, of any class; <c>null</c> when
    /// no group has that key. The file systems are not asked: they decide an estimate, never a key.
    /// </summary>
    Task<DuplicateGroup?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-40 — the group <paramref name="key"/> names now, worked out as the list does, with each of its files as the
    /// removal rules read them and the heading its card shows; <c>null</c> when no group has that key. The owner's Remove
    /// reads it, inside its turn.
    /// </summary>
    Task<DuplicateGroupFiles?> FindForRemovalAsync(string key, CancellationToken cancellationToken = default);
}
