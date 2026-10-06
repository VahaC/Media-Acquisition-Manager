using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — what a running run tells the TMDb page: how many works it searches and how many it has done, written on the
// run's row (the MAM-33 progress shape).
public sealed partial class LibraryLinkSuggestionRunner
{
    /// <summary>
    /// The least time between two writes of the count: the page polls the run every two seconds or so, and a write more
    /// often is one nobody reads, while a write per work would put a database write behind each of hundreds of works —
    /// most of them kept from the cache in a millisecond. The count's start and its end are written whatever the time.
    /// </summary>
    internal static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    /// <summary>One more work done: written once all are done, and before that only when <see cref="ProgressInterval"/>
    /// has passed since the last write.</summary>
    private Task AdvanceAsync(Preparation run, CancellationToken cancellationToken)
    {
        // Elapsed by the clock's timestamps, never its wall time: a wall clock the system sets back can't stop the writes,
        // nor one set forward hurry them (the TmdbRequestPacer rule).
        if (run.Done < run.Total && time.GetElapsedTime(run.WrittenAt) < ProgressInterval)
            return Task.CompletedTask;
        return WriteProgressAsync(run, cancellationToken);
    }

    /// <summary>
    /// Writes the run's counts on its row. Conditional on Running, so a run something else finished (the reconciler) is
    /// never written on again. Not terminal, so it is cancelled with the run.
    /// </summary>
    /// <remarks>
    /// What the page shows of a run never decides the run: a write that fails — the database busy past its timeout — is
    /// logged and the run goes on, its next write or its end putting the row right.
    /// </remarks>
    private async Task WriteProgressAsync(Preparation run, CancellationToken cancellationToken)
    {
        run.WrittenAt = time.GetTimestamp();
        var total = run.Total;
        var done = run.Done;
        try
        {
            await db.LibraryLinkSuggestionRuns
                .Where(row => row.Id == run.RunId && row.Status == LibraryLinkSuggestionRunStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.WorksTotal, (int?)total)
                        .SetProperty(row => row.WorksDone, done),
                    cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "TMDb suggestion run {RunId} couldn't write its progress; it searches on.", run.RunId);
        }
    }
}
