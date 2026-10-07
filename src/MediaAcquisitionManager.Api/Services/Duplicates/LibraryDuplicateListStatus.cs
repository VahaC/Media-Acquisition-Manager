namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-20 — how a request for a page of the Duplicates list ended.</summary>
public enum LibraryDuplicateListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The page, the page size, the class or the decision is not one the list takes, which the error
    /// names.</summary>
    Invalid = 2,
}
