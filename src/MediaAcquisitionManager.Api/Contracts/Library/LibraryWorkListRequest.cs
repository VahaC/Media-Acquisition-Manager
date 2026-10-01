namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the Library page's works: a search, two filters and a page, all
/// applied by the server.</summary>
/// <remarks>
/// MAM-8 — the filters are strings, not a <c>Guid?</c> and an enum: a typed query parameter would answer
/// a bad value with a validation body, not the <c>{ error }</c> sentence the page shows (the
/// <see cref="IntegrationListRequest"/> reason).
/// </remarks>
/// <param name="Search">Part of a title, an IMDb or TMDb id or a file path, in any letter case, or a
/// year; blank lists every work.</param>
/// <param name="LocationId">The id of a library (<see cref="LibraryLocationDto.Id"/>): only works with a
/// file in it. Blank for every library.</param>
/// <param name="Identifiers"><c>Missing</c>, in any letter case, for the works that lack an IMDb or a
/// TMDb id; blank for every work.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Works per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record LibraryWorkListRequest(
    string? Search = null,
    string? LocationId = null,
    string? Identifiers = null,
    int Page = 1,
    int PageSize = 25);
