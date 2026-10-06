namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the query string of the confident suggestions: one library or all, a page at a time.</summary>
/// <remarks>The library a string, for the <see cref="LibraryLinkRequest"/> reason (R18); the page numbers as every
/// list's.</remarks>
/// <param name="LocationId">Only the works with a file in this library; blank for every library.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Suggestions per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record ConfidentSuggestionListRequest(string? LocationId = null, int Page = 1, int PageSize = 25);
