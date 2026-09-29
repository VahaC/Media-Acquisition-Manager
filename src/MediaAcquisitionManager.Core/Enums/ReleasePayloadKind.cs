namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What a release is downloaded from. It decides which download clients can take the release
/// (specification §3.1 and §3.2, step 2). Stored by name.
/// </summary>
public enum ReleasePayloadKind
{
    /// <summary>A .torrent file the server fetches and hands to the client.</summary>
    TorrentFile = 1,

    /// <summary>A magnet link.</summary>
    Magnet = 2,

    /// <summary>An NZB file for a Usenet client.</summary>
    Nzb = 3,
}
