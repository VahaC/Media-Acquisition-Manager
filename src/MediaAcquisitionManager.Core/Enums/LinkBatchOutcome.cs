namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-34 — what "Accept all confident" did with one work it was given.</summary>
public enum LinkBatchOutcome
{
    /// <summary>Its links were saved and queued for the library.</summary>
    Linked = 1,

    /// <summary>Nothing was saved; the reason says why (the candidate is no longer confident, the work is gone…).</summary>
    Skipped = 2,
}
