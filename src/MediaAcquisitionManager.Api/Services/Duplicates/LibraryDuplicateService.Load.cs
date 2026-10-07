using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Search;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

// MAM-20 — the whole library read for grouping: a fixed handful of queries however large it is, never one per work or
// file; the id lists go as one JSON parameter each (EF.Parameter, the LibraryLinkReader rule), as thousands of ids
// would run into SQLite's limit on the number of parameters.
public sealed partial class LibraryDuplicateService
{
    /// <summary>
    /// Every file name the grouping reads — the libraries' names, and the folder scan's own that no library reports now
    /// (ADR 0021: a scan row stays until the next scan, after a sync starts reporting its path, and would otherwise look
    /// like a second name of its file) — with the works holding them and their series.
    /// </summary>
    private async Task<Snapshot> LoadAsync(bool forOwner, CancellationToken cancellationToken)
    {
        var rows = await db.PhysicalFiles.AsNoTracking()
            .Where(file => file.MediaVersionId != null
                || (file.SourceKind == ProvenanceSourceKind.FileInspection && file.MediaVersionId == null))
            .ToListAsync(cancellationToken);
        // ADR 0022 — a name's hash only under the identity, size and time of the app's last look at it: a hash of bytes
        // since changed, kept while a reading holds its key, never counts.
        var hashes = await (
                from file in db.PhysicalFiles
                join hash in db.FileHashes
                    on new { DeviceId = file.DeviceId, Inode = file.Inode, SizeBytes = file.ReadSizeBytes, ModifiedUtc = file.ReadModifiedUtc }
                    equals new { DeviceId = (long?)hash.DeviceId, Inode = (long?)hash.Inode, SizeBytes = (long?)hash.SizeBytes, ModifiedUtc = (DateTime?)hash.ModifiedUtc }
                select new { file.Id, hash.Sha256 })
            .ToDictionaryAsync(hashed => hashed.Id, hashed => hashed.Sha256, cancellationToken);

        var versionIds = rows.Select(file => file.MediaVersionId).OfType<Guid>().Distinct().ToList();
        var versions = await db.MediaVersions.AsNoTracking()
            .Where(version => EF.Parameter(versionIds).Contains(version.Id))
            .ToDictionaryAsync(version => version.Id, cancellationToken);
        var workIds = versions.Values.Select(version => version.MediaWorkId).Distinct().ToList();
        var works = await db.MediaWorks.AsNoTracking()
            .Where(work => EF.Parameter(workIds).Contains(work.Id))
            .ToDictionaryAsync(work => work.Id, cancellationToken);
        var positions = (await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => EF.Parameter(workIds).Contains(position.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(position => position.MediaWorkId);
        // The series of the episodes: their ids make the episodes' series keys, their titles the series' names.
        var readIds = workIds
            .Concat(positions.SelectMany(work => work).Select(position => position.SeriesWorkId).OfType<Guid>())
            .Distinct()
            .ToList();
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => EF.Parameter(readIds).Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        // MAM-34 — the ids as every reader counts them, links applied: never the raw rows, where a superseded wrong id can
        // sit beside the right one.
        var linked = await links.ReadAsync(readIds, forOwner, cancellationToken);

        // ADR 0003 — each integration's mappings as they are now, as the Library page maps its files.
        var pathMappings = (await db.PathMappings.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(
                integration => integration.Key,
                integration => (IReadOnlyList<PathMappingPair>)[.. integration
                    .OrderBy(mapping => mapping.Position)
                    .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))]);
        var locationNames = await db.LibraryLocations.AsNoTracking()
            .ToDictionaryAsync(location => location.Id, location => location.Name, cancellationToken);
        var reported = await LibraryAppPaths.LoadAsync(db, cancellationToken);

        // MAM-9 — the facts grouping reads from each name's reading: the duration and the video tracks' dynamic ranges.
        var readingIds = rows.Select(file => file.MediaInspectionId).OfType<Guid>().Distinct().ToList();
        var readings = await db.MediaInspections.AsNoTracking()
            .Where(reading => EF.Parameter(readingIds).Contains(reading.Id))
            .Select(reading => new { reading.Id, reading.DurationMilliseconds, reading.Error })
            .ToDictionaryAsync(reading => reading.Id, cancellationToken);
        var dynamicRanges = (await db.MediaInspectionTracks.AsNoTracking()
                .Where(track => EF.Parameter(readingIds).Contains(track.MediaInspectionId)
                    && track.Kind == MediaTrackKind.Video
                    && track.DynamicRange != null)
                .Select(track => new { track.MediaInspectionId, DynamicRange = track.DynamicRange!.Value })
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId, track => track.DynamicRange);

        // One name per path: two libraries reporting one path give two rows of one name, held by both their works. The
        // path is the app path where the mappings give one, compared the path's way (letter case only on POSIX); else
        // the path the library gave, as text.
        var byPath = new List<(string PathKey, List<PhysicalFileEntity> Rows)>();
        var appPathIndex = new Dictionary<NormalizedPath, int>();
        var servicePathIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.OrderBy(file => file.ServicePath, StringComparer.Ordinal).ThenBy(file => file.Id))
        {
            var mapped = PathMapper.Map(row.ServicePath, MappingsOf(row, pathMappings));
            NormalizedPath? appPath = mapped.HasAppPath && NormalizedPath.TryParse(mapped.AppPath, out var parsed, out _) ? parsed : null;
            if (row.MediaVersionId is null && appPath is not null && reported.Contains(appPath))
                continue;

            if (appPath is not null)
            {
                if (appPathIndex.TryGetValue(appPath, out var index))
                    byPath[index].Rows.Add(row);
                else
                {
                    appPathIndex[appPath] = byPath.Count;
                    byPath.Add(("app:" + appPath, [row]));
                }
            }
            else if (servicePathIndex.TryGetValue(row.ServicePath, out var index))
                byPath[index].Rows.Add(row);
            else
            {
                servicePathIndex[row.ServicePath] = byPath.Count;
                byPath.Add(("service:" + row.ServicePath, [row]));
            }
        }

        var files = new Dictionary<Guid, PhysicalFileEntity>();
        List<DuplicateName> names = [];
        foreach (var (pathKey, pathRows) in byPath)
        {
            // The row the name is shown by: one the app has looked at, if any, then by service path and id.
            var shown = pathRows.OrderBy(file => file.DeviceId is null || file.Inode is null).First();
            files[shown.Id] = shown;
            List<DuplicateHolder> holders =
            [
                .. pathRows
                    .Select(file => file.MediaVersionId is { } versionId ? versions.GetValueOrDefault(versionId) : null)
                    .OfType<MediaVersionEntity>()
                    .Select(version => new DuplicateHolder(version.MediaWorkId, version.Id))
                    .Distinct(),
            ];
            // MAM-9 — the facts count only when the last look read the media and the reading is no cached failure.
            var reading = shown is { InspectionStatus: FileInspectionStatus.Inspected, MediaInspectionId: { } readingId }
                && readings.GetValueOrDefault(readingId) is { Error: null } read
                    ? read
                    : null;
            // MAM-20 — a title is no edition: the works' own titles and their shows', or what an unreported name claims,
            // are taken out of the names before their words are read as labels ("Uncut.Gems.2019.mkv" is no Uncut cut).
            List<string> ownTitles = holders.Count == 0
                ? [.. FileNameClaims.Read(shown.ServicePath).SelectMany(claim => claim.Titles)]
                : [.. holders.SelectMany(holder => TitlesOfWork(holder.WorkId, titles, positions))];
            names.Add(new DuplicateName(
                shown.Id,
                pathKey,
                shown.DeviceId,
                shown.Inode,
                shown.LinkCount,
                shown.ReadSizeBytes,
                hashes.GetValueOrDefault(shown.Id),
                holders,
                reading?.DurationMilliseconds,
                reading is null ? [] : [.. dynamicRanges[reading.Id].Distinct().Order()],
                [.. EditionLabels.Read(FileNameOf(shown.ServicePath), ownTitles)
                    .Concat(holders.SelectMany(holder => EditionLabels.Read(versions[holder.VersionId].EditionLabel, ownTitles)))
                    .Distinct()
                    .Order()],
                // MAM-20 — only a name no library reported meets a work by its name; a library's says what it is. An
                // episode's name claims its show's title and year, as all its show's episodes do: none is offered by it.
                holders.Count == 0 ? FileNameClaims.ReadUnlessEpisode(shown.ServicePath) : []));
        }

        var heldIds = names.SelectMany(name => name.Holders).Select(holder => holder.WorkId).Distinct().ToList();
        List<DuplicateWork> duplicateWorks =
        [
            .. heldIds
                .Where(works.ContainsKey)
                .Select(workId => (WorkId: workId, Places: PlacesOf(positions[workId], linked[workId], linked, titles)))
                .Select(work => new DuplicateWork(
                    work.WorkId,
                    works[work.WorkId].Kind,
                    RegistryKeys.Of(works[work.WorkId].Kind, IdentifiersOf(linked[work.WorkId]), work.Places),
                    [.. titles[work.WorkId].Select(title => new ReportedTitle(title.Text, title.Kind, title.Year))],
                    work.Places)),
        ];

        return new Snapshot(names, files, duplicateWorks, works, titles, positions, versions, linked, pathMappings, locationNames);
    }

