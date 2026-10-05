using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — what a running run tells the Recommendations page it is doing (Vasyl asked to see more than "Computing…"):
// its stage, and how far into a counted stage it is, written on the run's row.
public sealed partial class RecommendationRunner
{
    /// <summary>
    /// The least time between two writes of a stage's count: the page polls the run every two seconds or so, and a
    /// write more often is one nobody reads, while a write per item would put a database write behind each of a large
    /// library's thousands of catalog reads — most of them answered from the cache in a millisecond. A stage's
    /// beginning and its end are written whatever the time.
    /// </summary>
    internal static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Begins <paramref name="stage"/> and writes it at once, counted in <paramref name="total"/> items — or not
    /// counted, <c>null</c>. A counted stage with nothing to count is passed over: the page keeps showing the stage
    /// before rather than "0 of 0".
    /// </summary>
    private Task BeginStageAsync(Computation run, RecommendationRunStage stage, int? total, CancellationToken cancellationToken)
    {
        if (total == 0)
            return Task.CompletedTask;

        run.Progress.Begin(stage, total);
        return WriteProgressAsync(run.Progress, cancellationToken);
    }

    /// <summary>
    /// <paramref name="count"/> more items of the counted stage done: written once the stage is done, and before that
    /// only when <see cref="ProgressInterval"/> has passed since the last write.
    /// </summary>
    private Task AdvanceStageAsync(Computation run, CancellationToken cancellationToken, int count = 1)
    {
        var progress = run.Progress;
        if (progress.Total is not { } total)
            return Task.CompletedTask;

        progress.Done += count;
        // Elapsed by the clock's timestamps, never its wall time: a wall clock the system sets back can't stop the
        // writes, nor one set forward hurry them (the TmdbRequestPacer rule).
        if (progress.Done < total && time.GetElapsedTime(progress.WrittenAt) < ProgressInterval)
            return Task.CompletedTask;
        return WriteProgressAsync(progress, cancellationToken);
    }

    /// <summary>
    /// Writes <paramref name="progress"/> on the run's row. Conditional on Running, so a run something else finished
    /// (the reconciler) is never written on again — the library sync's <c>ReportProgressAsync</c> shape. Not terminal,
    /// so it is cancelled with the run.
    /// </summary>
    /// <remarks>
    /// What the page shows of a run never decides the run: a write that fails — the database busy past its timeout —
    /// is logged and the computation goes on, its next write or its end putting the row right.
    /// </remarks>
    private async Task WriteProgressAsync(StageProgress progress, CancellationToken cancellationToken)
    {
        progress.WrittenAt = time.GetTimestamp();
        RecommendationRunStage? stage = progress.Stage;
        int? done = progress.Total is null ? null : progress.Done;
        var total = progress.Total;
        try
        {
            await db.RecommendationRuns
                .Where(row => row.Id == progress.RunId && row.Status == RecommendationRunStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.Stage, stage)
                        .SetProperty(row => row.StageDone, done)
                        .SetProperty(row => row.StageTotal, total),
                    cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Recommendation run {RunId} couldn't write its progress; it computes on.", progress.RunId);
        }
    }

    /// <summary>The stage a run is in, how far into it, and when that was last written.</summary>
    private sealed class StageProgress(Guid runId)
    {
        public Guid RunId { get; } = runId;

        /// <summary>The stage; <c>null</c> before the first.</summary>
        public RecommendationRunStage? Stage { get; private set; }

        /// <summary>The items of a counted stage done so far.</summary>
        public int Done { get; set; }

        /// <summary>The items of a counted stage in all; <c>null</c> for a stage that isn't counted.</summary>
        public int? Total { get; private set; }

        /// <summary>The clock's timestamp of the last write.</summary>
        public long WrittenAt { get; set; }

        public void Begin(RecommendationRunStage stage, int? total)
        {
            Stage = stage;
            Total = total;
            Done = 0;
        }
    }
}
