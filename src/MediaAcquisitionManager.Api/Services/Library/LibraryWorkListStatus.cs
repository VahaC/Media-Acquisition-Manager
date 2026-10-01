namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>How a request for a page of the Library page's works ended.</summary>
public enum LibraryWorkListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page, the page size, the library or the identifiers filter is not one the list
    /// takes, which the error names.</summary>
    Invalid = 2,
}
