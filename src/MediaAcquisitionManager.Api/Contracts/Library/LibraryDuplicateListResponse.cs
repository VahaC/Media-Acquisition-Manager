namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — one page of the Duplicates list.</summary>
/// <param name="Items">The groups on this page: by class in the specification's order, then by the title of their
/// first work in Ukrainian alphabetical order (a group of names no library reported by its first path), then by
/// key.</param>
/// <param name="TotalCount">Every group of the class asked for, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record LibraryDuplicateListResponse(IReadOnlyList<LibraryDuplicateGroupDto> Items, int TotalCount, int Page, int PageSize);
