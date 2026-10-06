namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the query string of a pasted id in a library work's dialog: a TMDb or IMDb link, an IMDb id, or a
/// bare TMDb number.</summary>
/// <remarks>Strings, for the <see cref="LibraryLinkRequest"/> reason (R18).</remarks>
/// <param name="Text">What the owner pasted.</param>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>: which of TMDb's two numberings a bare number is in (ADR 0017);
/// ignored for a link or an IMDb id.</param>
public sealed record LibraryLinkLookupRequest(string? Text = null, string? Kind = null);
