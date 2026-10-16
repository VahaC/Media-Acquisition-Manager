namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-45 — the order the Duplicates list's groups are listed in (Vasyl's decision e, 2026-10-16: an AI assistant asked
/// what to clean up reads first what frees the most room). Every order is total: a tie falls back to
/// <see cref="Default"/>'s order, which ends in the group key, so a page never reshuffles between two reads. Read by name
/// from the query string.
/// </summary>
public enum DuplicateSort
{
    /// <summary>The default: by class, in the specification's order, then by the title of each group's first work in
    /// Ukrainian alphabetical order, then by key.</summary>
    Default = 1,

    /// <summary>The most room recoverable first, by the estimate a group carries; a group with none — byte-identical
    /// copies the estimate can't be sure of, copies of one work and possible matches, which are never redundant — last.
    /// One file's names, which free nothing, come just before those.</summary>
    Recoverable = 2,
}
