namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Where the inspection of the library's files stands (MAM-9), for the Library page.</summary>
/// <param name="HasAllowedRoots">Whether the owner has allowed any folder; without one the app reads no
/// file, and the page says so.</param>
/// <param name="ActiveRun">The run under way — the running one, else the queued one; <c>null</c> when
/// none is.</param>
/// <param name="LastFinishedRun">The latest run that succeeded or failed; <c>null</c> before the
/// first.</param>
/// <param name="HasQueuedRun">Whether a run waits to start: the active one, or one queued behind the
/// running one. Another Inspect files would only join it.</param>
public sealed record FileInspectionOverviewDto(
    bool HasAllowedRoots,
    FileInspectionRunDto? ActiveRun,
    FileInspectionRunDto? LastFinishedRun,
    bool HasQueuedRun);
