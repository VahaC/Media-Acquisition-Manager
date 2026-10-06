namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>
/// MAM-34 — one episode of a show as the source knows it (<see cref="IMetadataCatalog.GetEpisodeAsync"/>): what the
/// owner's link of a library item to that episode needs — its own IMDb id, the one id of it a library can be given.
/// </summary>
/// <param name="SeasonNumber">Its season's number; 0 for the specials.</param>
/// <param name="EpisodeNumber">Its number in the season.</param>
/// <param name="Title">Its title in the source's catalog language; <c>null</c> when the source gives none.</param>
/// <param name="AirDate">When it first aired; <c>null</c> when the source doesn't know.</param>
/// <param name="RuntimeMinutes">Its runtime in minutes; <c>null</c> when the source gives none.</param>
/// <param name="ImdbId">The episode's own IMDb id; <c>null</c> when the source gives none.</param>
public sealed record CatalogEpisode(
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateOnly? AirDate,
    int? RuntimeMinutes,
    string? ImdbId);
