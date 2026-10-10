namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — how long the check of a replacement's new files waits for what it can't settle yet (Vasyl, 2026-10-10;
/// ADR 0035). Constants, not settings, as ADR 0014 keeps the download following's intervals; each wait is reckoned from
/// a time stored on the job, so a restart neither shortens nor lengthens it.
/// </summary>
/// <remarks>
/// While a wait runs the job stays <c>Inspecting</c> and both copies stay — the owner may fix an item in the library
/// meanwhile, and the check passes by itself —; once it ran out, what is still missing stops the job for the owner's
/// review. A wrong or conflicting id, or any other definitive problem, never waits.
/// </remarks>
public static class ReplacementVerificationRules
{
    /// <summary>
    /// How long the client's word and the files' look may stay unsettled, from the moment the job started being checked
    /// (<see cref="Entities.ReplacementJobEntity.InspectionStartedUtc"/>): a client unreachable, a download still moving its
    /// files, a file changing while it is read. An hour is long past any move or re-check of one download.
    /// </summary>
    public static readonly TimeSpan FilesWait = TimeSpan.FromHours(1);

    /// <summary>
    /// How long the library may take to list, finish reading and identify every new file, from the moment it was asked to
    /// look (<see cref="Entities.ReplacementJobEntity.LibraryAskedUtc"/>) — Vasyl's hour (2026-10-10). Jellyfin creates
    /// the item at once and gives it its ids in the refresh that follows, which for a new series can take minutes.
    /// </summary>
    public static readonly TimeSpan LibraryWait = TimeSpan.FromHours(1);

    /// <summary>Whether <see cref="FilesWait"/> has run out at <paramref name="nowUtc"/> for a check started at
    /// <paramref name="inspectionStartedUtc"/>: at the hour exactly, it has.</summary>
    public static bool FilesWaitPassed(DateTime inspectionStartedUtc, DateTime nowUtc) => nowUtc >= inspectionStartedUtc + FilesWait;

    /// <summary>Whether <see cref="LibraryWait"/> has run out at <paramref name="nowUtc"/> for a library asked to look at
    /// <paramref name="libraryAskedUtc"/>: at the hour exactly, it has.</summary>
    public static bool LibraryWaitPassed(DateTime libraryAskedUtc, DateTime nowUtc) => nowUtc >= libraryAskedUtc + LibraryWait;
}
