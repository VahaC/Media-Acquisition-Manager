using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — whether a file system lets two files share their blocks, which decides whether removing a byte-identical
/// copy frees its room (Vasyl, 2026-10-07: an estimate only where every file's file system is known not to share).
/// </summary>
/// <remarks>
/// Known to give every file its own blocks: the ext family, JFS, ReiserFS, FAT and exFAT, NTFS. Known to be able to
/// share them — reflinks (<c>cp --reflink</c>), snapshots, deduplication — btrfs, ZFS, XFS, bcachefs, APFS, ReFS, OCFS2
/// and NILFS2: two copies there may take the room of one, and removing one may free nothing. Anything else is unknown:
/// a network share's server, an overlay's or a FUSE file system's lower layer may share without saying so (ADR 0028).
/// </remarks>
public static class FileSystemSharing
{
    /// <summary>The file systems whose files never share blocks.</summary>
    private static readonly HashSet<string> Separate = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext2", "ext3", "ext4", "jfs", "reiserfs", "vfat", "exfat", "ntfs", "ntfs3",
    };

    /// <summary>The file systems whose files can share blocks.</summary>
    private static readonly HashSet<string> Sharing = new(StringComparer.OrdinalIgnoreCase)
    {
        "btrfs", "zfs", "xfs", "bcachefs", "apfs", "refs", "ocfs2", "nilfs2",
    };

    /// <summary>
    /// Why no estimate can be given for a copy on a file system of <paramref name="fileSystemType"/> (the mount table's
    /// name, in any letter case): <c>null</c> when its copies each take their own room,
    /// <see cref="RecoverableSpaceGap.SharingFileSystem"/> when they can share it, and
    /// <see cref="RecoverableSpaceGap.UnknownFileSystem"/> for a blank type or any other.
    /// </summary>
    public static RecoverableSpaceGap? GapOf(string? fileSystemType)
    {
        var type = fileSystemType?.Trim();
        if (string.IsNullOrEmpty(type))
            return RecoverableSpaceGap.UnknownFileSystem;
        if (Separate.Contains(type))
            return null;
        return Sharing.Contains(type) ? RecoverableSpaceGap.SharingFileSystem : RecoverableSpaceGap.UnknownFileSystem;
    }
}
