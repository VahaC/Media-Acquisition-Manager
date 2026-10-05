using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — which tab a work goes to, and which kinds of work a set of tabs asks the catalog for (Vasyl's
/// decision 2).
/// </summary>
public static class RecommendationTabs
{
    /// <summary>Every tab, in the page's order.</summary>
    public static IReadOnlyList<RecommendationTab> All { get; } =
        [RecommendationTab.Movies, RecommendationTab.Series, RecommendationTab.Cartoons, RecommendationTab.Documentaries];

    /// <summary>
    /// The tab of a work of <paramref name="kind"/> with genres of <paramref name="roles"/>: a documentary goes
    /// to Documentaries whatever else it is (an animated documentary is a documentary first), else an
    /// animation to Cartoons, else a film to Movies and a show to Series.
    /// </summary>
    public static RecommendationTab Classify(MediaWorkKind kind, IEnumerable<CatalogGenreRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (kind is not (MediaWorkKind.Movie or MediaWorkKind.Series))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only a film or a show is recommended.");

        var known = roles.ToHashSet();
        if (known.Contains(CatalogGenreRole.Documentary))
            return RecommendationTab.Documentaries;
        if (known.Contains(CatalogGenreRole.Animation))
            return RecommendationTab.Cartoons;
        return kind == MediaWorkKind.Movie ? RecommendationTab.Movies : RecommendationTab.Series;
    }

    /// <summary>The kinds of work <paramref name="tab"/> holds: films, shows, or both.</summary>
    public static IReadOnlyList<MediaWorkKind> KindsOf(RecommendationTab tab) => tab switch
    {
        RecommendationTab.Movies => [MediaWorkKind.Movie],
        RecommendationTab.Series => [MediaWorkKind.Series],
        RecommendationTab.Cartoons or RecommendationTab.Documentaries => [MediaWorkKind.Movie, MediaWorkKind.Series],
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Not a recommendation tab."),
    };

    /// <summary>
    /// Whether any of <paramref name="tabs"/> holds works of <paramref name="kind"/>: films are wanted by
    /// Movies, Cartoons or Documentaries, shows by Series, Cartoons or Documentaries. A kind no tab wants is
    /// never asked of the catalog.
    /// </summary>
    public static bool Wants(IEnumerable<RecommendationTab> tabs, MediaWorkKind kind)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        return tabs.Any(tab => KindsOf(tab).Contains(kind));
    }
}
