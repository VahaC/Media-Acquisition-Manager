namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>How a look at a local file went.</summary>
public enum FileInventoryStatus
{
    /// <summary>The file is there and was read.</summary>
    Found = 1,

    /// <summary>Nothing is at the path.</summary>
    NotFound = 2,

    /// <summary>The path is outside the allowed roots, or a link leads out of them.</summary>
    OutsideAllowedRoots = 3,

    /// <summary>Something is there, but not a regular file (a folder, a device, a socket).</summary>
    NotARegularFile = 4,

    /// <summary>The file is there, but the app isn't allowed to read it.</summary>
    Unreadable = 5,

    /// <summary>The file is there and readable, but the media inspector couldn't read it as media
    /// (MAM-9): not a media file, damaged, or the inspector ran out of time.</summary>
    MediaUnreadable = 6,

    /// <summary>The media inspector isn't installed, or couldn't start (MAM-9). A fact about the app's
    /// host, not about the file.</summary>
    InspectorUnavailable = 7,

    /// <summary>The file's identity, size or modification time changed while it was being read (MAM-9),
    /// so what was read can't be said to belong to either version.</summary>
    ChangedWhileReading = 8,
}
