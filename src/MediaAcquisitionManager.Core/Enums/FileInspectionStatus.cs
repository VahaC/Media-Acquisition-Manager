namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What the app's last look at one file name found (MAM-9). Stored by name; a name the app has not
/// looked at yet has none.
/// </summary>
public enum FileInspectionStatus
{
    /// <summary>The media inspector read its streams; the facts are in its media inspection.</summary>
    Inspected = 1,

    /// <summary>The file is there and readable, but the media inspector could not read it as media
    /// (not a video, damaged, or it took too long). Kept in the cache like a success, so a broken file
    /// is not read again until it changes.</summary>
    MediaUnreadable = 2,

    /// <summary>The reporting integration's path mappings give the file no path inside the app
    /// (ADR 0003).</summary>
    NoAppPath = 3,

    /// <summary>The path, or a link on the way to it, leads outside the allowed roots — or no root is
    /// allowed at all.</summary>
    OutsideAllowedRoots = 4,

    /// <summary>Nothing is at the path.</summary>
    NotFound = 5,

    /// <summary>Something is there, but not a regular file (a folder, a device, a socket).</summary>
    NotARegularFile = 6,

    /// <summary>The file is there, but the app is not allowed to read it.</summary>
    Unreadable = 7,

    /// <summary>The platform the app runs on gives no device and inode, so what was read could not be
    /// kept by the file's identity (an unknown identity beats a guessed one).</summary>
    IdentityUnknown = 8,

    /// <summary>The media inspector (ffprobe) is not installed or could not start. Never cached: the
    /// next inspection tries again.</summary>
    InspectorUnavailable = 9,
}
