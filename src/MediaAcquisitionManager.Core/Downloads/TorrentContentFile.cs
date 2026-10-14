namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>MAM-38 — one real file of a torrent (no pad file), as a download client lays it out. MAM-30 — or one the
/// torrent marks as padding (<see cref="TorrentContent.PadFiles"/>), read the same way.</summary>
/// <param name="Path">Its path below the client's save folder in the torrent's own layout ("Original"): the
/// torrent's name, then each folder and the file's name for a torrent of several files; the name alone for one
/// file. Each element cleaned the way libtorrent cleans it (<see cref="TorrentPathElement"/>).</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Samples">Up to three of its pieces that lie wholly inside it, with their hashes; none when no piece
/// does (a small file between two others in a v1 torrent) or it is empty.</param>
/// <param name="RawPath">MAM-30 — its path below the torrent's folder as the torrent holds it, never cleaned, for a
/// client that cleans names its own way (<see cref="TorrentNameReadings"/>): each folder and the file's name for a
/// torrent of several files; for a torrent of one file, its name — the torrent's (v1) or its key in the file tree
/// (v2). Each element read as <see cref="TorrentContent.RawName"/> is. <c>null</c> when the file wasn't read from a
/// torrent's bytes.</param>
/// <param name="RawPathIsExact">MAM-30 — whether each element of <paramref name="RawPath"/> is the torrent's bytes
/// exactly: valid UTF-8 with no NUL, which reading as text changes nothing in. When not, a client that writes a file's
/// path elements as the bytes they are (Transmission 4.0: no U+FFFD for bytes that aren't UTF-8, and the name cut at a
/// NUL only by the system, after cleaning) writes a name <paramref name="RawPath"/> doesn't hold, so where it writes
/// can't be predicted. Always true for a v1 torrent of one file, whose path is the torrent's name, which every client
/// reads as text first; a v2 file tree's keys are path elements.</param>
public sealed record TorrentContentFile(
    IReadOnlyList<string> Path,
    long Length,
    IReadOnlyList<TorrentPieceSample> Samples,
    IReadOnlyList<string>? RawPath = null,
    bool RawPathIsExact = true);
