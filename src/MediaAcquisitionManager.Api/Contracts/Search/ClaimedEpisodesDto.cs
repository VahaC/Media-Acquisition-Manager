namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-32 — the seasons, or the episodes of one season, a TV release's title claims: claims,
/// never verified.</summary>
/// <param name="FirstSeason">The first season claimed; 0 for the specials.</param>
/// <param name="LastSeason">The last season claimed; the first again for one season.</param>
/// <param name="FirstEpisode">The first episode claimed, of that one season; <c>null</c> when the title
/// claims whole seasons.</param>
/// <param name="LastEpisode">The last episode claimed; the first again for one episode, and <c>null</c>
/// when it is.</param>
public sealed record ClaimedEpisodesDto(int FirstSeason, int LastSeason, int? FirstEpisode, int? LastEpisode);
