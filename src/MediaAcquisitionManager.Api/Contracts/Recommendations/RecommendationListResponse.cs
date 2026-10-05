namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — one page of one recommendation tab as the caller sees it, with what its filters offer.</summary>
/// <param name="Items">The works on this page in the order asked for (<c>sort</c>); best first by default: score, then
/// votes, then title and catalog id, which every other order falls back to.</param>
/// <param name="TotalCount">Every work of the tab the filters keep, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
/// <param name="Facets">What the filters offer, counted over the tab before the request's filters.</param>
/// <param name="CatalogConnected">Whether a metadata catalog is connected; without one nothing is computed.</param>
/// <param name="LastRun">The latest computation, as <c>runs/latest</c> answers it; <c>null</c> before the first.</param>
public sealed record RecommendationListResponse(
    IReadOnlyList<RecommendationDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    RecommendationFacetsDto Facets,
    bool CatalogConnected,
    RecommendationRunDto? LastRun);
