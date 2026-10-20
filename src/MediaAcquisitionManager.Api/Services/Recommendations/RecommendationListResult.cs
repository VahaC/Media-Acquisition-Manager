using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationListService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="RecommendationListStatus.Listed"/>.</param>
/// <param name="Error">Why not otherwise, as a message the Api renders (ADR 0042).</param>
public sealed record RecommendationListResult(RecommendationListStatus Status, RecommendationListResponse? List, Message? Error)
{
    public static RecommendationListResult TabSwitchedOff { get; } =
        new(RecommendationListStatus.TabSwitchedOff, null, RecommendationListRules.TabSwitchedOffError);

    public static RecommendationListResult Listed(RecommendationListResponse list) => new(RecommendationListStatus.Listed, list, null);

    public static RecommendationListResult Invalid(Message error) => new(RecommendationListStatus.Invalid, null, error);
}
