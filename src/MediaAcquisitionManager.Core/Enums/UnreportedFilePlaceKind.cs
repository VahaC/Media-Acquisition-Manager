namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-42 — where a file no library reported sits among the libraries' folders, which says why a library most likely
/// doesn't list it. Worked out when the list is read, never stored.
/// </summary>
public enum UnreportedFilePlaceKind
{
    /// <summary>Under none of the folders the libraries listed at their last sync: no library looks there.</summary>
    NoLibraryFolder = 1,

    /// <summary>Under a library's folder, yet that library didn't report it: the library skipped it by its name (a
    /// sample, a trailer, an extra), or it came after the library's last scan.</summary>
    LibraryFolder = 2,

    /// <summary>The app knows no library folder with a path in the app — no sync stored one, or none maps — so it can't
    /// tell.</summary>
    FoldersUnknown = 3,
}
