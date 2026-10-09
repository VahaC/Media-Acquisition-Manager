using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// The writes the sending makes to its job after the job is created (MAM-13), each a conditional
/// <c>UPDATE</c> of a job still <see cref="DownloadJobStatus.Created"/>: a job whose outcome is written is
/// never written again, whoever else reaches it — the poller following the client, or a cancel.
/// </summary>
/// <remarks>
/// <para>
/// Never cancelled, the <c>LibrarySyncRunUpdates</c> rule: once an outcome is decided it is written, even
/// while the app shuts down, or a job the client took would read as still being sent.
/// </para>
/// <para>
/// MAM-14 — and the writes that follow a job in its client and cancel it, each conditional on the status its
/// writer read: whoever writes first wins, and the other's write, made against a status the job no longer
/// has, changes nothing. So a cancel racing a pass of the poller is never undone by it, and a job the sender
/// is still sending is never moved by the poller nor cancelled (ADR 0014).
/// </para>
/// </remarks>
public static class DownloadJobUpdates
{
    /// <summary>MAM-14 — the statuses <see cref="DownloadJobFollowing.CanCancel"/> allows, for the database to
    /// check.</summary>
    private static readonly DownloadJobStatus[] CancellableStatuses =
        [.. Enum.GetValues<DownloadJobStatus>().Where(DownloadJobFollowing.CanCancel)];

