namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// MAM-32 — the seasons a TV release's title claims, and within one season the episodes (ADR 0020):
/// claims like the rest of <see cref="ReleaseWorkClaim"/>, never verified. "S1-5" is seasons 1 to 5,
/// "S01E01-E10" episodes 1 to 10 of season 1.
/// </summary>
/// <param name="FirstSeason">The first season claimed; 0 for the specials.</param>
/// <param name="LastSeason">The last season claimed; <paramref name="FirstSeason"/> again for one
/// season.</param>
/// <param name="FirstEpisode">The first episode claimed, of that one season; <c>null</c> when the title
/// claims whole seasons.</param>
/// <param name="LastEpisode">The last episode claimed; <paramref name="FirstEpisode"/> again for one
/// episode, and <c>null</c> when it is.</param>
public sealed record ClaimedEpisodes(int FirstSeason, int LastSeason, int? FirstEpisode, int? LastEpisode)
{
    /// <summary>
    /// Whether an episode at <paramref name="season"/> and <paramref name="episode"/> is among those
    /// claimed. One without a season never is, and neither is one without a number when episodes are
    /// claimed: where it falls is unknown, and a guess would count an episode the release may not hold.
    /// </summary>
    public bool Holds(int? season, int? episode) =>
        season is { } seasonNumber
        && seasonNumber >= FirstSeason
        && seasonNumber <= LastSeason
        && (FirstEpisode is not { } firstEpisode
            || (episode is { } episodeNumber && episodeNumber >= firstEpisode && episodeNumber <= LastEpisode));
}
