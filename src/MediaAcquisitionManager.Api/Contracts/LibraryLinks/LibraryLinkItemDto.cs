using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one library item of a work in its TMDb dialog, with its link and where the link stands against the
/// library.</summary>
/// <param name="LinkId">The item's link; <c>null</c> when it has none.</param>
/// <param name="SourceItemId">The library's id of the item.</param>
/// <param name="SourceName">The name of the library integration it is in; <c>null</c> when no longer known.</param>
/// <param name="ItemTitle">The item's title as the library gives it.</param>
/// <param name="ItemKind">What the library says the item is.</param>
/// <param name="CanWrite">Whether the library would identify the item: the library type can identify items, identifies
/// this kind of item (Jellyfin: a film or a series, never an episode item) and the integration is switched on. (The name
/// is the one the page already reads; "write" means "identify".)</param>
/// <param name="CannotWriteReason">Why not, as a sentence — or, MAM-34, why the item's link identifies nothing although
/// the library would identify the item: an episode's link without its season and episode, or with an episode TMDb gives no
/// IMDb id; <c>null</c> when neither.</param>
/// <param name="Target">Whether the link says the item is the catalog work or an episode of the catalog show;
/// <c>null</c> without a link.</param>
/// <param name="CatalogKind">A film (<c>Movie</c>) or a show (<c>Series</c>); <c>null</c> without a link.</param>
/// <param name="CatalogId">TMDb's id; <c>null</c> without a link.</param>
/// <param name="CatalogTitle">TMDb's title when it was linked.</param>
/// <param name="SeasonNumber">The season an episode-to-show link names.</param>
/// <param name="EpisodeNumber">The episode of that season.</param>
/// <param name="ImdbId">The IMDb id TMDb gave for the linked film or show.</param>
/// <param name="WriteState">Where the link stands against the library; <c>null</c> without a link. <c>Written</c> means the
/// library identified the item as the link says.</param>
/// <param name="WriteError">Why the library didn't identify the item, as the final sentence to show verbatim (owner
/// only).</param>
/// <param name="WriteStateUtc">When that last changed.</param>
/// <param name="SetUtc">When the link was set or last replaced.</param>
/// <param name="SetByUserName">Who set it.</param>
/// <param name="LibraryIdentifiers">The ids the last sync read for the item: the library's own.</param>
/// <param name="ConflictingValue">The library's TMDb id that is neither the link's nor one it replaces: neither wins;
/// <c>null</c> when there is none.</param>
/// <param name="EpisodeImdbId">MAM-34 — for an episode's link, the episode's own IMDb id TMDb gave: what the item is
/// identified by; <c>null</c> without one.</param>
/// <param name="IdentifiedUtc">MAM-34 — when the app last confirmed the item identified as the link says (an Identify
/// confirmed by reading the item again, or the item found already showing the result); <c>null</c> while never, and after
/// an identification that wasn't confirmed.</param>
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
    string? ConflictingValue,
    string? EpisodeImdbId,
    DateTime? IdentifiedUtc);
