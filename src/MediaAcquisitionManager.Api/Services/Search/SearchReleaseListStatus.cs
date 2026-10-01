namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>How a request for a page of a stored search's releases ended (MAM-12).</summary>
public enum SearchReleaseListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>A filter, the order, the page or the page size is not one the list takes, which the error
    /// names.</summary>
    Invalid = 2,

    /// <summary>No search of the caller's has that id (<see cref="SearchReadStatus.NotFound"/>'s three
    /// cases); checked before the filters.</summary>
    NotFound = 3,
}
