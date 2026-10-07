using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — where an episode sits in a show, with the show's ids and titles: a library position's series work's, or the
/// TMDb show an episode-to-show link names (MAM-34), whose titles the library doesn't hold.
/// </summary>
/// <param name="SeriesIdentifiers">The show's ids, as every reader counts them.</param>
/// <param name="SeriesTitles">The show's titles, every source's; none for a link's show.</param>
/// <param name="SeasonNumber">The season, 0 for specials.</param>
/// <param name="EpisodeNumber">The episode within the season.</param>
public sealed record DuplicateEpisodePlace(
    IReadOnlyList<MediaIdentifier> SeriesIdentifiers, IReadOnlyList<string> SeriesTitles, int SeasonNumber, int EpisodeNumber);