    private static IReadOnlyList<PathMappingPair> MappingsOf(
        PhysicalFileEntity file, IReadOnlyDictionary<Guid, IReadOnlyList<PathMappingPair>> pathMappings) =>
        file.SourceConnectionId is { } connectionId ? pathMappings.GetValueOrDefault(connectionId) ?? [] : [];

    private static IReadOnlyList<MediaIdentifier> IdentifiersOf(LinkedWork linked) =>
        [.. linked.Identifiers.Select(identifier => new MediaIdentifier(identifier.Scheme, identifier.Value))];

    /// <summary>
    /// Where an episode sits, with its show's ids and titles: each place a library gives it in a series with both
    /// numbers, and — MAM-34 — each episode-to-show link with both numbers, the link's TMDb show as the show, whose titles
    /// the library doesn't hold (ADR 0028: the film item linked as S01E08 and the library's own S01E08 are one work).
    /// </summary>
    private static IReadOnlyList<DuplicateEpisodePlace> PlacesOf(
        IEnumerable<MediaEpisodePositionEntity> positions,
        LinkedWork work,
        IReadOnlyDictionary<Guid, LinkedWork> linked,
        ILookup<Guid, MediaWorkTitleEntity> titles) =>
    [
        .. positions
            .Where(position => position is { SeriesWorkId: not null, SeasonNumber: not null, EpisodeNumber: not null })
            .Select(position => new DuplicateEpisodePlace(
                linked.TryGetValue(position.SeriesWorkId!.Value, out var series) ? IdentifiersOf(series) : [],
                [.. titles[position.SeriesWorkId!.Value].Select(title => title.Text)],
                position.SeasonNumber!.Value,
                position.EpisodeNumber!.Value)),
        .. work.Links
            .Where(link => link is { Target: LibraryLinkTarget.ShowEpisode, SeasonNumber: not null, EpisodeNumber: not null })
            .Select(link => new DuplicateEpisodePlace(
                [new MediaIdentifier(link.Scheme, link.Value)], [], link.SeasonNumber!.Value, link.EpisodeNumber!.Value)),
    ];

