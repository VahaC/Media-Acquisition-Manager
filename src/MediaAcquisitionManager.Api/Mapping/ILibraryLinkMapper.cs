using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>MAM-34 — library items' TMDb links into what the TMDb dialog and the TMDb page's lists show.</summary>
/// <remarks>
/// Every endpoint showing them is the installation owner's (R19), so who set a link and why the library doesn't hold it
/// are always in.
/// </remarks>
public interface ILibraryLinkMapper
{
    /// <summary>
    /// One library item of a work in its dialog: the item as the library reports it, whether ids would be written into
    /// it in its library (<paramref name="canWrite"/>, <paramref name="cannotWriteReason"/>), its link when it has one —
    /// MAM-34: with the episode's IMDb id it writes and the library's lookup asked after the write —, and the ids the last
    /// sync read for it (<paramref name="libraryRows"/>, the library's own) — with the library's TMDb id that is neither
    /// the link's nor one it replaces, when there is one.
    /// </summary>
    LibraryLinkItemDto ToItemDto(
        string sourceItemId,
        string? sourceName,
        string? itemTitle,
        MediaWorkKind itemKind,
        bool canWrite,
        string? cannotWriteReason,
        LibraryItemLinkEntity? link,
        IReadOnlyList<MediaWorkIdentifierEntity> libraryRows);

    /// <summary>
    /// One link as the TMDb page's lists show it, on the work <paramref name="workTitle"/> names (none for a link whose
    /// item is gone), in the library <paramref name="sourceName"/>, with the conflict the item's
    /// <paramref name="libraryRows"/> make.
    /// </summary>
    LibraryLinkRowDto ToRowDto(
        LibraryItemLinkEntity link,
        string? workTitle,
        string sourceName,
        IReadOnlyList<MediaWorkIdentifierEntity> libraryRows);

    /// <summary>The kind of catalog work a link names, by its number space: a film for a TMDb id, a show for a TMDb TV id
    /// (ADR 0017).</summary>
    MediaWorkKind CatalogKindOf(MediaIdentifierScheme scheme);
}
