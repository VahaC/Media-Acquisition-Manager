namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — how the plan of a scheduled replacement went.</summary>
public enum ReplacementPlanStatus
{
    /// <summary>Every ticked copy can be replaced, and every confirmation it needs is ticked: the plan to write.</summary>
    Planned = 1,

    /// <summary>The request doesn't fit the release's works: a copy that isn't one of theirs, a confirmation missing, or
    /// one that isn't needed or has no copy ticked. A 400: the dialog reads the list again.</summary>
    Invalid = 2,

    /// <summary>A ticked copy can't be replaced now — the release, its work or the copy itself stands in the way. A 409:
    /// the dialog reads the list again and shows why.</summary>
    Unavailable = 3,
}
