namespace MediaAcquisitionManager.Core.Paths;

/// <summary>Which operating system's rules a path follows. Decided from the path's own shape, never
/// from the platform the app runs on: a service on Windows reports Windows paths to a Linux app.</summary>
public enum PathStyle
{
    /// <summary><c>/a/b</c>: one root, <c>/</c> separators, letter case significant (ext4).</summary>
    Posix = 1,

    /// <summary><c>D:\a\b</c> or <c>\\server\share\a</c>: <c>\</c> or <c>/</c> separators, letter case
    /// ignored (NTFS, SMB).</summary>
    Windows = 2,
}
