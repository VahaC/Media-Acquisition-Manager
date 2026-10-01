using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// The one way a file inspection run is marked failed — by the service (a full queue), the runner, the
/// worker and the reconciler alike (MAM-9), on the <c>LibrarySyncRunUpdates</c> pattern.
/// </summary>
/// <remarks>
/// A conditional <c>UPDATE</c>: the caller narrows <c>runs</c> to the run and the statuses it may still
/// leave, so two writers racing to finish a run can't overwrite each other's outcome. Never cancelled:
/// once an outcome is decided it is written, even while the app shuts down.
/// </remarks>
public static class FileInspectionRunUpdates
{
    /// <summary>
    /// Marks every run of <paramref name="runs"/> failed at <paramref name="finishedUtc"/> with
    /// <paramref name="error"/>, cut to fit its column; how many runs it marked.
    /// </summary>
    public static Task<int> MarkFailedAsync(this IQueryable<FileInspectionRunEntity> runs, string error, DateTime finishedUtc)
    {
        var storedError = Cut(error, FileInspectionRunEntity.ErrorMaximumLength);
        return runs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(run => run.Status, FileInspectionRunStatus.Failed)
                .SetProperty(run => run.FinishedUtc, finishedUtc)
                .SetProperty(run => run.Error, storedError),
            CancellationToken.None);
    }

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="maximumLength"/> with an ellipsis, so the reader
    /// sees it was cut; the column's own constant is passed, so the cut and the column can't drift apart.
    /// </summary>
    public static string Cut(string text, int maximumLength) =>
        text.Length <= maximumLength ? text : string.Concat(text.AsSpan(0, maximumLength - 1), "…");
}
