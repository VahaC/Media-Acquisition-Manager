using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// Sending a release the person picked to a download client (MAM-13): what it can be sent to, the sending
/// itself, and the job it made. Every signed-in user sends (MAM-13's decision 1), and only a release of one
/// of their own searches from the last day — a search is its creator's (ADR 0011). MAM-14 — and the jobs
/// page: the caller's jobs, a job's files, and stopping following one.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// The Download dialog's choices for the release <paramref name="releaseId"/>: every switched-on
    /// download client of a type this build can send to that takes its payload and has a destination, with
    /// its destinations, by name — and (MAM-32) the destination to pick first, by the release's tracker
    /// section (<c>DestinationSuggestion</c>). <see cref="DownloadOptionsStatus.Invalid"/> when the id is
    /// missing or malformed; <see cref="DownloadOptionsStatus.ReleaseMissing"/> when the release isn't the
    /// caller's. No client or source is asked.
    /// </summary>
    Task<DownloadOptionsResult> GetOptionsAsync(string? releaseId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-38, ADR 0034 — whether a release sent to the destination <paramref name="destinationId"/> is checked for
    /// files it would write over, or why its folder can't be: the destination's client is asked where its category
    /// saves, and the Media folders are read. Every signed-in user's, as the dialog is; a reason, never a path.
    /// </summary>
    Task<DownloadOverwriteCheckResult> GetOverwriteCheckAsync(string? destinationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the release to the client's destination at most once per request id (ADR 0013): the same
    /// request again answers the job it made, sending nothing; a new request is checked — the release the
    /// caller's, the client a switched-on download client that takes the payload, the destination the
    /// client's, the source still there to fetch from, the release not already sent to the client — and then
    /// written as a job with its audit row before anything is fetched or sent, and sent
    /// (<see cref="IDownloadSender"/>). A refused request writes and sends nothing. MAM-24 — a request id that scheduled
    /// a replacement answers a plain send as used for something else.
    /// </summary>
    Task<DownloadSendResult> SendAsync(SendDownloadRequest request, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-24 — sends the release as <see cref="SendAsync"/> does, scheduling the replacement of the old copies the owner
    /// ticked (ADR 0030–0034): the same checks, then the ticks planned against the release's works from the database
    /// and the disk alone (<c>IReplacementPlanner</c>) — <see cref="DownloadSendStatus.ReplacementInvalid"/> or
    /// <see cref="DownloadSendStatus.ReplacementUnavailable"/>, nothing written, when they don't fit or can't be replaced
    /// —, then the job, the replacement with its copies' fresh identities and its works' ids, and their audit rows in one
    /// save before anything is fetched or sent, then the send in strict mode, and the replacement brought in line with
    /// how it went. The same request id answers its job only with the same ticks, as sets; a request id a Keep both send
    /// used, or the other way round, is <see cref="DownloadSendStatus.RequestIdReused"/>. Who may call it is the route's
    /// to decide: the installation owner alone.
    /// </summary>
    Task<DownloadSendResult> ScheduleReplacementAsync(ScheduleReplacementRequest request, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>The caller's job <paramref name="jobId"/>; <c>null</c> when there is none or it is another
    /// user's.</summary>
    Task<DownloadJobDto?> GetAsync(Guid jobId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-14 — one page of the caller's jobs, newest first, with <paramref name="status"/> (a status by name, in
    /// any letter case; blank for every one) applied by the server. The caller's own only — the owner's too
    /// (MAM-14's decision 1). <see cref="DownloadJobListStatus.Invalid"/> with the sentence for a page, a page
    /// size or a status the list doesn't take. No client is asked: the rows are what the poller last read.
    /// MAM-41 — and only the jobs of the Jobs page's tab <paramref name="view"/> (a <c>DownloadJobView</c> by name, in any
    /// letter case; blank for every job), with the status when both are given; <see cref="DownloadJobListStatus.Invalid"/>
    /// for a view that isn't one. The answer counts the caller's jobs that need them and that the app still works on,
    /// whatever the view, the status and the page: the tabs' labels.
    /// </summary>
    Task<DownloadJobListResult> ListAsync(
        string? status, string? view, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-14 — one page of the files of the caller's job <paramref name="jobId"/>, in the client's order, as the
    /// poller last read them. <see cref="DownloadJobFileListStatus.NotFound"/> when there is no such job or it is
    /// another user's; <see cref="DownloadJobFileListStatus.Invalid"/> for a page or a page size the list doesn't
    /// take.
    /// </summary>
    Task<DownloadJobFileListResult> ListFilesAsync(Guid jobId, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-14 — stops following the caller's job <paramref name="jobId"/>: it becomes <c>Cancelled</c>, with its
    /// audit row, in one transaction. Nothing is sent to the client — the torrent and its files stay there as
    /// they are (ADR 0014) — and the release may be sent again. <see cref="DownloadCancelStatus.NotFound"/> when
    /// there is no such job or it is another user's; <see cref="DownloadCancelStatus.NothingToStop"/>, changing
    /// nothing, when it already ended; <see cref="DownloadCancelStatus.StillBeingSent"/>, changing nothing, when
    /// it is <c>Created</c> and its send may still be under way.
    /// </summary>
    Task<DownloadCancelResult> CancelAsync(Guid jobId, Guid callerUserId, CancellationToken cancellationToken = default);
}
