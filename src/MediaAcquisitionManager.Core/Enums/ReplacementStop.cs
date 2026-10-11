namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-27 — which stage stopped a replacement that waits for the owner's review (<see cref="ReplacementJobState.NeedsReview"/>),
/// told apart by the times the job keeps (<c>ReplacementJobProgress.StopOf</c>): what the owner may do next depends on it —
/// Retry sends a check's stop back to the check and a move's stop back to the mover, and a download the app can't follow
/// can only be left or cancelled. Never stored: derived from the row on every read, and sent by name.
/// </summary>
public enum ReplacementStop
{
    /// <summary>The download's following stopped it (the client's downloads couldn't be told apart): the check of the new
    /// files never started, so there is nothing to check again.</summary>
    Download = 1,

    /// <summary>MAM-25's check of the new files stopped it, before the mover ever looked at the job: Retry starts that
    /// check again from the files.</summary>
    Check = 2,

    /// <summary>MAM-26's mover stopped it — its checks right before the move, a name that couldn't move, or a move,
    /// restore or deletion a restart cut —: Retry puts it back in the mover's queue.</summary>
    Move = 3,
}
