namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — a torrent that lists an old copy (ADR 0031).</summary>
/// <param name="ClientName">The download client holding it.</param>
/// <param name="TorrentId">The client's id of the torrent (qBittorrent's hash): two torrents of one name — a cross-seed —
/// are two holds, each named, never one.</param>
/// <param name="TorrentName">The torrent's name there.</param>
/// <param name="SecondName">The torrent's file is another name of the copy's data at another path (a hardlink): moving
/// the copy frees no space. Otherwise the torrent holds the copy itself, and the owner removes that torrent, without its
/// files, before the copy can move.</param>
public sealed record ReplacementHoldDto(string ClientName, string TorrentId, string TorrentName, bool SecondName);
