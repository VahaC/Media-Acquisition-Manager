namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-27 — where one old copy the owner ticked stands in its replacement, as the job's page shows it: out of it, waiting
/// for the check, paired, stopping it, moving, in the quarantine, back or gone — derived on the server from the copy's row,
/// its video's name and the job's state (<c>ReplacementJobProgress.CopyStatusOf</c>), so the page never works the pairing
/// out again. Never stored, and sent by name.
/// </summary>
public enum ReplacementCopyStatus
{
    /// <summary>The owner dropped it from the replacement: it stays where it is and never moves with this job.</summary>
    Dropped = 1,

    /// <summary>The check hasn't paired it yet: kept until the new file is verified. A problem a pass found while it still
    /// waits on another file stops nothing yet, and is sent beside this.</summary>
    Waiting = 2,

    /// <summary>Paired with a new file of its work: it moves into the quarantine once the job is verified and moved.</summary>
    Paired = 3,

    /// <summary>It stopped the replacement, which waits for the owner's review; its problem says why.</summary>
    Stopped = 4,

    /// <summary>The check is over and found it no new file, though nothing about it stopped the job.</summary>
    NotPaired = 5,

    /// <summary>Its video is being moved into the quarantine, put back or deleted right now.</summary>
    Moving = 6,

    /// <summary>Its video is in the quarantine.</summary>
    InQuarantine = 7,

    /// <summary>Its video was put back where it was.</summary>
    Restored = 8,

    /// <summary>Its video was deleted from the quarantine for good.</summary>
    Deleted = 9,

    /// <summary>A restart cut a write, and its video was found neither where it was nor in the quarantine.</summary>
    Lost = 10,

    /// <summary>The replacement ended — cancelled, failed, or restored or deleted for good after a move that stopped before
    /// it — before anything of it moved: it stays where it is.</summary>
    Stays = 11,
}
