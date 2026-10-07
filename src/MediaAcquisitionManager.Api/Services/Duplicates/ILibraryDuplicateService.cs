namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>
/// MAM-20 — the Duplicates list: the library's file names grouped into the four classes of specification §3.3 (one
/// file's names, byte-identical files, copies of one work, possible matches), worked out on every read from what the
/// app holds and stored nowhere (ADR 0028). Every signed-in user reads it, as the Library; the owner-only sentences
/// only the owner does.
/// </summary>
/// <remarks>
/// Read only: nothing here writes a row, removes a file or asks a download client (ADR 0018) — a file a torrent seeds
/// is classed and counted like any other. Which copy to keep, and confirming a possible match, are the owner's, on the
/// Duplicates page (MAM-21).
/// </remarks>
public interface ILibraryDuplicateService
{
    /// <summary>
    /// One page of groups of <paramref name="duplicateClass"/> (a <c>DuplicateClass</c> by name, in any letter case;
    /// every class when blank), by class, then by the title of each group's first work in Ukrainian alphabetical order
    /// (a group of names no library reported by its first path), then by key, with how many there are in all. A file's
    /// path problem, its inspection problem and the name of who set a link only when <paramref name="callerUserId"/> is
    /// the installation owner, as on the Library page. <see cref="LibraryDuplicateListStatus.Invalid"/> for a page below
    /// 1, a page size outside 1 to 100, or a class that isn't one.
    /// </summary>
    Task<LibraryDuplicateListResult> ListAsync(
        string? duplicateClass, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default);
}
