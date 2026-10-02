using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>The Download dialog's choices for one release, or why there are none to read (MAM-13).</summary>
/// <param name="Status">How the read went.</param>
/// <param name="Options">The choices; <c>null</c> unless <see cref="DownloadOptionsStatus.Listed"/>.</param>
/// <param name="Error">A readable sentence; <c>null</c> when listed.</param>
public sealed record DownloadOptionsResult(DownloadOptionsStatus Status, DownloadOptionsResponse? Options, string? Error)
{
    public static DownloadOptionsResult Listed(DownloadOptionsResponse options) => new(DownloadOptionsStatus.Listed, options, null);

    public static DownloadOptionsResult Invalid(string error) => new(DownloadOptionsStatus.Invalid, null, error);

    public static DownloadOptionsResult ReleaseMissing(string error) => new(DownloadOptionsStatus.ReleaseMissing, null, error);
}
