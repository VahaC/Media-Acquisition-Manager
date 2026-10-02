namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One file of a <see cref="FakeQBittorrentTorrent"/> (MAM-14): what <c>torrents/files</c> lists for it, in
/// qBittorrent's own terms.
/// </summary>
/// <param name="Name">Its path under the torrent's folder, <c>/</c>-separated, the root folder included.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Progress">From 0 to 1.</param>
/// <param name="Priority">qBittorrent's priority: 0 is "do not download", 1 normal, 6 high, 7 maximal.</param>
public sealed record FakeQBittorrentFile(string Name, long Size, double Progress, int Priority = 1);
