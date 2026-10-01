using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// Which library of a media server holds a file, from the file's path and the libraries' root folders
/// (MAM-8): the "Jellyfin library" filter of the Library page.
/// </summary>
/// <remarks>
/// Folder name by folder name (<see cref="NormalizedPath.IsUnder"/>), never as text, so
/// <c>/media/movies</c> doesn't hold <c>/media/movies2/x.mkv</c>. Only a single answer is one: two
/// libraries whose roots both cover the file (nested roots), or none, leave the file without a library
/// rather than a guess. So does a server that rewrites file paths but not library roots (Jellyfin's
/// server-wide path substitution): its paths are covered by no root.
/// </remarks>
public static class LibraryLocationMatcher
{
    /// <summary>
    /// The one location of <paramref name="locations"/> one of whose root folders covers
    /// <paramref name="servicePath"/>; <c>null</c> when none or more than one does, or the path isn't a
    /// full path. A root that doesn't parse covers nothing.
    /// </summary>
    public static LibraryLocation? Find(string servicePath, IReadOnlyList<LibraryLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        if (!NormalizedPath.TryParse(servicePath, out var path, out _))
            return null;

        LibraryLocation? found = null;
        foreach (var location in locations)
        {
            var covers = location.ServicePaths.Any(root => NormalizedPath.TryParse(root, out var parsed, out _) && path.IsUnder(parsed));
            if (!covers)
                continue;
            if (found is not null)
                return null;
            found = location;
        }

        return found;
    }
}
