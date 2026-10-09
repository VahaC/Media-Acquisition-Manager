using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementGates"/>
/// <remarks>
/// <para>
/// A tick is kept by the folder's app path, so its meaning is worked out here on every read: it counts only while the
/// path is still a library folder the mappings bring into the app and lies inside an allowed root (ADR 0033 — a folder
/// that leaves the libraries or the allowed roots loses its tick's effect). Paths are compared with
/// <see cref="NormalizedPath"/>, folder by folder in the path's own letter-case rules, never as text.
/// </para>
/// <para>
/// One folder listed by several libraries is one entry with every library's name: the tick belongs to the folder, and
/// the page shows one checkbox for it.
/// </para>
/// </remarks>
public sealed class ReplacementGates(ApplicationDbContext db) : IReplacementGates
{
    public async Task<ReplacementGateState> ReadAsync(CancellationToken cancellationToken = default)
    {
        // No row is off: the state of a fresh installation and of one upgraded to MAM-24.
        var isEnabled = await db.ReplacementSettings
            .AsNoTracking()
            .AnyAsync(settings => settings.Id == ReplacementSettingsEntity.SingletonId && settings.IsEnabled, cancellationToken);
        var roots = await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);
        var ticks = await db.ReplacementFolders
            .AsNoTracking()
            .OrderBy(tick => tick.Position)
            .Select(tick => tick.Path)
            .ToListAsync(cancellationToken);
        var libraryFolders = await LibraryFolderReader.LoadAsync(db, cancellationToken);

        var parsedRoots = Parsed(roots);
        var parsedTicks = Parsed(ticks);
        var folders = libraryFolders.Mapped
            .Select(folder => (folder.LibraryName, Path: NormalizedPath.TryParse(folder.AppPath, out var path, out _) ? path : null))
            .Where(folder => folder.Path is not null)
            // NormalizedPath's own equality: two spellings of one Windows folder are one folder.
            .GroupBy(folder => folder.Path!)
            .Select(group => new ReplacementGateFolder(
                group.Key,
                [.. group.Select(folder => folder.LibraryName).Distinct(StringComparer.Ordinal)],
                parsedTicks.Any(tick => tick.IsSameAs(group.Key)),
                parsedRoots.Any(root => group.Key.IsUnder(root)) ? ReplacementFolderStatus.Available : ReplacementFolderStatus.OutsideMediaFolders))
            .OrderBy(folder => folder.AppPath.ToString(), StringComparer.Ordinal)
            .ToList();
        // A stored tick that doesn't parse (a hand-edited row) matches no folder: it is listed so it can be unticked.
        var staleTicks = ticks
            .Where(tick => !NormalizedPath.TryParse(tick, out var path, out _) || !folders.Any(folder => folder.AppPath.IsSameAs(path)))
            .ToList();

        return new ReplacementGateState(
            isEnabled,
            roots,
            ticks,
            folders,
            staleTicks,
            libraryFolders.StoredCount - libraryFolders.Mapped.Count);
    }

    // The paths that parse; a stored one that doesn't (a hand-edited row) can't contain anything.
    private static List<NormalizedPath> Parsed(IEnumerable<string> paths) =>
        [.. paths.Select(text => NormalizedPath.TryParse(text, out var path, out _) ? path : null).OfType<NormalizedPath>()];
}
