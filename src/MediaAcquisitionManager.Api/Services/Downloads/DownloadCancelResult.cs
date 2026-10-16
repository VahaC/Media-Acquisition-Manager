using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>What a request to stop following a download answered (MAM-14).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Job">The job, now cancelled, when <see cref="DownloadCancelStatus.Cancelled"/>.</param>
/// <param name="Error">The sentence saying why there was nothing to stop, when
/// <see cref="DownloadCancelStatus.NothingToStop"/>, or why not yet, when
/// <see cref="DownloadCancelStatus.StillBeingSent"/>.</param>
public sealed record DownloadCancelResult(DownloadCancelStatus Status, DownloadJobDto? Job, string? Error)
{
    public static DownloadCancelResult NotFound { get; } = new(DownloadCancelStatus.NotFound, null, null);

    public static DownloadCancelResult Cancelled(DownloadJobDto job) => new(DownloadCancelStatus.Cancelled, job, null);

    public static DownloadCancelResult NothingToStop(string error) => new(DownloadCancelStatus.NothingToStop, null, error);

    public static DownloadCancelResult StillBeingSent(string error) => new(DownloadCancelStatus.StillBeingSent, null, error);

    /// <summary>MAM-5 — the caller may not end the job's replacement, which the cancel would; the caller answers with
    /// its own sentence.</summary>
    public static DownloadCancelResult ReplacementNeedsTheWebApp { get; } = new(DownloadCancelStatus.ReplacementNeedsTheWebApp, null, null);
}
