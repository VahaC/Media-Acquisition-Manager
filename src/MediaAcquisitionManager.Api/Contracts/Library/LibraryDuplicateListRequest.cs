namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — the query string of the Duplicates list: a class and a page.</summary>
/// <remarks>
/// The class is a string, not an enum: a typed query parameter would answer a bad value with a validation body, not
/// the <c>{ error }</c> sentence the page shows (the <see cref="LibraryWorkListRequest"/> reason).
/// </remarks>
/// <param name="Class">One class by name, in any letter case — <c>SameFile</c>, <c>ByteIdentical</c>, <c>SameWork</c>
/// or <c>Possible</c> (<see cref="Core.Enums.DuplicateClass"/>), never its number; blank for every class.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Groups per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record LibraryDuplicateListRequest(string? Class = null, int Page = 1, int PageSize = 25);
