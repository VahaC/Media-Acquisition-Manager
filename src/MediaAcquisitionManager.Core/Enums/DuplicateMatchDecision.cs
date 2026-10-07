namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-21 — what the installation owner said of a possible match of the Duplicates list (<see cref="DuplicateClass.Possible"/>):
/// kept by its group's key (ADR 0029), stored and written by name, never by number.
/// </summary>
/// <remarks>
/// Neither makes the works one, changes whether the group needs review, or allows anything to be removed: a decision is
/// the owner's note on the list, and the library stays as it is.
/// </remarks>
public enum DuplicateMatchDecision
{
    /// <summary>The owner says the group's works and files are one work: the group stays on the list, shown as
    /// confirmed.</summary>
    Confirmed = 1,

    /// <summary>The owner says they are not: the group leaves the list and is listed with the dismissed ones, where the
    /// decision can be undone.</summary>
    Dismissed = 2,
}
