namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-40 — where the owner's removal of one copy of a duplicate group stands (ADR 0030's quarantine, reached from the
/// Duplicates page): moved into the quarantine of the allowed root that holds it, name by name, then kept there until the
/// owner restores it or deletes it for good. Each write is an intent and an outcome, as a replacement's are, so a restart
/// in the middle finds the names by device and inode. Stored by name, so a state added later needs no migration and no
/// value is ever reused.
/// </summary>
public enum CopyRemovalState
{
    /// <summary>The owner's Remove claimed the copy and is renaming its names into the quarantine, inside the request. Found
    /// by a pass while no one in this process holds it, it was cut, and is settled from where its names are.</summary>
    Moving = 1,

    /// <summary>The copy's video is in the quarantine, with the companions that could move. It rests here, with no timer,
    /// until the owner restores it or deletes it for good.</summary>
    Quarantined = 2,

    /// <summary>A write stopped part-way with names still in the quarantine; <c>Reason</c> says why. Restore or Delete for
    /// good settles it. Also a move whose video was lost — its rename moved another file, or a restart found it neither where
    /// it was nor in the quarantine —, <c>Reason</c> saying where to look.</summary>
    NeedsReview = 3,

    /// <summary>The owner's Restore is renaming the names back, inside the request.</summary>
    Restoring = 4,

    /// <summary>The owner's Restore put every name back where it was. An end.</summary>
    Restored = 5,

    /// <summary>The owner's Delete for good is deleting the names from the quarantine, inside the request.</summary>
    Deleting = 6,

    /// <summary>The owner's Delete for good removed every name the quarantine held. An end.</summary>
    Deleted = 7,

    /// <summary>The copy's video couldn't be moved — its rename was refused, or never began —, so nothing of it is in the
    /// quarantine; <c>Reason</c> says why. An end.</summary>
    NotMoved = 8,
}
