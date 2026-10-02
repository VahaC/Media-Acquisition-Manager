namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>How a request for a page of a download job's files ended (MAM-14).</summary>
public enum DownloadJobFileListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page or the page size is not one the list takes, which the error names.</summary>
    Invalid = 2,

    /// <summary>The caller has no such job: there is none, or it is another user's.</summary>
    NotFound = 3,
}
