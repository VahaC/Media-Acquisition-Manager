namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>How a request for a page of the Library page's works ended.</summary>
public enum LibraryWorkListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page, the page size, the library, the identifiers or kind filter, or the season is not
    /// one the list takes, which the error names.</summary>
    Invalid = 2,

    /// <summary>MAM-32 — no series has the id whose episodes were asked for.</summary>
    NotFound = 3,
}
