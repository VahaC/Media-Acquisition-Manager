namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — the query string of the Duplicates list: a class, a decision (MAM-21), a sort (MAM-45) and a
/// page.</summary>
/// <remarks>
/// The class, the decision and the sort are strings, not enums: a typed query parameter would answer a bad value with a
/// validation body, not the <c>{ error }</c> sentence the page shows (the <see cref="LibraryWorkListRequest"/> reason).
/// </remarks>
/// <param name="Class">One class by name, in any letter case — <c>SameFile</c>, <c>ByteIdentical</c>, <c>SameWork</c>
/// or <c>Possible</c> (<see cref="Core.Enums.DuplicateClass"/>), never its number; blank for every class.</param>
/// <param name="Decision">MAM-21 — the groups the owner decided so (MAM-40: kept copies are dismissed), by name in any letter case —
/// <c>Confirmed</c> or <c>Dismissed</c> (<see cref="Core.Enums.DuplicateMatchDecision"/>), never its number; blank for
/// every group except the dismissed ones, which leave the list.</param>
/// <param name="Sort">MAM-45 — the order by name, in any letter case: <c>Default</c> (when blank) or <c>Recoverable</c>,
/// the most recoverable room first and the groups without an estimate last (<see cref="Core.Enums.DuplicateSort"/>),
/// never its number.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Groups per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record LibraryDuplicateListRequest(
    string? Class = null, string? Decision = null, string? Sort = null, int Page = 1, int PageSize = 25);
