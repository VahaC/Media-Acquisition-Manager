namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — the signed-in user's own recommendation settings (Vasyl's decision 5): the ones they saved, or the
/// defaults — every tab on, 60 searches — until they do.</summary>
/// <param name="Movies">Whether the Movies tab is on.</param>
/// <param name="Series">Whether the Series tab is on.</param>
/// <param name="Cartoons">Whether the Cartoons tab is on.</param>
/// <param name="Documentaries">Whether the Documentaries tab is on.</param>
/// <param name="DailySearchLimit">How many release-source searches the app may run for the user's lists in 24 hours;
/// 0 runs none.</param>
/// <param name="SearchesLast24Hours">How many it ran for them in the last 24 hours.</param>
public sealed record RecommendationSettingsDto(
    bool Movies,
    bool Series,
    bool Cartoons,
    bool Documentaries,
    int DailySearchLimit,
    int SearchesLast24Hours);
