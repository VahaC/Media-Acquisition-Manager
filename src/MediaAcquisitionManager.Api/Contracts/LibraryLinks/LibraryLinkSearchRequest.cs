namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the query string of a free TMDb search from a library work's dialog: films and shows by the work's
/// kind, the first page of TMDb's answer.</summary>
/// <remarks>Strings, for the <see cref="LibraryLinkRequest"/> reason (R18).</remarks>
/// <param name="Title">What to search for; required.</param>
/// <param name="Year">A year that narrows the search; blank for any.</param>
public sealed record LibraryLinkSearchRequest(string? Title = null, string? Year = null);
