namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of a series' episodes on the Library page (MAM-32): a season, the list's own
/// search and filters, and a page, all applied by the server.</summary>
/// <remarks>
/// Strings rather than an <c>int?</c> and enums, for the <see cref="LibraryWorkListRequest"/> reason: a bad
/// value answers the <c>{ error }</c> sentence the page shows, not a validation body.
/// </remarks>
/// <param name="Season">A season number (0 for specials), or <c>None</c> for the episodes without one; blank
/// for every season.</param>
/// <param name="Search">The list's search, as <see cref="LibraryWorkListRequest.Search"/>: when the series
/// itself doesn't hold it, only the episodes that do are listed.</param>
/// <param name="LocationId">Only the episodes with a file in this library.</param>
/// <param name="Identifiers"><c>Missing</c>: when the series itself lacks none of its ids, only the episodes
/// that lack one of theirs.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Episodes per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record LibraryEpisodeListRequest(
    string? Season = null,
    string? Search = null,
    string? LocationId = null,
    string? Identifiers = null,
    int Page = 1,
    int PageSize = 25);
