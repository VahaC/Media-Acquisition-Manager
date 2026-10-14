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
    /// <param name="clientDestinationKey">The destination's stored key: a category, or — MAM-30 — a folder as the client
    /// sees it (ADR 0039).</param>
    /// <param name="destinationName">MAM-30 — the destination's name: the label a folder client shows the torrent with,
    /// and what the app's sentences call the destination, as members read them and never see a folder (D13).</param>
    /// <param name="replacement">
    /// MAM-24 — the job carries a replacement of old copies, sent in strict mode (ADR 0032, decision 7): a release that
    /// turns out a magnet link once fetched, a category whose folder isn't inside a library the app knows, and a check for
    /// files already there that finds any file at all, or couldn't look at every place, each fail the whole job with the
    /// app's sentence, nothing handed to the client — never a quiet Keep both.
    /// </param>
    Task SendAsync(
        Guid jobId,
        ReleaseEntity release,
        IntegrationConnectionEntity source,
        IntegrationConnectionEntity client,
        string clientDestinationKey,
        string destinationName,
        bool replacement = false);
}
