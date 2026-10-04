namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of a stored search's release list: filters, an order and a page, all applied
/// by the server (MAM-12).</summary>
/// <remarks>
/// Every filter a string, for the <see cref="LibraryWorkListRequest"/> reason: a wrong value answers the
/// rules' <c>{ error }</c> sentence, not a validation body. A blank filter filters nothing.
/// </remarks>
/// <param name="Source">A source of the search (<see cref="SearchSourceDto.Id"/>): only its releases.</param>
/// <param name="Kind">MAM-32 — <c>Movie</c> or <c>Tv</c>, in any letter case: only the releases the source
/// lists as that kind.</param>
/// <param name="Language">A three-letter ISO 639-2 code in any letter case (<c>ukr</c>): only the releases
/// whose title claims that audio language; <c>unknown</c> for those claiming none.</param>
/// <param name="Quality">A resolution class (<c>1080</c>): only the releases whose title claims exactly it;
/// <c>unknown</c> for those claiming none.</param>
/// <param name="MinSizeBytes">Only the releases the listing claims at least this many bytes for.</param>
/// <param name="MaxSizeBytes">Only the releases the listing claims at most this many bytes for.</param>
/// <param name="Sort"><c>seeders</c> (the default), <c>largest</c>, <c>smallest</c>, <c>newest</c> or
/// <c>quality</c>, in any letter case.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Releases per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record SearchReleaseListRequest(
    string? Source = null,
    string? Kind = null,
    string? Language = null,
    string? Quality = null,
    string? MinSizeBytes = null,
    string? MaxSizeBytes = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 25);
