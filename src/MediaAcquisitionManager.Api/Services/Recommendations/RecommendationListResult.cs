using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationListService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="RecommendationListStatus.Listed"/>.</param>
/// <param name="Error">The sentence otherwise.</param>
public sealed record RecommendationListResult(RecommendationListStatus Status, RecommendationListResponse? List, string? Error)
{
    public static RecommendationListResult TabSwitchedOff { get; } =
        new(RecommendationListStatus.TabSwitchedOff, null, RecommendationListRules.TabSwitchedOffError);

    public static RecommendationListResult Listed(RecommendationListResponse list) => new(RecommendationListStatus.Listed, list, null);

    public static RecommendationListResult Invalid(string error) => new(RecommendationListStatus.Invalid, null, error);
}