    /// <summary>Records the info hashes the app worked out from the payload, before the payload is sent.</summary>
    public static Task<int> RecordInfoHashesAsync(this IQueryable<DownloadJobEntity> jobs, Guid jobId, TorrentInfoHashes hashes) =>
        jobs.Where(job => job.Id == jobId && job.Status == DownloadJobStatus.Created)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.InfoHashV1, hashes.V1)
                    .SetProperty(job => job.InfoHashV2, hashes.V2),
                CancellationToken.None);

    /// <summary>
    /// MAM-24 — records what the release turned out to be once fetched — a .torrent file or a magnet link —, the payload
    /// type specification §4 asks a replacement to keep, which a gateway's link names only now. Every send's, Keep both
    /// too: it is a fact of the download, whatever it carries.
    /// </summary>
    public static Task<int> RecordPayloadKindAsync(this IQueryable<DownloadJobEntity> jobs, Guid jobId, ReleasePayloadKind kind) =>
        jobs.Where(job => job.Id == jobId && job.Status == DownloadJobStatus.Created)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.PayloadKind, kind), CancellationToken.None);

    /// <summary>MAM-38 — records how the release was checked for files it would write over, before it is
    /// sent.</summary>
    public static Task<int> RecordOverwriteCheckAsync(this IQueryable<DownloadJobEntity> jobs, Guid jobId, DownloadOverwriteCheck check) =>
        jobs.Where(job => job.Id == jobId && job.Status == DownloadJobStatus.Created)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.OverwriteCheck, check), CancellationToken.None);

    /// <summary>
    /// Writes the job's outcome at <paramref name="changedUtc"/>: its status, the client's id of the
    /// download, and the error, cut to fit its column, with whose sentence it is; how many jobs it wrote (0
    /// or 1).
    /// </summary>
    public static Task<int> FinishAsync(
        this IQueryable<DownloadJobEntity> jobs,
        Guid jobId,
        DownloadJobStatus status,
        string? clientJobId,
        string? error,
        DownloadJobErrorOrigin? errorOrigin,
        DateTime changedUtc)
    {
        var storedError = error is null ? null : Cut(error);
        return jobs.Where(job => job.Id == jobId && job.Status == DownloadJobStatus.Created)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, status)
                    .SetProperty(job => job.ClientJobId, clientJobId)
                    .SetProperty(job => job.Error, storedError)
                    .SetProperty(job => job.ErrorOrigin, errorOrigin)
                    .SetProperty(job => job.StatusChangedUtc, changedUtc),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-14 — writes what one look at the client said about the followed job <paramref name="jobId"/>, read
    /// as <paramref name="observedStatus"/>: its status, its progress, the client's current id of it (a client
    /// can rename a job), the v2 info hash the client lists for a job sent without one, and why it isn't moving
    /// on, if anything blocks it. <see cref="DownloadJobEntity.StatusChangedUtc"/> becomes
    /// <paramref name="changedUtc"/> only when the status changes. How many jobs it wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordObservationAsync(
        this IQueryable<DownloadJobEntity> jobs,
        Guid jobId,
        DownloadJobStatus observedStatus,
        DownloadJobStatus status,
        double? progress,
        string clientJobId,
        string? infoHashV2,
        string? blockReason,
        DownloadJobErrorOrigin? blockReasonOrigin,
        DateTime changedUtc)
    {
        var storedReason = Stored(blockReason);
        return jobs.Where(job => job.Id == jobId && job.Status == observedStatus)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters
                        .SetProperty(job => job.Status, status)
                        .SetProperty(job => job.Progress, progress)
                        .SetProperty(job => job.ClientJobId, clientJobId)
                        .SetProperty(job => job.InfoHashV2, infoHashV2)
                        .SetProperty(job => job.BlockReason, storedReason)
                        .SetProperty(job => job.BlockReasonOrigin, blockReasonOrigin);
                    if (status != observedStatus)
                        setters.SetProperty(job => job.StatusChangedUtc, changedUtc);
                },
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-14 — claims the followed job <paramref name="jobId"/>, read as <paramref name="observedStatus"/>, for
    /// a write that goes with it in the caller's transaction — its files — and changes nothing: an
    /// <c>UPDATE</c> of its status to itself. A write, so it holds SQLite's write lock until the caller commits,
    /// and counts the job only while it still has the status the pass read: a cancel either landed first, and
    /// the files aren't written, or waits for them. How many jobs it claimed (0 or 1).
    /// </summary>
    public static Task<int> ClaimAsync(this IQueryable<DownloadJobEntity> jobs, Guid jobId, DownloadJobStatus observedStatus) =>
        jobs.Where(job => job.Id == jobId && job.Status == observedStatus)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.Status, observedStatus), CancellationToken.None);

    /// <summary>
    /// MAM-14 — writes why the followed job <paramref name="jobId"/>, read as <paramref name="observedStatus"/>,
    /// isn't moving on — or, given <c>null</c>, that nothing blocks it — with whose sentence it is. Nothing else
    /// changes: a reason is never an end (MAM-14's decisions 2 and 3). How many jobs it wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordBlockReasonAsync(
        this IQueryable<DownloadJobEntity> jobs,
        Guid jobId,
        DownloadJobStatus observedStatus,
        string? blockReason,
        DownloadJobErrorOrigin? blockReasonOrigin)
    {
        var storedReason = Stored(blockReason);
        return jobs.Where(job => job.Id == jobId && job.Status == observedStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.BlockReason, storedReason)
                    .SetProperty(job => job.BlockReasonOrigin, blockReasonOrigin),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-14 — ends the following of the job <paramref name="jobId"/>, read as <paramref name="observedStatus"/>,
    /// at <paramref name="changedUtc"/>: <see cref="DownloadJobStatus.Failed"/> with the app's sentence in
    /// <paramref name="error"/>, or <see cref="DownloadJobStatus.NeedsLinking"/> with none. Its block reason
    /// goes: an ended job has nothing left to be blocked on. How many jobs it wrote (0 or 1).
    /// </summary>
    public static Task<int> StopFollowingAsync(
        this IQueryable<DownloadJobEntity> jobs,
        Guid jobId,
        DownloadJobStatus observedStatus,
        DownloadJobStatus status,
        string? error,
        DateTime changedUtc)
    {
        var storedError = Stored(error);
        DownloadJobErrorOrigin? errorOrigin = error is null ? null : DownloadJobErrorOrigin.App;
        return jobs.Where(job => job.Id == jobId && job.Status == observedStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, status)
                    .SetProperty(job => job.Error, storedError)
                    .SetProperty(job => job.ErrorOrigin, errorOrigin)
                    .SetProperty(job => job.BlockReason, (string?)null)
                    .SetProperty(job => job.BlockReasonOrigin, (DownloadJobErrorOrigin?)null)
                    .SetProperty(job => job.StatusChangedUtc, changedUtc),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-14 — cancels the job <paramref name="jobId"/> at <paramref name="changedUtc"/> when it may still be
    /// cancelled (<see cref="DownloadJobFollowing.CanCancel"/>), whatever status it has by then, and clears its
    /// block reason; the caller narrows <paramref name="jobs"/> to its own. How many jobs it wrote (0 or 1).
    /// Nothing is sent anywhere: cancelling only stops following (ADR 0014).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conditional on "still cancellable" rather than on the one status the caller read: a pass of the poller
    /// moving a job from <c>Submitted</c> to <c>Downloading</c> between that read and this write leaves it as
    /// cancellable as before, so the cancel still wins, in one statement; only a job that ended meanwhile — it
    /// finished, or another click cancelled it first — is left as it is.
    /// </para>
    /// <para>
    /// A <see cref="DownloadJobStatus.Created"/> job only once its send counts as interrupted — at least
    /// <see cref="DownloadJobPoller.InterruptedSendAge"/> old, the poller's own rule — and in the same
    /// statement: a cancel never races a <see cref="DownloadSender"/> still working, whose outcome, written
    /// only on a job still <c>Created</c>, it would otherwise throw away while the client keeps the torrent.
    /// </para>
    /// </remarks>
    public static Task<int> CancelAsync(this IQueryable<DownloadJobEntity> jobs, Guid jobId, DateTime changedUtc)
    {
        var interruptedBefore = changedUtc - DownloadJobPoller.InterruptedSendAge;
        return jobs.Where(job => job.Id == jobId
                && CancellableStatuses.Contains(job.Status)
                && (job.Status != DownloadJobStatus.Created || job.CreatedUtc <= interruptedBefore))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, DownloadJobStatus.Cancelled)
                    .SetProperty(job => job.BlockReason, (string?)null)
                    .SetProperty(job => job.BlockReasonOrigin, (DownloadJobErrorOrigin?)null)
                    .SetProperty(job => job.StatusChangedUtc, changedUtc),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-14 — <paramref name="text"/> as its column keeps it (<see cref="DownloadJobEntity.ErrorMaximumLength"/>,
    /// which <see cref="DownloadJobEntity.Error"/> and <see cref="DownloadJobEntity.BlockReason"/> share): the
    /// poller compares a new reason with the stored one as it would be stored, so a long one isn't written
    /// again on every pass.
    /// </summary>
    internal static string? Stored(string? text) => text is null ? null : Cut(text);

    // A sentence past the column is cut with an ellipsis, so the reader sees it was cut. The column's own
    // constant, so the cut and the column can't drift apart.
    private static string Cut(string error) =>
        error.Length <= DownloadJobEntity.ErrorMaximumLength
            ? error
            : string.Concat(error.AsSpan(0, DownloadJobEntity.ErrorMaximumLength - 1), "…");
}
