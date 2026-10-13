namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Where the scan of the media folders stands (MAM-18), for the Library page's Media folders
/// row.</summary>
/// <param name="ActiveRun">The scan under way — the running one, else the queued one; <c>null</c> when
/// none is.</param>
/// <param name="LastFinishedRun">The latest scan that succeeded or failed; <c>null</c> before the
/// first.</param>
/// <param name="HasQueuedRun">Whether a scan waits to start: the active one, or one queued behind the
/// running one. Another Scan folders would only join it.</param>
/// <param name="Files">Where the app's look at the names the scans found stands, counted now: only the
/// names no library reports under the current path mappings, so a sync that starts reporting one takes
/// it off at once. MAM-42 — the names the owner told the app to stop counting are left out: their sum is the
/// files in no library the page warns of.</param>
/// <param name="Space">How much room the files behind those same names take on disk (MAM-19), each file
/// counted once — MAM-42 — the ignored ones included, as they still take that room.</param>
/// <param name="IgnoredCount">MAM-42 — how many of those names the owner told the app to stop counting.</param>
public sealed record FolderScanOverviewDto(
    FolderScanRunDto? ActiveRun,
    FolderScanRunDto? LastFinishedRun,
    bool HasQueuedRun,
    LibrarySourceInspectionDto Files,
    LibrarySpaceDto Space,
    int IgnoredCount);
