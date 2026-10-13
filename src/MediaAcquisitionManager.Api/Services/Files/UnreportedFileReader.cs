using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// MAM-42 — the names the scans stored that no library name leads to now (<see cref="LibraryAppPaths"/>): the files in
/// no library, the ignored ones included. One reader for the Media folders row's counts and the Files in no library
/// list, so the two can't disagree.
/// </summary>
/// <remarks>
/// MAM-18, ADR 0021 — worked out when asked: a sync that starts reporting a path, or a fixed path mapping, takes a name
/// off at once, and the next scan deletes its row. The app's own path is matched as it is: no mapping applies to it.
/// </remarks>
public static class UnreportedFileReader
{
    /// <summary>Every such name, untracked, in path order (ordinal), then id, so "the first problem" and a page are the
    /// same on every read.</summary>
    public static async Task<List<PhysicalFileEntity>> LoadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var names = await db.PhysicalFiles
            .AsNoTracking()
            .Where(file => file.SourceKind == ProvenanceSourceKind.FileInspection && file.MediaVersionId == null)
            .ToListAsync(cancellationToken);
        // Before any scan stored a name, which is the default, no library path is read.
        if (names.Count == 0)
            return names;

        var libraryPaths = await LibraryAppPaths.LoadAsync(db, cancellationToken);
        return
        [
            .. names
                .Where(name => !(NormalizedPath.TryParse(PathMapper.Map(name.ServicePath, []).AppPath, out var path, out _)
                    && libraryPaths.Contains(path)))
                .OrderBy(name => name.ServicePath, StringComparer.Ordinal)
                .ThenBy(name => name.Id),
        ];
    }

    /// <summary>Whether the name's own path is one the app can use: the scan stores the app's path, which maps as it
    /// is.</summary>
    public static bool HasAppPathOf(PhysicalFileEntity name) => PathMapper.Map(name.ServicePath, []).HasAppPath;
}
