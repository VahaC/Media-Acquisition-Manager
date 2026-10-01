namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// The order a stored search's releases are listed in (MAM-12). Never stored: each list request names
/// it (<c>SearchReleaseListRules</c>).
/// </summary>
/// <remarks>
/// A release whose listing doesn't give the value sorted by comes last in every order: unknown is never
/// read as the most or the least. Ties keep the search's own order — the source's place in the search,
/// then the release's place in its source's answer — so a page never reshuffles between two reads.
/// </remarks>
public enum SearchReleaseSort
{
    /// <summary>Most seeders first: the release that downloads soonest. The default.</summary>
    Seeders = 1,

    /// <summary>Largest first, by the size the listing claims.</summary>
    Largest = 2,

    /// <summary>Smallest first, by the size the listing claims.</summary>
    Smallest = 3,

    /// <summary>Most recently published first.</summary>
    Newest = 4,

    /// <summary>Highest resolution the title claims first, then most seeders among those of one
    /// resolution.</summary>
    Quality = 5,
}
