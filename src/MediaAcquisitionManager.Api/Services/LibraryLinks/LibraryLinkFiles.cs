using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the library files of a set of works, as the TMDb suggestions use them: a film's or an episode's own, and a
/// series' through its episodes (a series item has no file of its own). Where they sit gives a work's folder hint, its
/// library and its linked neighbours; how long the app measured them gives its runtime.
/// </summary>
/// <remarks>
/// Worked out when asked, never stored (the <c>LibraryAppPaths</c> rule). A file a folder scan found and no library
/// reported belongs to no version, so it never shows here (ADR 0021).
/// </remarks>
internal static class LibraryLinkFiles
{
    /// <summary>Every file of <paramref name="workIds"/>'s versions — and of their episodes' — in path order.</summary>
    public static async Task<List<LibraryLinkFile>> LoadAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> workIds, CancellationToken cancellationToken)
    {
        if (workIds.Count == 0)
            return [];

        // One JSON parameter for each id list (EF.Parameter, the LibrarySnapshotStore rule): SQLite caps how many
        // parameters one command takes, and the TMDb page reads every work without an id at once.
        List<Guid> asked = [.. workIds.Distinct()];
        var episodes = await db.MediaEpisodePositions
            .AsNoTracking()
            .Where(position => position.SeriesWorkId != null && EF.Parameter(asked).Contains(position.SeriesWorkId!.Value))
            .Select(position => new { EpisodeId = position.MediaWorkId, SeriesId = position.SeriesWorkId!.Value })
            .Distinct()
            .ToListAsync(cancellationToken);
        var seriesOf = episodes.ToLookup(episode => episode.EpisodeId, episode => episode.SeriesId);
        List<Guid> owners = [.. asked.Concat(episodes.Select(episode => episode.EpisodeId)).Distinct()];

        var rows = await (
                from version in db.MediaVersions.AsNoTracking()
                join file in db.PhysicalFiles.AsNoTracking() on (Guid?)version.Id equals file.MediaVersionId
                from reading in db.MediaInspections.AsNoTracking().Where(reading => reading.Id == file.MediaInspectionId).DefaultIfEmpty()
                where EF.Parameter(owners).Contains(version.MediaWorkId)
                select new
                {
                    version.MediaWorkId,
                    VersionId = version.Id,
                    file.ServicePath,
                    file.LibraryLocationId,
                    // The LibraryMapper rule: a kept reading alone doesn't count — only a file whose last look read it.
                    Duration = file.InspectionStatus == FileInspectionStatus.Inspected && reading != null && reading.Error == null
                        ? reading.DurationMilliseconds
                        : null,
                })
            .ToListAsync(cancellationToken);

        var askedSet = asked.ToHashSet();
        return
        [
            .. rows
                .SelectMany(row => (askedSet.Contains(row.MediaWorkId) ? [row.MediaWorkId] : Enumerable.Empty<Guid>())
                    .Concat(seriesOf[row.MediaWorkId].Where(askedSet.Contains))
                    .Select(workId => new LibraryLinkFile(workId, row.MediaWorkId, row.VersionId, row.ServicePath, row.LibraryLocationId, row.Duration)))
                .OrderBy(file => file.ServicePath, StringComparer.Ordinal)
                .ThenBy(file => file.VersionId),
        ];
    }
}
