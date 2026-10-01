using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibraryWorkReader"/>
/// <remarks>
/// MAM-12 — moved out of <see cref="LibraryService"/> unchanged, so the search page shows a work through
/// the same loader as the Library page.
/// </remarks>
public sealed class LibraryWorkReader(ApplicationDbContext db, ILibraryMapper mapper) : ILibraryWorkReader
{
    public async Task<List<LibraryWorkDto>> ReadAsync(IReadOnlyList<MediaWorkEntity> works, bool forOwner, CancellationToken cancellationToken = default)
    {
        var workIds = works.Select(work => work.Id).ToList();
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => workIds.Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var identifiers = (await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => workIds.Contains(identifier.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(identifier => identifier.MediaWorkId);
        var versions = await db.MediaVersions.AsNoTracking()
            .Where(version => workIds.Contains(version.MediaWorkId))
            .ToListAsync(cancellationToken);

        var versionIds = versions.Select(version => version.Id).ToList();
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
                [.. identifiers[work.Id]],
                [.. versionsByWork[work.Id]],
                version => [.. filesByVersion[version.Id].Select(file => mapper.ToDto(
                    file,
                    file.SourceConnectionId is { } connectionId ? pathMappings.GetValueOrDefault(connectionId) ?? [] : [],
                    file.LibraryLocationId is { } location ? locationNames.GetValueOrDefault(location) : null,
                    file.MediaInspectionId is { } readingId ? readings.GetValueOrDefault(readingId) : null,
                    file.MediaInspectionId is { } trackReadingId ? [.. tracks[trackReadingId]] : [],
                    forOwner))])),
        ];
    }
}
