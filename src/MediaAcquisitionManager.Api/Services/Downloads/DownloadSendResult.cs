using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>What a request to send a release answered (MAM-13).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Job">The job made or found; <c>null</c> unless <see cref="DownloadSendStatus.Created"/> or
/// <see cref="DownloadSendStatus.Replayed"/>.</param>
/// <param name="Error">Why it was refused, as a message the Api renders (ADR 0042); <c>null</c> with a job.</param>
public sealed record DownloadSendResult(DownloadSendStatus Status, DownloadJobDto? Job, Message? Error)
{
    public static DownloadSendResult Created(DownloadJobDto job) => new(DownloadSendStatus.Created, job, null);

    public static DownloadSendResult Replayed(DownloadJobDto job) => new(DownloadSendStatus.Replayed, job, null);

    public static DownloadSendResult Refused(DownloadSendStatus status, Message error) => new(status, null, error);
}
