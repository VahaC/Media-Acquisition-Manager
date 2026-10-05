using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — the recommendations as one user reads them: a page of one tab, filtered, ordered and paged by the server
/// from what the last successful run stored for everyone (ADR 0023). Reads only; nothing is asked of the catalog or a
/// release source. Expected failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// <para>
/// The run stores one list for the installation; what makes it the caller's is applied here, on every read: their
/// own tabs, their "Not interested" marks (Vasyl's decision 3), the download jobs as they stand — any user's, as the
/// download serves the household — and the availability check, which a fallback work needs to be shown at all.
/// </para>
/// <para>
/// In memory, after a few queries: the rules above match releases to works by their titles (ADR 0012), which SQL
/// can't do, and a tab holds a few thousand works at most (the reference installation's estimate). The browser still
/// gets one page — ADR 0011's way.
/// </para>
/// </remarks>
public interface IRecommendationListService
{
    /// <summary>
    /// One page of the tab <paramref name="request"/> names, as <paramref name="callerUserId"/> sees it, with the facets
    /// counted over the whole tab before the request's filters, whether a catalog is connected, and the latest run.
    /// <see cref="RecommendationListStatus.Invalid"/> for a value the list doesn't take;
    /// <see cref="RecommendationListStatus.TabSwitchedOff"/> for a tab the caller has off.
    /// </summary>
    Task<RecommendationListResult> ListAsync(RecommendationListRequest request, Guid callerUserId, CancellationToken cancellationToken = default);
}
