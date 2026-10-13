using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
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

    /// <summary>
    /// MAM-44 — every such name with its place among the libraries' folders, sorted into counted, extras and ignored
    /// (<see cref="UnreportedFileSet"/>): one read of the folders for them all.
    /// </summary>
    public static async Task<UnreportedFileSet> LoadSetAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var names = await LoadAsync(db, cancellationToken);
        if (names.Count == 0)
            return new UnreportedFileSet(names, new Dictionary<Guid, UnreportedFilePlaceDto>());

        var folders = (await LibraryFolderReader.LoadAsync(db, cancellationToken)).Mapped
            .Select(folder => (folder.LibraryName, Path: NormalizedPath.TryParse(folder.AppPath, out var path, out _) ? path : null))
            .Where(folder => folder.Path is not null)
            .Select(folder => (folder.LibraryName, Path: folder.Path!))
            .ToList();
        return new UnreportedFileSet(names, names.ToDictionary(name => name.Id, name => PlaceOf(name, folders)));
    }

    /// <summary>Whether the name's own path is one the app can use: the scan stores the app's path, which maps as it
    /// is.</summary>
    public static bool HasAppPathOf(PhysicalFileEntity name) => PathMapper.Map(name.ServicePath, []).HasAppPath;

    /// <summary>
    /// MAM-42 — where <paramref name="name"/> sits among <paramref name="folders"/> — the libraries' folders with a path
    /// in the app, by library name (<see cref="LibraryFolderReader"/>): under the first that holds it, under none, or
    /// unknown when there are none to compare with. MAM-43 — under one, an extras folder's name or an extra's name says
    /// why the library skipped it (<see cref="LibraryExtraNames"/>), the folder first, as it covers every video in it.
    /// </summary>
    private static UnreportedFilePlaceDto PlaceOf(PhysicalFileEntity name, List<(string LibraryName, NormalizedPath Path)> folders)
    {
        if (folders.Count == 0)
            return new UnreportedFilePlaceDto(UnreportedFilePlaceKind.FoldersUnknown, null);
        if (NormalizedPath.TryParse(PathMapper.Map(name.ServicePath, []).AppPath, out var path, out _)
            && folders.FirstOrDefault(folder => path.IsUnder(folder.Path)) is { LibraryName: { } libraryName, Path: { } libraryFolder })
        {
            if (LibraryExtraNames.ExtrasFolderOf(path, libraryFolder) is { } extrasFolder)
                return new UnreportedFilePlaceDto(UnreportedFilePlaceKind.ExtrasFolder, libraryName, extrasFolder);
            if (LibraryExtraNames.ExtraNameOf(path.Segments[^1]) is { } extraName)
                return new UnreportedFilePlaceDto(UnreportedFilePlaceKind.ExtraName, libraryName, extraName);
            return new UnreportedFilePlaceDto(UnreportedFilePlaceKind.LibraryFolder, libraryName);
        }

        return new UnreportedFilePlaceDto(UnreportedFilePlaceKind.NoLibraryFolder, null);
    }
}