    // The titles a work goes by, every source's, and its shows': the words a name of it holds that are no edition.
    private static IEnumerable<string> TitlesOfWork(
        Guid workId, ILookup<Guid, MediaWorkTitleEntity> titles, ILookup<Guid, MediaEpisodePositionEntity> positions) =>
        titles[workId]
            .Concat(positions[workId].Select(position => position.SeriesWorkId).OfType<Guid>().SelectMany(seriesId => titles[seriesId]))
            .Select(title => title.Text);

    // The last name of a path in either style: where a file's name says its edition.
    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>What one read of the library gives the grouping and the page.</summary>
    /// <param name="Names">Every name, one per path.</param>
    /// <param name="Files">The row each name is shown by, by its id.</param>
    /// <param name="Works">The works holding the names, as the grouping reads them.</param>
    /// <param name="WorkRows">Those works' rows.</param>
    /// <param name="Titles">The titles of those works and of their series.</param>
    /// <param name="Positions">The episodes' places.</param>
    /// <param name="Versions">The versions holding the names.</param>
    /// <param name="Linked">The ids of those works and of their series, links applied.</param>
    /// <param name="PathMappings">Each integration's mappings, in their order.</param>
    /// <param name="LocationNames">Each library's name.</param>
    private sealed record Snapshot(
        IReadOnlyList<DuplicateName> Names,
        IReadOnlyDictionary<Guid, PhysicalFileEntity> Files,
        IReadOnlyList<DuplicateWork> Works,
        IReadOnlyDictionary<Guid, MediaWorkEntity> WorkRows,
        ILookup<Guid, MediaWorkTitleEntity> Titles,
        ILookup<Guid, MediaEpisodePositionEntity> Positions,
        IReadOnlyDictionary<Guid, MediaVersionEntity> Versions,
        IReadOnlyDictionary<Guid, LinkedWork> Linked,
        IReadOnlyDictionary<Guid, IReadOnlyList<PathMappingPair>> PathMappings,
        IReadOnlyDictionary<Guid, string> LocationNames);
}
