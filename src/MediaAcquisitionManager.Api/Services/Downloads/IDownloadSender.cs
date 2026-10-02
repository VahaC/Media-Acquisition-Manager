using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// The sending of one job <see cref="IDownloadService"/> has just written (MAM-13): the payload fetched from
/// the release's source, the destination's category confirmed with the client, the client asked whether it
/// already has the torrent, the release submitted once, the client's job found, and the job finished with
/// how it went.
/// </summary>
/// <remarks>
/// No cancellation token, on purpose: once the job exists, the person closing the tab must not leave it
/// half-sent — a submission cancelled after the client took it would leave a download nobody records.
/// Every request its calls send is bounded by the integration clients' own timeouts (15 seconds each).
/// </remarks>
public interface IDownloadSender
{
    /// <summary>
    /// Sends <paramref name="release"/>, fetched from <paramref name="source"/>, to <paramref name="client"/>'s
    /// category <paramref name="clientDestinationKey"/>, and writes the outcome onto the job
    /// <paramref name="jobId"/> while it is still <c>Created</c>. Never throws on a failure of either
    /// service: that is the job's outcome. <paramref name="client"/> is read with its path mappings.
    /// </summary>
    Task SendAsync(
        Guid jobId,
        ReleaseEntity release,
        IntegrationConnectionEntity source,
        IntegrationConnectionEntity client,
        string clientDestinationKey);
}
