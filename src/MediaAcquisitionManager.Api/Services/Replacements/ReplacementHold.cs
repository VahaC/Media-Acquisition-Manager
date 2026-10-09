namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — a torrent that lists an old copy (ADR 0031): never a reason it can't be ticked, said before the owner
/// schedules.</summary>
/// <param name="ClientName">The download client holding the torrent.</param>
/// <param name="TorrentId">
/// The client's id of the torrent (qBittorrent's hash): what tells two torrents of one name apart — a cross-seed adds the
/// same content again under another hash, often with the same name —, so each is named, and removing one leaves the other
/// holding the copy.
/// </param>
/// <param name="TorrentName">The torrent's name in it.</param>
/// <param name="SecondName">
/// The torrent's file is another name of the copy's data (a hardlink) at another path: moving the copy leaves the
/// torrent's own and frees no space. Otherwise the torrent holds the copy itself — its path, or one reaching the same
/// file through a link —, and the owner removes that torrent, without its files, before the copy can move.
/// </param>
public sealed record ReplacementHold(string ClientName, string TorrentId, string TorrentName, bool SecondName);
