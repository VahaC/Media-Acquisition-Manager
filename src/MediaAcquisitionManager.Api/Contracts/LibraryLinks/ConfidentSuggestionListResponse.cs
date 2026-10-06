namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one page of the library works whose TMDb suggestion is confident, and the counts per library.</summary>
/// <param name="Items">The works on this page with their confident suggestion, by title.</param>
/// <param name="TotalCount">Every such work, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
/// <param name="Locations">Per library: how many works lack a TMDb id, and how many of them have a confident
/// suggestion.</param>
public sealed record ConfidentSuggestionListResponse(
    IReadOnlyList<ConfidentSuggestionDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<LibraryLinkLocationCountDto> Locations);
