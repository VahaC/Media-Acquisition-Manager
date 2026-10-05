namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — how a request for a page of a recommendation tab ended.</summary>
public enum RecommendationListStatus
{
    /// <summary>The page was read.</summary>
    Listed = 1,

    /// <summary>The tab, a filter, the page or the page size is not one the list takes, which the error names.</summary>
    Invalid = 2,

    /// <summary>The caller switched the tab off in their Profile (Vasyl's decision 5): they see none of it.</summary>
    TabSwitchedOff = 3,
}
