namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// Runs one queued library sync to its end (MAM-8, ADR 0008): claims the run, tests the connection (the
/// version gate), reads the libraries and every page of items, then replaces the integration's snapshot
/// — or fails the run with a readable sentence, leaving the stored snapshot exactly as it was.
/// </summary>
/// <remarks>
/// Expected failures (a refused key, a server too old, a library that changed while it was read) end
/// the run as failed and return normally. Only a cancellation by the caller propagates, leaving the run
/// running for the reconciler to settle at the next start; anything else that throws is a bug, which
/// the worker logs and fails the run for.
/// </remarks>
public interface ILibrarySyncRunner
{
    /// <summary>
    /// Runs the sync <paramref name="runId"/> when it is still queued; a run another worker claimed, or
    /// one already finished, is left alone.
    /// </summary>
    Task RunAsync(Guid runId, CancellationToken cancellationToken = default);
}
