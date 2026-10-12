namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-41 — the tabs of the Jobs page, which sort the caller's jobs by what waits for them, in the order the page shows
/// them. A job falls in the first that fits it, so it is in exactly one. Never stored: a query string names one by name.
/// </summary>
public enum DownloadJobView
{
    /// <summary>
    /// Waits for its sender: a download that needs linking, or a replacement waiting for the owner's review, resting in the
    /// quarantine until he decides, or verified while replacement is switched off — the mover takes it up only once he
    /// switches it on (ADR 0033).
    /// </summary>
    NeedsYou = 1,

    /// <summary>
    /// The app still works on it by itself: its download is followed, or its replacement's new files are checked, its old
    /// copies are about to move or moving, or a Restore or Delete for good runs.
    /// </summary>
    InProgress = 2,

    /// <summary>Every other job: nothing more happens to it unless someone acts.</summary>
    Finished = 3,
}
