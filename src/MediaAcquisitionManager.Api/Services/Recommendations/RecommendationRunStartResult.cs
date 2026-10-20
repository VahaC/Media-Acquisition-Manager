using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationRunService.StartAsync"/> and
/// <see cref="IRecommendationRunService.EnsureQueuedAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="RecommendationRunStartStatus.Queued"/>, as the owner sees it.</param>
/// <param name="Error">Why it wasn't queued, as a message the Api renders (ADR 0042).</param>
public sealed record RecommendationRunStartResult(RecommendationRunStartStatus Status, RecommendationRunDto? Run, Message? Error)
{
    public static RecommendationRunStartResult Queued(RecommendationRunDto run) => new(RecommendationRunStartStatus.Queued, run, null);

    public static RecommendationRunStartResult NoCatalog(Message error) => new(RecommendationRunStartStatus.NoCatalog, null, error);

    public static RecommendationRunStartResult Unavailable(Message error) => new(RecommendationRunStartStatus.Unavailable, null, error);
}
