namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-20 — the four kinds of duplicate specification §3.3 lists, in its order: what one group of the Duplicates
/// list says its files are to each other. Worked out on every read and stored nowhere (ADR 0028); written by name.
/// </summary>
/// <remarks>
/// The classes are independent: a name can be in a <see cref="ByteIdentical"/> group and a <see cref="SameWork"/>
/// group at once, and the list is read one class at a time.
/// </remarks>
public enum DuplicateClass
{
    /// <summary>
    /// Two or more names of one file — hardlinks, one device and inode as the app read them (ADR 0006, ADR 0018): its
    /// bytes are on the disk once, so removing a name frees nothing.
    /// </summary>
    SameFile = 1,

    /// <summary>
    /// Two or more files whose bytes have one SHA-256 (ADR 0022): each takes its own room unless the file system shares
    /// blocks between them.
    /// </summary>
    ByteIdentical = 2,

    /// <summary>
    /// The files of works that share a registry id — another copy or version of the same work, across libraries
    /// (ADR 0017); never called redundant.
    /// </summary>
    SameWork = 3,

    /// <summary>
    /// Works, or names no library reported, with one title and year and no id that decides (ADR 0012): a possible match
    /// for the owner to confirm, never a removal.
    /// </summary>
    Possible = 4,
}
