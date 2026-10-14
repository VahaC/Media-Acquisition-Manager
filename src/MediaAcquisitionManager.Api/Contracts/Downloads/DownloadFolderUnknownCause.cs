namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-31 — why the Download dialog's destination has a folder the app doesn't know
/// (<c>NotCheckedFolderUnknown</c>), so its warning names the client and the fix instead of "has no path in the app, or
/// its download client can't say where it is". The live check stopped Transmission and read that line above the
/// replacement's own "couldn't ask “Transmission”": the first named neither the client nor the fix.
/// </summary>
public enum DownloadFolderUnknownCause
{
    /// <summary>The client couldn't be asked where the destination saves: switched off, unreachable, refusing the app,
    /// a type this build can't use, or a stored secret the app can't read.</summary>
    ClientNotAsked = 1,

    /// <summary>The client answered without the destination's category (a client of categories, as qBittorrent is).</summary>
    CategoryMissing = 2,

    /// <summary>The client said where the destination saves, and none of its path mappings covers that folder, so adding
    /// one is the fix. Two mappings that send it to different places, or a folder that isn't a full path, give no
    /// cause.</summary>
    NoPathMapping = 3,
}
