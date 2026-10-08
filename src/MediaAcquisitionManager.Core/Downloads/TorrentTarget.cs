using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>MAM-38 — one place a torrent's file may be written to, inside the app.</summary>
/// <param name="Path">The place.</param>
/// <param name="File">The torrent's file written there.</param>
public sealed record TorrentTarget(NormalizedPath Path, TorrentContentFile File);
