using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A release sent to a download client, and how it went (MAM-13).</summary>
/// <remarks>
/// MAM-13 — the client's and the destination's names as they were when the job was made, so a job reads
/// the same after either is removed. Nothing that locates the payload: the release's download link stays
/// on the server (ADR 0004).
/// </remarks>
/// <param name="Id">The job's id.</param>
/// <param name="ReleaseId">The release snapshot that was sent.</param>
/// <param name="ReleaseTitle">Its title as listed.</param>
/// <param name="SourceName">The source that listed it.</param>
/// <param name="DownloadClientName">The download client it went to.</param>
/// <param name="DestinationName">The destination it went to; <c>null</c> only for a job of an older
/// build.</param>
/// <param name="ClientDestinationKey">The category that was sent.</param>
/// <param name="Status">Where the job stands: <c>Created</c> while it is being sent (or when how that went
/// isn't known), <c>Submitted</c> once the client took it, <c>NeedsLinking</c> when the client took it but
/// which of its downloads it became has to be picked by hand, <c>Failed</c> with <paramref name="Error"/>.</param>
/// <param name="ClientJobId">The client's id of the download (an info hash for qBittorrent); <c>null</c>
/// while the client hasn't listed it.</param>
/// <param name="Error">Why it failed, every secret masked; <c>null</c> unless it failed.</param>
/// <param name="CreatedUtc">When it was sent.</param>
/// <param name="StatusChangedUtc">When <paramref name="Status"/> last changed.</param>
public sealed record DownloadJobDto(
    Guid Id,
    Guid ReleaseId,
    string ReleaseTitle,
    string SourceName,
    string DownloadClientName,
    string? DestinationName,
    string? ClientDestinationKey,
    DownloadJobStatus Status,
    string? ClientJobId,
    string? Error,
    DateTime CreatedUtc,
    DateTime StatusChangedUtc);
