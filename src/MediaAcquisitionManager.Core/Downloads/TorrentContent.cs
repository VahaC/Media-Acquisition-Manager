namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>MAM-38 — the files a .torrent will write, as <see cref="TorrentContentReader"/> read them.</summary>
/// <param name="HasRootFolder">Whether the torrent holds several files under a folder of its name; a torrent of
/// one file has none.</param>
/// <param name="Files">Its real files, pad files left out.</param>
/// <param name="RawName">MAM-30 — its name as the torrent holds it (<c>name.utf-8</c> before <c>name</c>), never
/// cleaned, for a client that cleans names its own way (<see cref="TorrentNameReadings"/>): read as Transmission reads
/// text — cut at its first NUL, each sequence that isn't valid UTF-8 one U+FFFD. <c>null</c> when the content wasn't
/// read from a torrent's bytes.</param>
/// <param name="PadFiles">MAM-30 — the files it marks as padding (BEP 47, <c>attr</c> with <c>p</c>) that have a path,
/// in the torrent's order, each read as a real file is: libtorrent never writes one, but a client that ignores the
/// mark (Transmission) writes it like any other file, at the path the torrent gives and with the bytes its pieces
/// hash to — so the mark alone says nothing of where. <c>null</c> or empty for none.</param>
public sealed record TorrentContent(
    bool HasRootFolder,
    IReadOnlyList<TorrentContentFile> Files,
    string? RawName = null,
    IReadOnlyList<TorrentContentFile>? PadFiles = null);
