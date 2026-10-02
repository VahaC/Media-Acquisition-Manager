using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// Sending a release the person picked to a download client (MAM-13): what it can be sent to, the sending
/// itself, and the job it made. Every signed-in user sends (MAM-13's decision 1), and only a release of one
/// of their own searches from the last day — a search is its creator's (ADR 0011).
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// The Download dialog's choices for the release <paramref name="releaseId"/>: every switched-on
    /// download client of a type this build can send to that takes its payload and has a destination, with
    /// its destinations, by name. <see cref="DownloadOptionsStatus.Invalid"/> when the id is missing or
    /// malformed; <see cref="DownloadOptionsStatus.ReleaseMissing"/> when the release isn't the caller's.
    /// No client is asked.
    /// </summary>
    Task<DownloadOptionsResult> GetOptionsAsync(string? releaseId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the release to the client's destination at most once per request id (ADR 0013): the same
    /// request again answers the job it made, sending nothing; a new request is checked — the release the
    /// caller's, the client a switched-on download client that takes the payload, the destination the
    /// client's, the source still there to fetch from, the release not already sent to the client — and then
    /// written as a job with its audit row before anything is fetched or sent, and sent
    /// (<see cref="IDownloadSender"/>). A refused request writes and sends nothing.
    /// </summary>
    Task<DownloadSendResult> SendAsync(SendDownloadRequest request, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>The caller's job <paramref name="jobId"/>; <c>null</c> when there is none or it is another
    /// user's.</summary>
    Task<DownloadJobDto?> GetAsync(Guid jobId, Guid callerUserId, CancellationToken cancellationToken = default);
}
