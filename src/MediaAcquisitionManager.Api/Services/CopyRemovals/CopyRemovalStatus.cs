namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>MAM-40 — how an owner's list, Remove, Restore or Delete for good of a removal went.</summary>
public enum CopyRemovalStatus
{
    /// <summary>It was done, with its audit rows; the answer is the removal as it stands now, or the page.</summary>
    Done = 1,

    /// <summary>There is no such group, file in it, or removal.</summary>
    NotFound = 2,

    /// <summary>The request itself is wrong — no group or file, a page out of range —; nothing changed.</summary>
    Invalid = 3,

    /// <summary>A gate or a check refused and nothing changed — no removal row is kept —; or the video couldn't move and
    /// the removal ended <c>NotMoved</c>; or a write stopped part-way and it waits for review. The sentence says which.</summary>
    Refused = 4,
}
