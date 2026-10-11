namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>MAM-26 — how a call to <see cref="IFileQuarantine"/> went. Every status but <see cref="Done"/> and
/// <see cref="Swapped"/> left the name where it was.</summary>
public enum FileQuarantineStatus
{
    /// <summary>The rename or the deletion happened, or the read was made.</summary>
    Done = 1,

    /// <summary>Nothing is at the path.</summary>
    NotFound = 2,

    /// <summary>The path is outside the allowed roots, a link leads out of them, or — for a deletion — it isn't
    /// inside a quarantine.</summary>
    OutsideAllowedRoots = 3,

    /// <summary>The file's size, modification time, device or inode isn't what the caller expected, it isn't a
    /// regular file, or a name on the way was swapped for a link while the app checked it.</summary>
    Changed = 4,

    /// <summary>A name is already at the place the file would go (<c>EEXIST</c>).</summary>
    NameTaken = 5,

    /// <summary>The folder a restore would put the file back into is gone; the app creates no folder for it.</summary>
    FolderGone = 6,

    /// <summary>The two ends lie on different mounts (<c>EXDEV</c>, or mount ids that differ): the rename would be a
    /// copy.</summary>
    OtherMount = 7,

    /// <summary>The mount is read-only (<c>EROFS</c>).</summary>
    ReadOnly = 8,

    /// <summary>The app's user may not write there (<c>EACCES</c>, <c>EPERM</c>, or an owner the container can't map,
    /// <c>EOVERFLOW</c>).</summary>
    NotAllowed = 9,

    /// <summary>This platform or file system can't rename without replacing (<c>EINVAL</c>, a kernel without
    /// <c>STATX_MNT_ID</c>, or not Linux).</summary>
    NotSupported = 10,

    /// <summary>Anything else the file system refused, with its own reason.</summary>
    Failed = 11,

    /// <summary>
    /// The name was renamed, but the file now at the new name isn't the one checked: another file took its place in
    /// between the last look and the rename. That other file is where the caller's file was going, and the caller's own
    /// file wasn't found; nothing else is done about it.
    /// </summary>
    Swapped = 12,
}
