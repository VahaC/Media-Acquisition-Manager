namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one page of the TMDb page's links of a filter.</summary>
/// <param name="Items">The links on this page.</param>
/// <param name="TotalCount">Every link of the filter, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record LibraryLinkListResponse(IReadOnlyList<LibraryLinkRowDto> Items, int TotalCount, int Page, int PageSize);
