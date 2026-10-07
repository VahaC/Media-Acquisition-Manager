using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibraryWorkReader"/>
/// <remarks>
/// MAM-12 — moved out of <see cref="LibraryService"/> unchanged, so the search page shows a work through
/// the same loader as the Library page.
/// </remarks>
public sealed class LibraryWorkReader(ApplicationDbContext db, ILibraryMapper mapper, ILibraryLinkReader links) : ILibraryWorkReader
{
    public async Task<List<LibraryWorkDto>> ReadAsync(
        IReadOnlyList<MediaWorkEntity> works,
        bool forOwner,
        IReadOnlyDictionary<Guid, IReadOnlyList<LibrarySeasonDto>>? seasons = null,
        CancellationToken cancellationToken = default)
    {
        var workIds = works.Select(work => work.Id).ToList();
        // MAM-32 — the episodes' places, and the series' seasons when the caller has no counts of its own.
        var positions = (await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => workIds.Contains(position.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(position => position.MediaWorkId);
        var seriesIds = works.Where(work => work.Kind == MediaWorkKind.Series).Select(work => work.Id).ToList();
        seasons ??= seriesIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<LibrarySeasonDto>>()
            : await LibrarySeasonCounts.ReadAsync(db.MediaEpisodePositions.AsNoTracking(), seriesIds, cancellationToken);
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => workIds.Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        // MAM-35 — the items' poster tags, the card's picture chosen among them by its title (LibraryMapper).
        var posters = (await db.MediaWorkPosters.AsNoTracking()
                .Where(poster => workIds.Contains(poster.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(poster => poster.MediaWorkId);
        // MAM-34 — the ids with the items' TMDb links applied, as every reader counts them.
        var linked = await links.ReadAsync(workIds, forOwner, cancellationToken);
        var versions = await db.MediaVersions.AsNoTracking()
            .Where(version => workIds.Contains(version.MediaWorkId))
            .ToListAsync(cancellationToken);

        // Nullable to match the column: only the scan's own names (MAM-18) have no version, and none is asked for.
        var versionIds = versions.Select(version => (Guid?)version.Id).ToList();
        var files = await db.PhysicalFiles.AsNoTracking()
            .Where(file => versionIds.Contains(file.MediaVersionId))
            .ToListAsync(cancellationToken);

        var locationIds = files.Select(file => file.LibraryLocationId).OfType<Guid>().Distinct().ToList();
        var locationNames = await db.LibraryLocations.AsNoTracking()
            .Where(location => locationIds.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Name, cancellationToken);

        // ADR 0003 — the reporting integration's mappings as they are now; a file whose integration has
        // none (or that the app read itself) maps as it is.
        var connectionIds = files.Select(file => file.SourceConnectionId).OfType<Guid>().Distinct().ToList();
        var pathMappings = (await db.PathMappings.AsNoTracking()
                .Where(mapping => connectionIds.Contains(mapping.IntegrationConnectionId))
                .ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(
                integration => integration.Key,
                integration => (IReadOnlyList<PathMappingPair>)[.. integration
                    .OrderBy(mapping => mapping.Position)
                    .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))]);

        // MAM-9 — what the media inspector read from each file's bytes; two names of one inode share one.
        var readingIds = files.Select(file => file.MediaInspectionId).OfType<Guid>().Distinct().ToList();
        var readings = await db.MediaInspections.AsNoTracking()
            .Where(reading => readingIds.Contains(reading.Id))
            .ToDictionaryAsync(reading => reading.Id, cancellationToken);
        var tracks = (await db.MediaInspectionTracks.AsNoTracking()
                .Where(track => readingIds.Contains(track.MediaInspectionId))
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId);

        var versionsByWork = versions.ToLookup(version => version.MediaWorkId);
        var filesByVersion = files.ToLookup(file => file.MediaVersionId);
        return
        [
            .. works.Select(work => mapper.ToDto(
                work,
                [.. titles[work.Id]],
                [.. posters[work.Id]],
                linked[work.Id],
                [.. versionsByWork[work.Id]],
                version => [.. filesByVersion[version.Id].Select(file => mapper.ToDto(
                    file,
                    file.SourceConnectionId is { } connectionId ? pathMappings.GetValueOrDefault(connectionId) ?? [] : [],
                    file.LibraryLocationId is { } location ? locationNames.GetValueOrDefault(location) : null,
                    file.MediaInspectionId is { } readingId ? readings.GetValueOrDefault(readingId) : null,
                    file.MediaInspectionId is { } trackReadingId ? [.. tracks[trackReadingId]] : [],
                    forOwner))],
                [.. positions[work.Id]],
                seasons.GetValueOrDefault(work.Id),
                forOwner)),
        ];
    }
}
