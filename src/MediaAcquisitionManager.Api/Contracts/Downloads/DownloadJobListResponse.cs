namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of the caller's download jobs (MAM-14).</summary>
/// <param name="Items">The jobs on this page, newest first — the caller's own only, the owner's too
/// (MAM-14's decision 1).</param>
/// <param name="TotalCount">Every job of the caller matching the filter, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record DownloadJobListResponse(IReadOnlyList<DownloadJobDto> Items, int TotalCount, int Page, int PageSize);
