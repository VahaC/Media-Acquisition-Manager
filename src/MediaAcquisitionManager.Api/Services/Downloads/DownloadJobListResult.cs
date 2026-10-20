using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>The outcome of <see cref="IDownloadService.ListAsync"/> (MAM-14).</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="DownloadJobListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take otherwise.</param>
public sealed record DownloadJobListResult(DownloadJobListStatus Status, DownloadJobListResponse? List, Message? Error)
{
    public static DownloadJobListResult Listed(DownloadJobListResponse list) => new(DownloadJobListStatus.Listed, list, null);

    public static DownloadJobListResult Invalid(Message error) => new(DownloadJobListStatus.Invalid, null, error);
}
