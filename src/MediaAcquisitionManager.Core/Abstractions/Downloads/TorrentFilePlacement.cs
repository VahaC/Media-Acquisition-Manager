namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// MAM-30 — where a download client writes a torrent's files below the folder it saves to, and how it names them
/// (ADR 0034): each value fixes the layout and the naming together, so no combination is left without a meaning.
/// What the overwrite check (<see cref="Core.Downloads.TorrentTargetPaths"/>) looks at before a release is sent.
/// </summary>
public enum TorrentFilePlacement
{
    /// <summary>
    /// qBittorrent: each name cleaned as libtorrent 2.0 cleans it (<see cref="Core.Downloads.TorrentPathElement"/>), under
    /// any of its three content layouts (Original, Subfolder, NoSubfolder), as the app never reads which one is set.
    /// </summary>
    AnyOfThreeLayouts = 1,

    /// <summary>
    /// Transmission: the torrent's own layout only, which no setting changes — a torrent of one file has its file
    /// directly in the folder, a torrent of several files has them under a folder named after its cleaned name (none
    /// when that cleans to nothing) —, each name read every way <see cref="Core.Downloads.TorrentNameReadings"/> gives.
    /// </summary>
    OwnLayoutOnly = 2,
}
