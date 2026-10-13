using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// MAM-43 — the files of a Blu-ray or DVD a library reports as one version by its folder
/// (<see cref="LibraryVersion.DiscFolderPath"/>): the media files the app finds in the disc's own <c>BDMV</c> or
/// <c>VIDEO_TS</c> folder, named in the library's terms — the library's folder with the clip's place inside it.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin gives a disc's folder as the version's path and names no file of it, so without this the film had no file:
/// its space counted as "in no library", the folder scan listed its clip as a file no library reported, and the Library
/// page showed the version empty. Listing the clips here, before the snapshot is stored, makes them the version's names
/// like any file a library reports, so every reader that knows a library's files — the scan, the room the files take,
/// the inspection, the duplicates — counts them without a rule of its own. The alternative, the scan tying the clips it
/// finds to the version, would have left a library's names without the library's provenance and taught each of those
/// readers an exception.
/// </para>
/// <para>
/// Read-only and inside the owner's media folders only (specification §4): the disc folder is mapped into the app through
/// the integration's own mappings (ADR 0003) and walked with the allowed roots' checks
/// (<see cref="IFileInventory.ListMediaFilesUnderAsync"/>). A folder that doesn't map, lies outside the roots or is away
/// gives the version no file, as before — never a failed sync: the library itself was read whole. Two versions naming
/// one folder list it once.
/// </para>
/// </remarks>
public static class LibraryDiscClips
{
    /// <summary><paramref name="items"/> with each disc version's clips as its files; the same list when no version is
    /// a disc, or no media folder is allowed.</summary>
    public static async Task<IReadOnlyList<LibraryItem>> FillAsync(
        ApplicationDbContext db,
        IFileInventory inventory,
        Guid integrationId,
        IReadOnlyList<LibraryItem> items,
        CancellationToken cancellationToken = default)
    {
        if (!items.Any(item => item.Versions.Any(IsDisc)))
            return items;

        var roots = await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);
        if (roots.Count == 0)
            return items;

        var pairs = (await db.PathMappings
                .AsNoTracking()
                .Where(mapping => mapping.IntegrationConnectionId == integrationId)
                .OrderBy(mapping => mapping.Position)
                .ToListAsync(cancellationToken))
            .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))
            .ToList();

        var clipsByFolder = new Dictionary<string, IReadOnlyList<LibraryFile>>(StringComparer.Ordinal);
        var filled = new List<LibraryItem>(items.Count);
        foreach (var item in items)
        {
            if (!item.Versions.Any(IsDisc))
            {
                filled.Add(item);
                continue;
            }

            var versions = new List<LibraryVersion>(item.Versions.Count);
            foreach (var version in item.Versions)
            {
                if (!IsDisc(version))
                {
                    versions.Add(version);
                    continue;
                }

                var folder = version.DiscFolderPath!;
                if (!clipsByFolder.TryGetValue(folder, out var clips))
                {
                    clips = await ListAsync(inventory, folder, pairs, roots, cancellationToken);
                    clipsByFolder[folder] = clips;
                }

                versions.Add(version with { Files = clips });
            }

            filled.Add(item with { Versions = versions });
        }

        return filled;
    }

    // A disc the library named no file of: a library that names the clips itself keeps its own word.
    private static bool IsDisc(LibraryVersion version) => version.DiscFolderPath is not null && version.Files.Count == 0;

    /// <summary>
    /// The clips of the disc at <paramref name="servicePath"/>, in path order (ordinal): each media file the walk of its
    /// app folder gives inside a <c>BDMV</c> or <c>VIDEO_TS</c> folder right under it, where Jellyfin looks for one. A
    /// sample or a bonus video beside those folders is not the disc's.
    /// </summary>
    private static async Task<IReadOnlyList<LibraryFile>> ListAsync(
        IFileInventory inventory,
        string servicePath,
        IReadOnlyList<PathMappingPair> pairs,
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        if (!NormalizedPath.TryParse(servicePath, out var serviceFolder, out _)
            || PathMapper.Map(servicePath, pairs) is not { HasAppPath: true } mapped
            || !NormalizedPath.TryParse(mapped.AppPath, out var appFolder, out _))
            return [];

        var clips = new List<LibraryFile>();
        await foreach (var entry in inventory.ListMediaFilesUnderAsync(appFolder.ToString(), roots, cancellationToken))
        {
            if (entry.Status != FileListingStatus.MediaFile
                || !NormalizedPath.TryParse(entry.AppPath, out var appPath, out _)
                || !appPath.IsUnder(appFolder))
                continue;

            var inside = appPath.SegmentsAfter(appFolder);
            if (inside.Count < 2 || !DiscFolders.IsDiscFolderName(inside[0]))
                continue;

            // No size: the library reported none for the clip, and the inspection reads the one on the disk.
            clips.Add(new LibraryFile(serviceFolder.Append(inside).ToString(), null));
        }

        return [.. clips.OrderBy(clip => clip.ServicePath, StringComparer.Ordinal)];
    }
}
