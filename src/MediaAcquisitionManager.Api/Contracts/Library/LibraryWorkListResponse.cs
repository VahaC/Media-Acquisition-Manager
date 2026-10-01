namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of the Library page's works.</summary>
/// <param name="Items">The works on this page, ordered by title in Ukrainian alphabetical order, letter
/// case ignored (MAM-8).</param>
/// <param name="TotalCount">Every work matching the search and filters, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record LibraryWorkListResponse(IReadOnlyList<LibraryWorkDto> Items, int TotalCount, int Page, int PageSize);
