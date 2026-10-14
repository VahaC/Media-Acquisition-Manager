using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>The outcome of <see cref="IRecommendationOverviewService.GetAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Overview">The answer when <see cref="RecommendationOverviewStatus.Found"/>.</param>
/// <param name="Error">The sentence when <see cref="RecommendationOverviewStatus.Invalid"/>,
/// <see cref="RecommendationOverviewStatus.NoCatalog"/> or <see cref="RecommendationOverviewStatus.Failed"/>.</param>
public sealed record RecommendationOverviewResult(RecommendationOverviewStatus Status, RecommendationOverviewResponse? Overview, string? Error)
{
    public static RecommendationOverviewResult NotFound { get; } = new(RecommendationOverviewStatus.NotFound, null, null);

    public static RecommendationOverviewResult Found(string? overview, string? language) =>
        new(RecommendationOverviewStatus.Found, new RecommendationOverviewResponse(overview, language), null);

    public static RecommendationOverviewResult Invalid(string error) => new(RecommendationOverviewStatus.Invalid, null, error);

    public static RecommendationOverviewResult NoCatalog(string error) => new(RecommendationOverviewStatus.NoCatalog, null, error);

    public static RecommendationOverviewResult Failed(string error) => new(RecommendationOverviewStatus.Failed, null, error);
}
