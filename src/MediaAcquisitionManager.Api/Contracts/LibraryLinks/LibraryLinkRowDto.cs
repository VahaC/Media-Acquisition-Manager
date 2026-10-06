using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one library item's TMDb link, as the TMDb page's lists show it.</summary>
/// <param name="LinkId">The link.</param>
/// <param name="WorkId">The work its item sits on; <c>null</c> while the library reports no such item.</param>
/// <param name="WorkTitle">That work's title.</param>
/// <param name="SourceName">The library integration the item is in.</param>
/// <param name="SourceItemId">The library's id of the item.</param>
/// <param name="ItemTitle">The item's title when it was linked, kept for an item the library no longer reports.</param>
/// <param name="Target">Whether the item is the catalog work or an episode of the catalog show.</param>
/// <param name="CatalogKind">A film (<c>Movie</c>) or a show (<c>Series</c>).</param>
/// <param name="CatalogId">TMDb's id.</param>
/// <param name="CatalogTitle">TMDb's title when it was linked.</param>
/// <param name="SeasonNumber">The season an episode-to-show link names.</param>
/// <param name="EpisodeNumber">The episode of that season.</param>
/// <param name="WriteState">Where the link stands against the library.</param>
/// <param name="WriteError">Why the library doesn't hold it.</param>
/// <param name="SetUtc">When it was set or last replaced.</param>
/// <param name="SetByUserName">Who set it.</param>
/// <param name="ConflictingValue">The library's TMDb id that is neither the link's nor one it replaces; <c>null</c>
/// when there is none.</param>
public sealed record LibraryLinkRowDto(
    Guid LinkId,
    Guid? WorkId,
    string? WorkTitle,
    string SourceName,
    string SourceItemId,
    string? ItemTitle,
    LibraryLinkTarget Target,
    MediaWorkKind CatalogKind,
    string CatalogId,
    string? CatalogTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    LibraryLinkWriteState WriteState,
    string? WriteError,
    DateTime SetUtc,
    string SetByUserName,
    string? ConflictingValue);
