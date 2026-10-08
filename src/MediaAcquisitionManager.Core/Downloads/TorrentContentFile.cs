namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>MAM-38 — one real file of a torrent (no pad file), as a download client lays it out.</summary>
/// <param name="Path">Its path below the client's save folder in the torrent's own layout ("Original"): the
/// torrent's name, then each folder and the file's name for a torrent of several files; the name alone for one
/// file. Each element cleaned the way libtorrent cleans it (<see cref="TorrentPathElement"/>).</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Samples">Up to three of its pieces that lie wholly inside it, with their hashes; none when no piece
/// does (a small file between two others in a v1 torrent) or it is empty.</param>
public sealed record TorrentContentFile(IReadOnlyList<string> Path, long Length, IReadOnlyList<TorrentPieceSample> Samples);
