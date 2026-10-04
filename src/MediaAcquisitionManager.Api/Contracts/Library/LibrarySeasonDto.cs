namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One season of a series on the Library page (MAM-32): its number and how many of its episodes
/// the list's search and filters keep.</summary>
/// <param name="Number">The season, 0 for specials; <c>null</c> for the episodes the library gives no season
/// number.</param>
/// <param name="EpisodeCount">How many episode works of the season the search and filters keep.</param>
public sealed record LibrarySeasonDto(int? Number, int EpisodeCount);
