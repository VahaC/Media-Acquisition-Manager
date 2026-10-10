namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — the episodes a file's name says it covers within one season: <c>S02E05-E06</c> is season 2,
/// episodes 5 to 6 (<see cref="EpisodeRangeNames.RangeOf"/>).</summary>
/// <param name="Season">The season.</param>
/// <param name="FirstEpisode">The first episode.</param>
/// <param name="LastEpisode">The last episode, never before <paramref name="FirstEpisode"/>.</param>
public sealed record EpisodeNameRange(int Season, int FirstEpisode, int LastEpisode)
{
    /// <summary>Whether the name covers more than one episode: <c>S02E05-E05</c> covers one.</summary>
    public bool CoversSeveral => LastEpisode > FirstEpisode;
}
