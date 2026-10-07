namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-20 — why a byte-identical group gives no estimate of the room removing its extra copies would free
/// (Vasyl, 2026-10-07: no estimate where the relationship is unknown). Written by name.
/// </summary>
public enum RecoverableSpaceGap
{
    /// <summary>
    /// A file has more names than the app found (its link count is higher, or unknown): a name outside the folders the
    /// app reads keeps the bytes whatever is removed here.
    /// </summary>
    UnlistedNames = 1,

    /// <summary>
    /// A file is on a file system that can share blocks between files (btrfs, ZFS, XFS and the like: reflinks,
    /// snapshots, deduplication), so a copy may take no room of its own.
    /// </summary>
    SharingFileSystem = 2,

    /// <summary>
    /// The file system of a file is unknown, or one whose server or lower layer may share blocks (a network share, an
    /// overlay, FUSE).
    /// </summary>
    UnknownFileSystem = 3,
}
