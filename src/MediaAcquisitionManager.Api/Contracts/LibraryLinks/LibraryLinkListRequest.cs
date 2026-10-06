namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the query string of the TMDb page's lists of links: those whose item is gone, those waiting for
/// the library, those in conflict, not written, or lost.</summary>
/// <remarks>
/// The filter a string, for the <see cref="LibraryLinkRequest"/> reason (R18); the page numbers as every list's
/// (<see cref="LibraryEpisodeListRequest"/>).
/// </remarks>
/// <param name="Filter"><c>Unattached</c>, <c>Pending</c>, <c>Conflict</c>, <c>NotWritten</c> or <c>Lost</c>;
/// required.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Links per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record LibraryLinkListRequest(string? Filter = null, int Page = 1, int PageSize = 25);
