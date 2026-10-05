namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — the signed-in user's recommendation settings to save from their Profile; every field is
/// required.</summary>
/// <remarks>
/// Every field nullable and without <c>[Required]</c> on purpose, as in <see cref="ChangePasswordRequest"/>: a
/// non-nullable <c>bool</c> would read a missing field as <c>false</c> and quietly switch a tab off, and
/// <c>[Required]</c> would stop it at a validation body with no <c>error</c> in it. A missing field reaches the
/// service, whose sentence the Profile page shows.
/// </remarks>
/// <param name="Movies">Whether the Movies tab is on.</param>
/// <param name="Series">Whether the Series tab is on.</param>
/// <param name="Cartoons">Whether the Cartoons tab is on.</param>
/// <param name="Documentaries">Whether the Documentaries tab is on.</param>
/// <param name="DailySearchLimit">How many release-source searches the app may run for the user's lists in 24 hours, a
/// whole number from 0 (none) to 500.</param>
public sealed record RecommendationSettingsRequest(
    bool? Movies,
    bool? Series,
    bool? Cartoons,
    bool? Documentaries,
    int? DailySearchLimit);
