namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of a job's files on its details page: a page, applied by the server
/// (MAM-14).</summary>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Files per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record DownloadJobFileListRequest(int Page = 1, int PageSize = 25);
