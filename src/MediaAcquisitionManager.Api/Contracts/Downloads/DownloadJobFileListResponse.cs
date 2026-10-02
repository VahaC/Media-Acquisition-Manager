namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of a download job's files, as its client last listed them (MAM-14).</summary>
/// <param name="Items">The files on this page, in the client's order.</param>
/// <param name="TotalCount">Every file of the job, across all pages; 0 until the client has listed them (a
/// magnet still fetching its metadata).</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record DownloadJobFileListResponse(IReadOnlyList<DownloadJobFileDto> Items, int TotalCount, int Page, int PageSize);
