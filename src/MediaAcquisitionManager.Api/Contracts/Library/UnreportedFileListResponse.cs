namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-42 — one page of the Files in no library list, with what each of its tabs holds.</summary>
/// <param name="Items">The files on this page, by path (ordinal), then id.</param>
/// <param name="TotalCount">Every file of the view asked for, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
/// <param name="CountedCount">The files in no library the app counts: the first tab's, and the Media folders row's.</param>
/// <param name="ProblemCount">Those of them the app's last look couldn't read: the Problems tab's.</param>
/// <param name="IgnoredCount">The files the owner told the app to stop counting: the Ignored tab's.</param>
/// <param name="ExtrasCount">MAM-44 — the files the library takes for a film's extras, not ignored: the Extras tab's.</param>
public sealed record UnreportedFileListResponse(
    IReadOnlyList<UnreportedFileDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int CountedCount,
    int ProblemCount,
    int IgnoredCount,
    int ExtrasCount);
