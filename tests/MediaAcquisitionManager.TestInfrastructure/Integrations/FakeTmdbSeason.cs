namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>MAM-33 — one season of a <see cref="FakeTmdbWork"/> show, as TMDb's show details list it.</summary>
/// <param name="Number">Its <c>season_number</c>; 0 for the specials.</param>
/// <param name="AirDate">Its <c>air_date</c> as TMDb writes it; <c>null</c> for a season not aired yet.</param>
/// <param name="EpisodeCount">Its <c>episode_count</c>.</param>
public sealed record FakeTmdbSeason(int Number, string? AirDate = "2000-01-01", int EpisodeCount = 10);
