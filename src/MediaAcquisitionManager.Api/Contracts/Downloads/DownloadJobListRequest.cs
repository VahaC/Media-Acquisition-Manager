namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the Jobs page: a status filter and a page, both applied by the server
/// (MAM-14). MAM-41 — and the tab, applied by the server too.</summary>
/// <remarks>
/// The status a string, for the <see cref="SearchReleaseListRequest"/> reason: a wrong value answers the
/// rules' <c>{ error }</c> sentence, not a validation body. A blank one filters nothing. The view the same way.
/// </remarks>
/// <param name="Status">A job status by name in any letter case (<c>downloading</c>): only the jobs that have
/// it.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Jobs per page, 1 to 100; 25 unless asked otherwise.</param>
/// <param name="View">MAM-41 — <c>NeedsYou</c>, <c>InProgress</c> or <c>Finished</c>, in any letter case: only the jobs of
/// that tab (<c>DownloadJobView</c>). With a status, both apply.</param>
public sealed record DownloadJobListRequest(string? Status = null, int Page = 1, int PageSize = 25, string? View = null);
