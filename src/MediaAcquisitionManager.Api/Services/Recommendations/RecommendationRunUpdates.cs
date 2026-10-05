using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — the one way a recommendation run is marked failed — by the service (a full queue), the runner, the
/// worker and the reconciler alike, on the <see cref="FileInspectionRunUpdates"/> pattern.
/// </summary>
/// <remarks>
/// A conditional <c>UPDATE</c>: the caller narrows <c>runs</c> to the run and the statuses it may still leave, so
/// two writers racing to finish a run can't overwrite each other's outcome. Never cancelled: once an outcome is
/// decided it is written, even while the app shuts down.
/// </remarks>
public static class RecommendationRunUpdates
{
    /// <summary>
    /// Marks every run of <paramref name="runs"/> failed at <paramref name="finishedUtc"/> with
    /// <paramref name="error"/>, cut to fit its column; how many runs it marked.
    /// </summary>
    public static Task<int> MarkFailedAsync(this IQueryable<RecommendationRunEntity> runs, string error, DateTime finishedUtc)
    {
        var storedError = FileInspectionRunUpdates.Cut(error, RecommendationRunEntity.ErrorMaximumLength);
        return runs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(run => run.Status, RecommendationRunStatus.Failed)
                .SetProperty(run => run.FinishedUtc, finishedUtc)
                .SetProperty(run => run.Error, storedError)
                // A failed run is doing nothing any more: the stage it was in goes with it (MAM-33).
                .SetProperty(run => run.Stage, (RecommendationRunStage?)null)
                .SetProperty(run => run.StageDone, (int?)null)
                .SetProperty(run => run.StageTotal, (int?)null),
            CancellationToken.None);
    }
}
