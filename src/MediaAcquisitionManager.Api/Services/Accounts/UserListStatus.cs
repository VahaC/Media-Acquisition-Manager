namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>How a request for a page of the user list ended.</summary>
public enum UserListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page or the page size is out of range, which the error names.</summary>
    Invalid = 2,
}
