using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// The library folders the app knows (MAM-11): every root folder the media library syncs stored, mapped into the app
/// through its own integration's current path mappings (ADR 0003). One reader, so a destination's check and the
/// replacement ticks (MAM-24, ADR 0033) can't disagree about which folders a library holds.
/// </summary>
/// <remarks>
/// MAM-24 — moved here unchanged from <c>DestinationChecker</c>, the folders' first reader, when the Media folders
/// page's replacement ticks came to need the same list. Worked out when asked, never stored with a sync: a fixed
/// mapping counts at once. MAM-25 — each folder also says which integration reported it and its path in the library's
/// own terms, which the check of a replacement's new files asks the library by (ADR 0035); the other readers ignore both.
/// </remarks>
public static class LibraryFolderReader
{
    /// <summary>
    /// Every stored library folder that maps into the app — a folder the mappings give no app path is left out, as it
    /// can't hold anything the app can name — and how many were stored in all. MAM-30 — the folders left out come back
    /// apart, each with its library, its integration and the mapper's sentence (<see cref="LibraryFolders.Unmapped"/>).
    /// </summary>
    public static async Task<LibraryFolders> LoadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        // By library name, then the library, then the server's order of its folders: the order a
        // destination's library names come in (DestinationPathCheck takes the first match of each).
        var stored = await db.LibraryLocationFolders
            .AsNoTracking()
            .Join(
                db.LibraryLocations.Where(location => location.SourceKind == ProvenanceSourceKind.MediaLibrary && location.SourceConnectionId != null),
                folder => folder.LibraryLocationId,
                location => location.Id,
                (folder, location) => new
                {
                    LibraryName = location.Name,
                    LibraryId = location.Id,
                    IntegrationId = location.SourceConnectionId!.Value,
                    // MAM-30 — the integration's name now, which is what Integrations lists, for an unmapped folder's
                    // sentence.
                    IntegrationName = db.IntegrationConnections
                        .Where(integration => integration.Id == location.SourceConnectionId)
                        .Select(integration => integration.Name)
                        .FirstOrDefault(),
                    folder.Position,
                    folder.ServicePath,
                })
            .OrderBy(folder => folder.LibraryName)
            .ThenBy(folder => folder.LibraryId)
            .ThenBy(folder => folder.Position)
            .ToListAsync(cancellationToken);
        if (stored.Count == 0)
            return new LibraryFolders([], 0, []);

        // Batched, no N+1: the mappings of every integration that reported a library, in one query.
        var integrationIds = stored.Select(folder => folder.IntegrationId).Distinct().ToList();
        var pairsByIntegration = (await db.PathMappings
                .AsNoTracking()
                .Where(mapping => integrationIds.Contains(mapping.IntegrationConnectionId))
                .ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(group => group.Key, group => ToPairs(group));

        var mapped = new List<LibraryFolder>();
        var unmapped = new List<UnmappedLibraryFolder>();
        foreach (var folder in stored)
        {
            var pairs = pairsByIntegration.TryGetValue(folder.IntegrationId, out var found) ? found : [];
            var result = PathMapper.Map(folder.ServicePath, pairs);
            if (result.HasAppPath)
                mapped.Add(new LibraryFolder(folder.LibraryName, result.AppPath!, folder.IntegrationId, folder.ServicePath));
            else
                unmapped.Add(new UnmappedLibraryFolder(folder.LibraryName, folder.ServicePath, folder.IntegrationName ?? "", result.Reason!));
        }

        return new LibraryFolders(mapped, stored.Count, unmapped);
    }

    private static List<PathMappingPair> ToPairs(IEnumerable<PathMappingEntity> mappings) =>
        [.. mappings.OrderBy(mapping => mapping.Position).Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))];
}
