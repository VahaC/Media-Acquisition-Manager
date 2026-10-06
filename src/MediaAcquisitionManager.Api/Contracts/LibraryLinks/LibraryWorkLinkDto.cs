using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a library work's TMDb link as its card shows it: what the work's items are linked to, and the
/// worst of their states against the library.</summary>
/// <param name="Target">Whether the work is the catalog work or an episode of the catalog show.</param>
/// <param name="CatalogKind">A film (<c>Movie</c>) or a show (<c>Series</c>).</param>
/// <param name="CatalogId">TMDb's id.</param>
/// <param name="CatalogTitle">TMDb's title when it was linked.</param>
/// <param name="SeasonNumber">The season an episode-to-show link names.</param>
/// <param name="EpisodeNumber">The episode of that season.</param>
/// <param name="ImdbId">R8 — the IMDb id TMDb gave, shown even where the library's own IMDb id counts instead.</param>
/// <param name="SetByUserName">Who set it. For the installation owner only: <c>null</c> for everyone else.</param>
/// <param name="SetUtc">When it was set or last replaced.</param>
/// <param name="WriteState">The worst state of the work's items: not written, then lost, writing, queued, written, in
/// the library, kept in the app.</param>
/// <param name="WriteError">Why the library doesn't hold it. For the installation owner only.</param>
/// <param name="ConflictingValue">The library's TMDb id that is neither the link's nor one it replaces, when any item
/// holds one — for an episode's link to its show, a film id the library gave the item after it; neither wins.</param>
public sealed record LibraryWorkLinkDto(
    LibraryLinkTarget Target,
    MediaWorkKind CatalogKind,
    string CatalogId,
    string? CatalogTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? ImdbId,
    string? SetByUserName,
    DateTime SetUtc,
    LibraryLinkWriteState WriteState,
    string? WriteError,
    string? ConflictingValue);
