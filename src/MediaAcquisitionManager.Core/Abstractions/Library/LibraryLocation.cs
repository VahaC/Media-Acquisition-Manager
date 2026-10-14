namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>One library of a media server and the folders it covers.</summary>
/// <param name="SourceLocationId">The server's own id of the library.</param>
/// <param name="Name">The library's name ("Фільми").</param>
/// <param name="ServicePaths">Its root folders as the server sees them.</param>
/// <param name="HoldsMediaFiles">
/// MAM-30 — whether its folders hold the media files themselves; false for a library of the server's own records, such
/// as Jellyfin's Collections (box sets) or playlists, whose folder is no place a download belongs.
/// </param>
public sealed record LibraryLocation(string SourceLocationId, string Name, IReadOnlyList<string> ServicePaths, bool HoldsMediaFiles = true);
