namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the TMDb film or show the owner links a library work to, from its TMDb dialog.</summary>
/// <remarks>
/// Every field a nullable string without <c>[Required]</c> (R18, the <see cref="SendDownloadRequest"/> reason): a missing
/// or malformed value reaches the service's rules, whose <c>{ error }</c> sentence the dialog shows, not a validation
/// body with no <c>error</c> in it.
/// </remarks>
/// <param name="CatalogKind"><c>Movie</c> for a TMDb film, <c>Series</c> for a TMDb show.</param>
/// <param name="CatalogId">TMDb's id of it, in the numbering of that kind.</param>
/// <param name="SeasonNumber">For a show linked to a film or an episode with no series: the season the item is of,
/// 0 to 999; blank for none.</param>
/// <param name="EpisodeNumber">The episode of that season, 1 to 9999; never without a season.</param>
/// <param name="SourceItemIds">R17 — the work's library items to link; none for every item of the work.</param>
public sealed record LibraryLinkRequest(
    string? CatalogKind = null,
    string? CatalogId = null,
    string? SeasonNumber = null,
    string? EpisodeNumber = null,
    IReadOnlyList<string?>? SourceItemIds = null);
