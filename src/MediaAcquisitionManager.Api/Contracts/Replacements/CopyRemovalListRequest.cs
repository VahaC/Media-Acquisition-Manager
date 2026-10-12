namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-40 — the query string of the Removed copies list: a page.</summary>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Removals per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record CopyRemovalListRequest(int Page = 1, int PageSize = 25);
