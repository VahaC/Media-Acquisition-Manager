namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-26 — where one name a replacement moves into the quarantine stands (ADR 0030). Each write through the write
/// contract is an intent (<see cref="Moving"/>, <see cref="Restoring"/>, <see cref="Deleting"/>) committed before the
/// call and an outcome committed after it, so a restart between the two finds the name by device and inode and says
/// where it is. Stored by name.
/// </summary>
public enum ReplacementNameState
{
    /// <summary>Written with the move's plan, before anything is renamed: the name still waits at its old path.</summary>
    Planned = 1,

    /// <summary>The rename into the quarantine is under way (the intent is committed, the outcome isn't).</summary>
    Moving = 2,

    /// <summary>The name is in the quarantine, at its quarantine path, with the identity the move read.</summary>
    Quarantined = 3,

    /// <summary>The name stays at its old path — a torrent holds it, or its rename was refused —; <c>Problem</c> says
    /// why.</summary>
    Stayed = 4,

    /// <summary>The owner's Restore is renaming it back (the intent is committed, the outcome isn't).</summary>
    Restoring = 5,

    /// <summary>The name is back at its old path.</summary>
    Restored = 6,

    /// <summary>The owner's Delete for good is deleting it from the quarantine (the intent is committed, the outcome
    /// isn't).</summary>
    Deleting = 7,

    /// <summary>The name was deleted from the quarantine for good.</summary>
    Deleted = 8,

    /// <summary>A restart cut a write, and the name was found neither where it was nor where it was going; <c>Problem</c>
    /// says so.</summary>
    Lost = 9,
}
