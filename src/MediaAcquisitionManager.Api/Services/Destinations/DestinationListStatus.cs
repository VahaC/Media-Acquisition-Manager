namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>How a request for a page of the destination list ended.</summary>
public enum DestinationListStatus
{
    /// <summary>The page was read, each destination on it checked.</summary>
    Listed = 1,

    /// <summary>The page or the page size is not one the list takes, which the error names.</summary>
    Invalid = 2,
}
