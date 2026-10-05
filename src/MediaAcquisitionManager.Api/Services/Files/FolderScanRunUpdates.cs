using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// The one way a scan of the media folders is marked failed — by the service (a full queue), the runner,
/// the worker and the reconciler alike (MAM-18), on the <c>FileInspectionRunUpdates</c> pattern.
/// </summary>
/// <remarks>
/// A conditional <c>UPDATE</c>: the caller narrows <c>runs</c> to the run and the statuses it may still
/// leave, so two writers racing to finish a run can't overwrite each other's outcome. Never cancelled:
/// once an outcome is decided it is written, even while the app shuts down.
/// </remarks>
public static class FolderScanRunUpdates
{
    /// <summary>
    /// Marks every run of <paramref name="runs"/> failed at <paramref name="finishedUtc"/> with
    /// <paramref name="error"/>, cut to fit its column; how many runs it marked.
    /// </summary>
    public static Task<int> MarkFailedAsync(this IQueryable<FolderScanRunEntity> runs, string error, DateTime finishedUtc)
    {
        var storedError = FileInspectionRunUpdates.Cut(error, FolderScanRunEntity.ErrorMaximumLength);
        return runs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(run => run.Status, FolderScanStatus.Failed)
                .SetProperty(run => run.FinishedUtc, finishedUtc)
                .SetProperty(run => run.Error, storedError),
            CancellationToken.None);
    }
}
