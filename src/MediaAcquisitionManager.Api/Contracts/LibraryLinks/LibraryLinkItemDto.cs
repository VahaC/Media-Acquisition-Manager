using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one library item of a work in its TMDb dialog, with its link and where the link stands against the
/// library.</summary>
/// <param name="LinkId">The item's link; <c>null</c> when it has none.</param>
/// <param name="SourceItemId">The library's id of the item.</param>
/// <param name="SourceName">The name of the library integration it is in; <c>null</c> when no longer known.</param>
/// <param name="ItemTitle">The item's title as the library gives it.</param>
/// <param name="ItemKind">What the library says the item is.</param>
/// <param name="CanWrite">Whether a link to the work itself would be written into the library: the library type can
/// set ids, writes this kind of item (Jellyfin: a film, never a series) and the integration is switched on.</param>
/// <param name="CannotWriteReason">Why not, as a sentence; <c>null</c> when it can.</param>
/// <param name="Target">Whether the link says the item is the catalog work or an episode of the catalog show;
/// <c>null</c> without a link.</param>
/// <param name="CatalogKind">A film (<c>Movie</c>) or a show (<c>Series</c>); <c>null</c> without a link.</param>
/// <param name="CatalogId">TMDb's id; <c>null</c> without a link.</param>
/// <param name="CatalogTitle">TMDb's title when it was linked.</param>
/// <param name="SeasonNumber">The season an episode-to-show link names.</param>
/// <param name="EpisodeNumber">The episode of that season.</param>
/// <param name="ImdbId">The IMDb id TMDb gave for the linked film or show.</param>
/// <param name="WriteState">Where the link stands against the library; <c>null</c> without a link.</param>
/// <param name="WriteError">Why the library doesn't hold it (owner only).</param>
/// <param name="WriteStateUtc">When that last changed.</param>
/// <param name="SetUtc">When the link was set or last replaced.</param>
/// <param name="SetByUserName">Who set it.</param>
/// <param name="LibraryIdentifiers">The ids the last sync read for the item: the library's own.</param>
/// <param name="ConflictingValue">The library's TMDb id that is neither the link's nor one it replaces: neither wins;
/// <c>null</c> when there is none.</param>
public sealed record LibraryLinkItemDto(
    Guid? LinkId,
    string SourceItemId,
    string? SourceName,
    string? ItemTitle,
    MediaWorkKind ItemKind,
    bool CanWrite,
    string? CannotWriteReason,
    LibraryLinkTarget? Target,
    MediaWorkKind? CatalogKind,
    string? CatalogId,
    string? CatalogTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? ImdbId,
    LibraryLinkWriteState? WriteState,
    string? WriteError,
    DateTime? WriteStateUtc,
    DateTime? SetUtc,
    string? SetByUserName,
    IReadOnlyList<LibraryIdentifierDto> LibraryIdentifiers,
    string? ConflictingValue);
