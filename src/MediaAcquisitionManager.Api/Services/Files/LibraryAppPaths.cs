using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Where the app finds every file name a library reported (MAM-18, ADR 0021): what makes a file the scan
/// reaches "reported", and what takes a name the scan stored off the Library page's count. One helper, so
/// the scan and the count can't disagree.
/// </summary>
/// <remarks>
/// <para>
/// Worked out when asked, through each integration's current mappings (ADR 0003), never stored with a
/// scan: a sync that starts reporting a path, or a fixed mapping, counts at once, without a rescan.
/// </para>
/// <para>
/// Every name with a version counts, whatever source kind reported it; only the scan's own names have
/// none. A name the mappings give no app path can't be compared with a path on the disk, so it reports
/// nothing. Matching is by path alone (<see cref="NormalizedPath"/> equality, so letter case follows the
/// path's style), never by a file name's text, a title or an id.
/// </para>
/// </remarks>
public static class LibraryAppPaths
{
    /// <summary>The app paths of every file name a library reported, through the current mappings; a name
    /// whose integration is gone maps as it is, as the inspection maps it.</summary>
    public static async Task<HashSet<NormalizedPath>> LoadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var files = await db.PhysicalFiles
            .AsNoTracking()
            .Where(file => file.MediaVersionId != null)
            .Select(file => new { file.ServicePath, file.SourceConnectionId })
            .ToListAsync(cancellationToken);
        var pathMappings = (await db.PathMappings.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PathMappingPair>)[.. group
                    .OrderBy(mapping => mapping.Position)
                    .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))]);

        var paths = new HashSet<NormalizedPath>();
        foreach (var file in files)
        {
            var mapped = PathMapper.Map(
                file.ServicePath,
                file.SourceConnectionId is { } connectionId ? pathMappings.GetValueOrDefault(connectionId) ?? [] : []);
            if (mapped.HasAppPath && NormalizedPath.TryParse(mapped.AppPath, out var path, out _))
                paths.Add(path);
        }

        return paths;
    }
}
