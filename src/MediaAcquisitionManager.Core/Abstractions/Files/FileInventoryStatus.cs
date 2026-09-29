namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>How a look at a local file went.</summary>
public enum FileInventoryStatus
{
    /// <summary>The file is there and was read.</summary>
    Found = 1,

    /// <summary>Nothing is at the path.</summary>
    NotFound = 2,

    /// <summary>The path is outside the configured roots, or a link leads out of them.</summary>
    OutsideAllowedRoots = 3,

    /// <summary>Something is there, but not a regular file (a folder, a device, a socket).</summary>
    NotARegularFile = 4,

    /// <summary>The file is there, but the app isn't allowed to read it.</summary>
    Unreadable = 5,
}
