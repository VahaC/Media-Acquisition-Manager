namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Where a destination's folder is, as far as the app can tell (MAM-11, ADR 0003): inside a media
/// library folder, which is where a destination is expected to save, or why the app can't say. Anything
/// but <see cref="InLibrary"/> keeps automatic replacement off for the destination (Phase C).
/// </summary>
public enum DestinationPathStatus
{
    /// <summary>The folder maps inside a media library folder.</summary>
    InLibrary = 1,

    /// <summary>The app knows where the folder is, and it is inside no media library folder: downloads
    /// still work, automatic replacement doesn't.</summary>
    OutsideLibraries = 2,

    /// <summary>The client doesn't say which folder the destination saves to, or the client's path
    /// mappings give no app path for it (not covered, ambiguous or not a full path).</summary>
    PathUnknown = 3,

    /// <summary>The app knows where the folder is, but no media library folders are stored yet: a library
    /// has to be synced first.</summary>
    LibrariesUnknown = 4,

    /// <summary>The client has no destination with this key (any more).</summary>
    CategoryMissing = 5,

    /// <summary>The download client integration is switched off, so it isn't asked.</summary>
    ClientDisabled = 6,

    /// <summary>The client couldn't be asked: its call failed, its type isn't registered in this build,
    /// or its stored secret can't be read.</summary>
    ClientUnavailable = 7,

    /// <summary>MAM-30 — the client can't see or use the folder (ADR 0039): it answered that it can't reach it, or the
    /// stored folder isn't a full path, or the client didn't list it. Listed with why, never picked and never sent
    /// to.</summary>
    FolderNotSeen = 8,
}
