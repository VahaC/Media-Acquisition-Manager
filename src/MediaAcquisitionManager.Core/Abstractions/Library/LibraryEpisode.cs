namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// Where an episode item sits, as the library says (MAM-32): its series item and its numbers. The core
/// never takes the series item for the show (ADR 0017): it reads the series item's registry ids, and one
/// show may be several series items.
/// </summary>
/// <param name="SeriesSourceItemId">The library's own id of the series item the episode belongs to;
/// <c>null</c> when it belongs to none.</param>
/// <param name="SeasonNumber">Its season number, 0 for specials; <c>null</c> when the library gives
/// none.</param>
/// <param name="EpisodeNumber">Its number within the season; <c>null</c> when the library gives
/// none.</param>
public sealed record LibraryEpisode(string? SeriesSourceItemId, int? SeasonNumber, int? EpisodeNumber);
