namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>How a request for a page of the caller's download jobs ended (MAM-14).</summary>
public enum DownloadJobListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page, the page size or the status filter is not one the list takes, which the error
    /// names.</summary>
    Invalid = 2,
}
