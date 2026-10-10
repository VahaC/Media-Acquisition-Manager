namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-25 — where one file's episode item sits, as the library says when asked about that file
/// (<see cref="IMediaLibraryFileLookup.ReadFilesAsync"/>): its series item and its numbers, the end number included. The
/// sync's <see cref="LibraryEpisode"/> has no end number and stays as it is; only the check of a replacement reads one
/// (ADR 0017: the series item says which show's ids to read, it is never the show itself).
/// </summary>
/// <param name="SeriesItemId">The library's own id of the series item the episode belongs to; <c>null</c> when it belongs
/// to none (Jellyfin's Tom &amp; Jerry Kids folder is one episode item with no series).</param>
/// <param name="SeasonNumber">Its season number, 0 for specials; <c>null</c> when the library gives none.</param>
/// <param name="EpisodeNumber">Its number within the season; <c>null</c> until the library has read the file's name
/// (Jellyfin numbers a new episode only in the final save of its first refresh).</param>
/// <param name="EpisodeNumberEnd">The last episode the file covers when it holds several (<c>S01E01-E02</c>); <c>null</c>
/// when the library gives none, as it does for a file of one episode.</param>
public sealed record LibraryEpisodePlace(string? SeriesItemId, int? SeasonNumber, int? EpisodeNumber, int? EpisodeNumberEnd);
