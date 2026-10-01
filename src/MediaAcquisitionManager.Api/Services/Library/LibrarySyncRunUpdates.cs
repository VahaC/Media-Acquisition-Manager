using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// The one way a library sync run is marked failed — by the start (a full queue), the runner, the
/// worker and the reconciler alike (MAM-8).
/// </summary>
/// <remarks>
/// <para>
/// A conditional <c>UPDATE</c>: the caller narrows <c>runs</c> to the run and to the statuses it may
/// still leave (<c>Running</c>, say), so two writers racing to finish one run can't overwrite each
/// other's outcome, and a finished run is never reopened.
/// </para>
/// <para>
/// Never cancelled: once an outcome is decided it is written, even while the app shuts down. A cancelled
/// write would leave the run under way until the next start, whose reconciler would then blame a restart
/// for what was really, say, a refused key.
/// </para>
/// </remarks>
public static class LibrarySyncRunUpdates
{
    /// <summary>
    /// Marks every run of <paramref name="runs"/> failed at <paramref name="finishedUtc"/> with
    /// <paramref name="error"/>, cut to fit its column; how many runs it marked.
    /// </summary>
    public static Task<int> MarkFailedAsync(this IQueryable<LibrarySyncRunEntity> runs, string error, DateTime finishedUtc)
    {
        var storedError = Cut(error);
        return runs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(run => run.Status, LibrarySyncStatus.Failed)
                .SetProperty(run => run.FinishedUtc, finishedUtc)
                .SetProperty(run => run.Error, storedError),
            CancellationToken.None);
    }

    // A sentence past the column is cut with an ellipsis, so the reader sees it was cut. The column's
    // own constant, so the cut and the column can't drift apart.
    private static string Cut(string error) =>
        error.Length <= LibrarySyncRunEntity.ErrorMaximumLength
            ? error
            : string.Concat(error.AsSpan(0, LibrarySyncRunEntity.ErrorMaximumLength - 1), "…");
}
