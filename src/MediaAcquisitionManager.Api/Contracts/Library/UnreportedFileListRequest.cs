namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-42 — the query string of the Files in no library list: a view and a page.</summary>
/// <remarks>
/// The view is a string, not an enum: a typed query parameter would answer a bad value with a validation body, not the
/// <c>{ error }</c> sentence the dialog shows (the <see cref="LibraryWorkListRequest"/> reason).
/// </remarks>
/// <param name="View">One view by name, in any letter case — <c>Problems</c> or <c>Ignored</c>
/// (<see cref="Core.Enums.UnreportedFileView"/>), never its number; blank for every file the app counts.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Files per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record UnreportedFileListRequest(string? View = null, int Page = 1, int PageSize = 25);
