using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — the name of the folder a library file sits in, as one more query when the catalog is searched for a work
/// without a TMDb id: "Tom &amp; Jerry e001-e161 (1940-1967)" names a short's series where its file name doesn't.
/// </summary>
/// <remarks>
/// A query hint only, never a fact (ADR 0017: a name decides nothing). A file directly in a library's root folder has
/// no folder of its own — the root's name ("cartoons") would be the same query for every work in it — and a path that
/// doesn't parse gives none. Folder names are compared by <see cref="NormalizedPath"/>, never as text.
/// </remarks>
public static class FolderHint
{
    /// <summary>
    /// The name of <paramref name="servicePath"/>'s parent folder; <c>null</c> when that folder is one of
    /// <paramref name="libraryRootPaths"/>, the file sits at the top of its drive, or the path isn't a full path.
    /// </summary>
    public static string? Of(string servicePath, IReadOnlyList<string> libraryRootPaths)
    {
        ArgumentNullException.ThrowIfNull(libraryRootPaths);
        if (!NormalizedPath.TryParse(servicePath, out var path, out _) || path.Segments.Count < 2)
            return null;

        var parent = path with { Segments = [.. path.Segments.Take(path.Segments.Count - 1)] };
        var inRoot = libraryRootPaths.Any(root => NormalizedPath.TryParse(root, out var parsed, out _) && parent.IsSameAs(parsed));
        return inRoot ? null : path.Segments[^2];
    }
}
