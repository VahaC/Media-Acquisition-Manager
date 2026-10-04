namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the Library page's works: a search, three filters and a page, all
/// applied by the server. MAM-32 — the list holds movies, series and the episodes of no series; a series
/// is listed when one of its episodes passes the search and filters.</summary>
/// <remarks>
/// MAM-8 — the filters are strings, not a <c>Guid?</c> and an enum: a typed query parameter would answer
/// a bad value with a validation body, not the <c>{ error }</c> sentence the page shows (the
/// <see cref="IntegrationListRequest"/> reason).
/// </remarks>
/// <param name="Search">Part of a title, a registry id or a file path, in any letter case, or a year;
/// blank lists every work. A series is found by its own titles and ids or by any of its episodes'.</param>
/// <param name="LocationId">The id of a library (<see cref="LibraryLocationDto.Id"/>): only works with a
/// file in it. Blank for every library.</param>
/// <param name="Identifiers"><c>Missing</c>, in any letter case, for the works that lack an id of their
/// kind (<see cref="LibraryWorkDto.MissingIdentifiers"/>); blank for every work.</param>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>, in any letter case (MAM-32): only movies, or only
/// series and the episodes of no series; blank for both.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Works per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record LibraryWorkListRequest(
    string? Search = null,
    string? LocationId = null,
    string? Identifiers = null,
    string? Kind = null,
    int Page = 1,
    int PageSize = 25);
