using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — one user's recommendation settings as they hold (Vasyl's decision 5): the row they saved, or
/// <see cref="RecommendationSettingsRules.Default"/> when they never did.
/// </summary>
/// <param name="Movies">Whether the Movies tab is on.</param>
/// <param name="Series">Whether the Series tab is on.</param>
/// <param name="Cartoons">Whether the Cartoons tab is on.</param>
/// <param name="Documentaries">Whether the Documentaries tab is on.</param>
/// <param name="DailySearchLimit">How many release-source searches the app may run for the user's lists in
/// 24 hours; 0 runs none.</param>
public sealed record RecommendationSettings(bool Movies, bool Series, bool Cartoons, bool Documentaries, int DailySearchLimit)
{
    /// <summary>The tabs that are on, in the page's order.</summary>
    public IReadOnlyList<RecommendationTab> Tabs =>
        [.. RecommendationTabs.All.Where(IsOn)];

    /// <summary>Whether <paramref name="tab"/> is on.</summary>
    public bool IsOn(RecommendationTab tab) => tab switch
    {
        RecommendationTab.Movies => Movies,
        RecommendationTab.Series => Series,
        RecommendationTab.Cartoons => Cartoons,
        RecommendationTab.Documentaries => Documentaries,
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Not a recommendation tab."),
    };
}
