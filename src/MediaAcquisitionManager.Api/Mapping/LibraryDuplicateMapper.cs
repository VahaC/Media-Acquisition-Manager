using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ILibraryDuplicateMapper"/>
public sealed class LibraryDuplicateMapper(ILibraryMapper library) : ILibraryDuplicateMapper
{
    public LibraryDuplicateGroupDto ToDto(DuplicateGroup group, IReadOnlyList<LibraryDuplicateFileDto> files, LibrarySpaceDto space) =>
        new(
            group.Key,
            group.Class,
            group.NeedsReview,
            [.. group.Distinctions.Select(distinction => new DuplicateDistinctionDto(distinction.Kind, distinction.Scheme, distinction.OfSeries))],
            new DuplicateEvidenceDto(
                group.Identity is { } identity
                    ? new DuplicateIdentityDto(identity.DeviceId, identity.Inode, identity.LinkCount, identity.NameCount)
                    : null,
                group.Sha256,
                [.. group.Identifiers.Select(key => new DuplicateIdentifierDto(key.Scheme, key.Value, key.SeasonNumber, key.EpisodeNumber))],
                [.. group.Titles.Select(title => new DuplicateTitleDto(title.Text, title.Year))],
                group.DurationsAgree),
            files,
            space,
            group.Recoverable is { } recoverable ? new DuplicateRecoverableSpaceDto(recoverable.SizeBytes, recoverable.Gap) : null);

    public LibraryDuplicateFileDto ToDto(
        DuplicateName name,
        PhysicalFileEntity file,
        IReadOnlyList<PathMappingPair> pathMappings,
        string? locationName,
        MediaInspectionEntity? reading,
        IReadOnlyList<MediaInspectionTrackEntity> tracks,
        IReadOnlyList<LibraryDuplicateWorkDto> works,
        bool forOwner) =>
        new(
            // The Library page's own file, so MAM-21 shows it with the Library's parts: the app path, its status, the
            // inspection and its facts, the owner-only sentences null for everyone else.
            library.ToDto(file, pathMappings, locationName, reading, tracks, forOwner),
            name.SizeBytes,
            name.DeviceId,
            name.Inode,
            name.LinkCount,
            name.Sha256,
            name.Editions,
            [.. works
                .OrderBy(work => work.Title ?? "", LibraryTitleOrder.Comparer)
                .ThenBy(work => work.Title ?? "", StringComparer.Ordinal)
                .ThenBy(work => work.Id)
                .ThenBy(work => work.VersionId)]);

    public LibraryDuplicateWorkDto ToDto(
        MediaWorkEntity work,
        IReadOnlyList<MediaWorkTitleEntity> titles,
        IReadOnlyList<MediaEpisodePositionEntity> positions,
        Func<Guid, IReadOnlyList<MediaWorkTitleEntity>> seriesTitlesOf,
        MediaVersionEntity version,
        LinkedWork linked)
    {
        // MAM-8 — the card's title and year: a displayed title, else an original one; no year made up from another's.
        var title = LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display);
        var originalTitle = LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original);
        var episode = work.Kind == MediaWorkKind.Episode ? LibraryMapper.ToEpisodeDto(positions) : null;
        var seriesTitles = episode?.SeriesId is { } seriesId ? seriesTitlesOf(seriesId) : [];
        return new LibraryDuplicateWorkDto(
            work.Id,
            work.Kind,
            (title ?? originalTitle)?.Text,
            title?.Year ?? originalTitle?.Year,
            episode,
            (LibraryMapper.FirstInTitleOrder(seriesTitles, MediaTitleKind.Display)
                ?? LibraryMapper.FirstInTitleOrder(seriesTitles, MediaTitleKind.Original))?.Text,
            version.Id,
            version.EditionLabel,
            version.SourceName,
            // MAM-34 — the ids as every reader counts them, with their sources: files of works whose ids disagree each
            // show their own work's (Vasyl, 2026-10-07).
            library.ToDtos(linked.Identifiers));
    }
}
