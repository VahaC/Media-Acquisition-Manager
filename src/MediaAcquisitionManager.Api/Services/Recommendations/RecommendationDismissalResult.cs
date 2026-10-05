namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationDismissalService"/>'s calls.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">The sentence when <see cref="RecommendationDismissalStatus.Invalid"/>.</param>
public sealed record RecommendationDismissalResult(RecommendationDismissalStatus Status, string? Error)
{
    public static RecommendationDismissalResult Done { get; } = new(RecommendationDismissalStatus.Done, null);

    public static RecommendationDismissalResult Invalid(string error) => new(RecommendationDismissalStatus.Invalid, error);
}
