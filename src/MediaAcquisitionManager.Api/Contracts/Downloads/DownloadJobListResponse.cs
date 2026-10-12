namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of the caller's download jobs (MAM-14).</summary>
/// <param name="Items">The jobs on this page, newest first — the caller's own only, the owner's too
/// (MAM-14's decision 1).</param>
/// <param name="TotalCount">Every job of the caller matching the filter, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
/// <param name="NeedsYouCount">MAM-41 — every job of the caller that waits for them (<c>DownloadJobView.NeedsYou</c>),
/// whatever the view, status and page asked for: the tab's count.</param>
/// <param name="InProgressCount">MAM-41 — every job of the caller the app still works on by itself
/// (<c>DownloadJobView.InProgress</c>), whatever the view, status and page asked for: the tab's count.</param>
public sealed record DownloadJobListResponse(
    IReadOnlyList<DownloadJobDto> Items, int TotalCount, int Page, int PageSize, int NeedsYouCount, int InProgressCount);
