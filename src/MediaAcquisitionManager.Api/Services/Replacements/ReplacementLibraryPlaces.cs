using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — which media library holds a file inside the app, and the file's path in that library's own terms (ADR 0003,
/// ADR 0035): the library folders the app knows (<c>LibraryFolderReader</c>), each mapped into the app through its own
/// integration's mappings, compared with the file's app path folder by folder.
/// </summary>
/// <remarks>
/// The path in the library is the folder's own path as the library reported it, followed by the file's folders under the
/// folder's app path — no mapping run backwards, which could honestly give several paths: two library folders may map to
/// one app folder (two mounts of one folder). The library is then asked by exactly that spelling.
/// </remarks>
public static class ReplacementLibraryPlaces
{
    /// <summary>
    /// One place per library whose folders hold <paramref name="appPath"/> — a library being an integration's library of
    /// one name —, each through the deepest of its folders that does, in the order of <paramref name="folders"/>; none
    /// when no folder holds it. A folder whose integration or library path isn't known holds nothing here.
    /// </summary>
    public static IReadOnlyList<ReplacementLibraryPlace> Of(NormalizedPath appPath, IReadOnlyList<LibraryFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        ArgumentNullException.ThrowIfNull(folders);
        var holding = new List<(Guid IntegrationId, string LibraryName, NormalizedPath AppFolder, NormalizedPath LibraryFolder)>();
        foreach (var folder in folders)
        {
            if (folder is { IntegrationId: { } integrationId, ServicePath: { } servicePath }
                && NormalizedPath.TryParse(folder.AppPath, out var appFolder, out _)
                && NormalizedPath.TryParse(servicePath, out var libraryFolder, out _)
                && appPath.IsUnder(appFolder))
                holding.Add((integrationId, folder.LibraryName, appFolder, libraryFolder));
        }

        return
        [
            .. holding
                .GroupBy(folder => (folder.IntegrationId, folder.LibraryName))
                .Select(library =>
                {
                    // A library may list a folder inside another of its own: the deepest names the file as the library
                    // reads it under that folder.
                    var deepest = library.MaxBy(folder => folder.AppFolder.Segments.Count);
                    return new ReplacementLibraryPlace(
                        library.Key.IntegrationId,
                        library.Key.LibraryName,
                        deepest.LibraryFolder.Append(appPath.SegmentsAfter(deepest.AppFolder)).ToString());
                }),
        ];
    }
}
