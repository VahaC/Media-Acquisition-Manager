namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>How a request for a page of the integration list ended.</summary>
public enum IntegrationListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page, the page size or the kind is not one the list takes, which the error
    /// names.</summary>
    Invalid = 2,
}
