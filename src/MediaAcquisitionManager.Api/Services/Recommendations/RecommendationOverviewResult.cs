using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>The outcome of <see cref="IRecommendationOverviewService.GetAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Overview">The answer when <see cref="RecommendationOverviewStatus.Found"/>.</param>
/// <param name="Error">Why, when <see cref="RecommendationOverviewStatus.Invalid"/>,
/// <see cref="RecommendationOverviewStatus.NoCatalog"/> or <see cref="RecommendationOverviewStatus.Failed"/>, as a message
/// the Api renders (ADR 0042).</param>
public sealed record RecommendationOverviewResult(RecommendationOverviewStatus Status, RecommendationOverviewResponse? Overview, Message? Error)
{
    public static RecommendationOverviewResult NotFound { get; } = new(RecommendationOverviewStatus.NotFound, null, null);

    public static RecommendationOverviewResult Found(string? overview, string? language) =>
        new(RecommendationOverviewStatus.Found, new RecommendationOverviewResponse(overview, language), null);

    public static RecommendationOverviewResult Invalid(Message error) => new(RecommendationOverviewStatus.Invalid, null, error);

    public static RecommendationOverviewResult NoCatalog(Message error) => new(RecommendationOverviewStatus.NoCatalog, null, error);

    public static RecommendationOverviewResult Failed(Message error) => new(RecommendationOverviewStatus.Failed, null, error);
}
