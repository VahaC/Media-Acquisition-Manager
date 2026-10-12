namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — why one old copy can't be ticked for replacement (ADR 0032, ADR 0033). The first that applies in this
/// order wins — <see cref="NoAppPath"/>, <see cref="FolderNotTicked"/>, <see cref="OutsideTheClaim"/>,
/// <see cref="AlreadyScheduled"/>, <see cref="HeldByRemoval"/>, <see cref="NotInspected"/>,
/// <see cref="SharedWithUnavailableWork"/>, then from the
/// disk <see cref="Missing"/>, <see cref="Unreadable"/> and <see cref="ChangedSinceInspection"/> —, the disk read only
/// for a copy that passed the stored checks. Written by name.
/// </summary>
public enum ReplacementCopyUnavailableReason
{
    /// <summary>The owner hasn't ticked every library folder that holds the copy (ADR 0033).</summary>
    FolderNotTicked = 1,

    /// <summary>The mappings give it no path in the app, or the path lies outside every allowed root.</summary>
    NoAppPath = 2,

    /// <summary>The app hasn't read the file's identity: not inspected, or inspected without one.</summary>
    NotInspected = 3,

    /// <summary>The file's identity on disk now isn't the one its last inspection read.</summary>
    ChangedSinceInspection = 4,

    /// <summary>Nothing is at its path any more.</summary>
    Missing = 5,

    /// <summary>The disk answered something else than the file (not a regular file, unreadable, a link out).</summary>
    Unreadable = 6,

    /// <summary>A TV copy whose own library item places its episode outside the seasons or episodes the release claims,
    /// or under another series.</summary>
    OutsideTheClaim = 7,

    /// <summary>A replacement scheduled earlier, not yet ended, already holds the file.</summary>
    AlreadyScheduled = 8,

    /// <summary>
    /// The same file is also a copy of another work the release matched, one that can't be replaced (by a title alone,
    /// conflicting ids): that work's refusal holds for its every copy (ADR 0032), whichever work lists it.
    /// </summary>
    SharedWithUnavailableWork = 9,

    /// <summary>
    /// MAM-40 — the owner's removal of the file from its duplicate group, not yet ended, holds it: under Removed copies on
    /// the Duplicates page, on its way into the quarantine, in it, or on its way back or out. Its own reason, not
    /// <see cref="AlreadyScheduled"/>: no download replaces it, and the owner settles it on the Duplicates page.
    /// </summary>
    HeldByRemoval = 10,
}
