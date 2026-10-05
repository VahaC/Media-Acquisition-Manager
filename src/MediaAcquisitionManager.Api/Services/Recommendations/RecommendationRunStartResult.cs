using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationRunService.StartAsync"/> and
/// <see cref="IRecommendationRunService.EnsureQueuedAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="RecommendationRunStartStatus.Queued"/>, as the owner sees it.</param>
/// <param name="Error">The sentence when it wasn't queued.</param>
public sealed record RecommendationRunStartResult(RecommendationRunStartStatus Status, RecommendationRunDto? Run, string? Error)
{
    public static RecommendationRunStartResult Queued(RecommendationRunDto run) => new(RecommendationRunStartStatus.Queued, run, null);

    public static RecommendationRunStartResult NoCatalog(string error) => new(RecommendationRunStartStatus.NoCatalog, null, error);

    public static RecommendationRunStartResult Unavailable(string error) => new(RecommendationRunStartStatus.Unavailable, null, error);
}
