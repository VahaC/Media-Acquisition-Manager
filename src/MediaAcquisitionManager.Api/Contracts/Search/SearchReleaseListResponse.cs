namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of a stored search's releases (MAM-12).</summary>
/// <param name="Items">The releases on this page, in the order asked for.</param>
/// <param name="TotalCount">Every release matching the filters, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record SearchReleaseListResponse(IReadOnlyList<SearchReleaseDto> Items, int TotalCount, int Page, int PageSize);
