using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>What a request to send a release answered (MAM-13).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Job">The job made or found; <c>null</c> unless <see cref="DownloadSendStatus.Created"/> or
/// <see cref="DownloadSendStatus.Replayed"/>.</param>
/// <param name="Error">A readable sentence; <c>null</c> with a job.</param>
public sealed record DownloadSendResult(DownloadSendStatus Status, DownloadJobDto? Job, string? Error)
{
    public static DownloadSendResult Created(DownloadJobDto job) => new(DownloadSendStatus.Created, job, null);

    public static DownloadSendResult Replayed(DownloadJobDto job) => new(DownloadSendStatus.Replayed, job, null);

    public static DownloadSendResult Refused(DownloadSendStatus status, string error) => new(status, null, error);
}
