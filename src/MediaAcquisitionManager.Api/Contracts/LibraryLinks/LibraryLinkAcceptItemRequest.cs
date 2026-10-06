namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one accepted confident suggestion of a <see cref="LibraryLinkAcceptRequest"/>.</summary>
/// <remarks>
/// Every field a nullable string (R18). The server takes it only while that candidate is still the work's confident one,
/// computed again when the click arrives; otherwise the work is skipped with the reason.
/// </remarks>
/// <param name="WorkId">The library work.</param>
/// <param name="CatalogKind"><c>Movie</c> or <c>Series</c>, as the suggestion said.</param>
/// <param name="CatalogId">TMDb's id, as the suggestion said.</param>
public sealed record LibraryLinkAcceptItemRequest(string? WorkId = null, string? CatalogKind = null, string? CatalogId = null);
