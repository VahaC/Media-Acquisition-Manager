namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>MAM-42 — how a request for a page of the Files in no library list ended.</summary>
public enum UnreportedFileListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page, the page size or the view is not one the list takes, which the error names.</summary>
    Invalid = 2,
}
