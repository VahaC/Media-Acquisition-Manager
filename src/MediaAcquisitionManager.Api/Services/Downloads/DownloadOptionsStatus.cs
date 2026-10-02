namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>How a read of the Download dialog's choices went (MAM-13).</summary>
public enum DownloadOptionsStatus
{
    /// <summary>The choices, possibly none.</summary>
    Listed = 1,

    /// <summary>No release was named; the error says so.</summary>
    Invalid = 2,

    /// <summary>The release isn't one of the caller's searches' from the last day.</summary>
    ReleaseMissing = 3,
}
