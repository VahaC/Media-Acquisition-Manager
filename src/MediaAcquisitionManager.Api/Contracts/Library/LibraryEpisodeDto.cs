namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Where an episode work sits, as a library places it (MAM-32).</summary>
/// <param name="SeriesId">The series work it belongs to; <c>null</c> when the library names none.</param>
/// <param name="SeasonNumber">Its season, 0 for specials; <c>null</c> when the library gives none.</param>
/// <param name="EpisodeNumber">Its number within the season; <c>null</c> when the library gives none.</param>
public sealed record LibraryEpisodeDto(Guid? SeriesId, int? SeasonNumber, int? EpisodeNumber);
