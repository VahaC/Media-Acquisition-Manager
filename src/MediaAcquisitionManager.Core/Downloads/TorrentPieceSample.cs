using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — one piece of a torrent's file that lies wholly inside the file, with the torrent's own hash of it: what
/// a file already at the file's path must hash to for its bytes to be the torrent's there.
/// </summary>
/// <param name="Range">Where the piece is in the file and how it is hashed.</param>
/// <param name="ExpectedHash">The torrent's hash of it.</param>
public sealed record TorrentPieceSample(FilePieceRange Range, byte[] ExpectedHash);
