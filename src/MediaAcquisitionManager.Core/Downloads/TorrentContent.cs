namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>MAM-38 — the files a .torrent will write, as <see cref="TorrentContentReader"/> read them.</summary>
/// <param name="HasRootFolder">Whether the torrent holds several files under a folder of its name; a torrent of
/// one file has none.</param>
/// <param name="Files">Its real files, pad files left out.</param>
public sealed record TorrentContent(bool HasRootFolder, IReadOnlyList<TorrentContentFile> Files);
