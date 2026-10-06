namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-34 — one episode of a show of a <see cref="FakeTmdbServer"/>'s catalog, as <c>GET /3/tv/{id}/season/{season}/episode/{episode}</c>
/// answers it (the shape of <see cref="TmdbSamples.TvEpisodeWwiiFile"/>, recorded live).
/// </summary>
/// <param name="ShowId">TMDb's id of the show.</param>
/// <param name="SeasonNumber">Its <c>season_number</c>.</param>
/// <param name="EpisodeNumber">Its <c>episode_number</c>.</param>
/// <param name="Title">Its <c>name</c>, in the catalog's language.</param>
/// <param name="ImdbId">Its <c>external_ids.imdb_id</c>, sent when the details are asked to append the external ids;
/// TMDb's <c>null</c> when <c>null</c>.</param>
/// <param name="AirDate">Its <c>air_date</c> as TMDb writes it.</param>
/// <param name="RuntimeMinutes">Its <c>runtime</c>; TMDb's <c>null</c> when <c>null</c>.</param>
public sealed record FakeTmdbEpisode(
    int ShowId,
    int SeasonNumber,
    int EpisodeNumber,
    string Title,
    string? ImdbId = null,
    string? AirDate = "2000-01-01",
    int? RuntimeMinutes = null);
