using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationDismissalService"/>'s calls.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">Why, when <see cref="RecommendationDismissalStatus.Invalid"/>, as a message the Api renders (ADR
/// 0042).</param>
public sealed record RecommendationDismissalResult(RecommendationDismissalStatus Status, Message? Error)
{
    public static RecommendationDismissalResult Done { get; } = new(RecommendationDismissalStatus.Done, null);

    public static RecommendationDismissalResult Invalid(Message error) => new(RecommendationDismissalStatus.Invalid, error);
}
