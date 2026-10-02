using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>The outcome of <see cref="IDownloadService.ListFilesAsync"/> (MAM-14).</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="DownloadJobFileListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take, when
/// <see cref="DownloadJobFileListStatus.Invalid"/>.</param>
public sealed record DownloadJobFileListResult(DownloadJobFileListStatus Status, DownloadJobFileListResponse? List, string? Error)
{
    public static DownloadJobFileListResult NotFound { get; } = new(DownloadJobFileListStatus.NotFound, null, null);

    public static DownloadJobFileListResult Listed(DownloadJobFileListResponse list) => new(DownloadJobFileListStatus.Listed, list, null);

    public static DownloadJobFileListResult Invalid(string error) => new(DownloadJobFileListStatus.Invalid, null, error);
}
