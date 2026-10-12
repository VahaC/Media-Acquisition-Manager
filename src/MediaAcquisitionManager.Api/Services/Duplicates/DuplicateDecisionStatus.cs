namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-21 — how a decision on a possible match, or its undo, ended.</summary>
public enum DuplicateDecisionStatus
{
    /// <summary>The group is now decided so, or now undecided — whether or not it already was.</summary>
    Done = 1,

    /// <summary>No group has the key now, or none could have it; nothing changed.</summary>
    NotFound = 2,

    /// <summary>The decision is missing or not one, or the group's class never takes it (MAM-40: copies are only kept,
    /// one file's names never decided), which the error names; nothing changed.</summary>
    Invalid = 3,
}
