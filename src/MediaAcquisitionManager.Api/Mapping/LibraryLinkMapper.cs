using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ILibraryLinkMapper"/>
public sealed class LibraryLinkMapper(ILibraryMapper libraryMapper, IRequestTexts texts) : ILibraryLinkMapper
{
    public LibraryLinkItemDto ToItemDto(
        string sourceItemId,
        string? sourceName,
        string? itemTitle,
        MediaWorkKind itemKind,
        bool canWrite,
        Message? cannotWriteReason,
        LibraryItemLinkEntity? link,
        IReadOnlyList<MediaWorkIdentifierEntity> libraryRows) =>
        new(
            link?.Id,
            sourceItemId,
            sourceName,
            itemTitle ?? link?.ItemTitle,
            itemKind,
            canWrite,
            texts.Render(cannotWriteReason),
            cannotWriteReason?.Key,
            link?.Target,
            link is null ? null : CatalogKindOf(link.Scheme),
            link?.Value,
            link?.CatalogTitle,
            link?.SeasonNumber,
            link?.EpisodeNumber,
            link?.ImdbId,
            link?.WriteState,
            texts.Render(link?.WriteError),
            link?.WriteError?.Key,
            link?.WriteStateUtc,
            link?.SetUtc,
            link?.SetByUserName,
            libraryMapper.ToDtos(libraryRows),
            link is null ? null : ConflictingValue(link, libraryRows),
            link?.EpisodeImdbId,
            link?.IdentifiedUtc);

    public LibraryLinkRowDto ToRowDto(
        LibraryItemLinkEntity link,
        string? workTitle,
        string sourceName,
        IReadOnlyList<MediaWorkIdentifierEntity> libraryRows) =>
        new(
            link.Id,
            link.MediaWorkId,
            workTitle,
            sourceName,
            link.SourceItemId,
            link.ItemTitle,
            link.Target,
            CatalogKindOf(link.Scheme),
            link.Value,
            link.CatalogTitle,
            link.SeasonNumber,
            link.EpisodeNumber,
            link.WriteState,
            texts.Render(link.WriteError),
            link.SetUtc,
            link.SetByUserName,
            ConflictingValue(link, libraryRows),
            link.IdentifiedUtc);

    public MediaWorkKind CatalogKindOf(MediaIdentifierScheme scheme) =>
        scheme == MediaIdentifierScheme.TmdbTv ? MediaWorkKind.Series : MediaWorkKind.Movie;

    // The one rule of a conflict (LinkedIdentifiers), as the Library page's card applies it.
    private static string? ConflictingValue(LibraryItemLinkEntity link, IReadOnlyList<MediaWorkIdentifierEntity> libraryRows) =>
        LinkedIdentifiers.ConflictingValue(
            [.. libraryRows.Select(row => new MediaIdentifier(row.Scheme, row.Value))],
            new ItemLinkFacts(link.Target, link.Scheme, link.Value, link.SupersededValue, link.ImdbId, link.WriteState));
}
