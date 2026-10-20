using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
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
    /// <paramref name="error"/>; how many runs it marked.
    /// </summary>
    /// <remarks>MAM-47 (ADR 0042) — stored as key and values, fitted into the column by its converter, so no sentence
    /// is cut here.</remarks>
    public static Task<int> MarkFailedAsync(this IQueryable<LibrarySyncRunEntity> runs, Message error, DateTime finishedUtc) =>
        runs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(run => run.Status, LibrarySyncStatus.Failed)
                .SetProperty(run => run.FinishedUtc, finishedUtc)
                .SetProperty(run => run.Error, error),
            CancellationToken.None);
}
