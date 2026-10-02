namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-14 — one pass over the download jobs still followed: each read from its client and its row brought in
/// line with what the client says (ADR 0014). <see cref="DownloadJobTracker"/> runs a pass every
/// <see cref="DownloadJobTracker.PollInterval"/>; tests run one by hand, as the test host runs no hosted
/// services.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is kept in memory between passes: every pass reads the followed jobs from the database, so a
/// restart resumes where the rows are, and no reconciler is needed. The client is only read — the contract
/// has no member that removes, stops or pauses anything (<c>DownloadClientNeverRemovesTests</c>).
/// </para>
/// <para>
/// Every write is conditional on the status the pass read (<see cref="DownloadJobUpdates"/>), so a cancel or a
/// send that gets to a job first is never overwritten.
/// </para>
/// </remarks>
public interface IDownloadJobPoller
{
    /// <summary>
    /// Follows every followed job once: <c>Submitted</c>, <c>Downloading</c> and <c>Paused</c> ones, and a
    /// <c>Created</c> one whose sending was interrupted (<see cref="DownloadJobPoller.InterruptedSendAge"/>).
    /// One client that can't be asked, or that breaks, never stops the others. Only a cancellation by the
    /// caller propagates.
    /// </summary>
    Task PollAsync(CancellationToken cancellationToken = default);
}
