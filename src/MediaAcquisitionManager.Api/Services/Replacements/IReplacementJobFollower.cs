namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — brings each scheduled replacement in line with its download job (ADR 0030–0034): it mirrors the download
/// until the download is done and leaves it there — MAM-25's <see cref="IReplacementVerifier"/> takes it up, and a job
/// being checked or verified is never followed —; it stops when the download fails, is cancelled or can't be told apart
/// in the client.
/// </summary>
/// <remarks>
/// <para>
/// R6 — a state past <c>Scheduled</c> is mirrored only for a download the strict check let through
/// (<c>DownloadOverwriteCheck.Checked</c>, written before the one submission): a download that left <c>Created</c>
/// without it was adopted by the poller after the app stopped in the middle of its send, before anything checked where
/// its files land — its "new" file may be the old copy itself —, so its replacement fails rather than follow it.
/// </para>
/// <para>
/// Nothing is kept in memory: every call reads the rows, so a restart resumes where they are. Every write is conditional
/// on the state read (<see cref="ReplacementJobUpdates"/>), so a cancel that gets there first is never overwritten. No
/// audit row for these mirrored steps: the download's own rows and the replacement's state say them.
/// </para>
/// </remarks>
public interface IReplacementJobFollower
{
    /// <summary>
    /// Brings every replacement still followed (<c>ReplacementJobStates.Followed</c>) in line with its download, each on its
    /// own: one that breaks is logged and the others go on. Only a cancellation by the caller propagates.
    /// <c>DownloadJobTracker</c> calls it after each pass of the download jobs.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings the replacement of the download job <paramref name="downloadJobId"/>, if it has one still followed, in line
    /// with it: what a schedule calls right after its send, so its answer already says how the replacement stands.
    /// </summary>
    Task SyncAsync(Guid downloadJobId, CancellationToken cancellationToken = default);
}
