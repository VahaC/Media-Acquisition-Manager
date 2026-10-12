using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — what a library notice says and how it is sent (ADR 0030: Jellyfin hears <c>POST /Library/Media/Updated</c>
/// after every move and every restore), as a replacement's notice (MAM-26, <see cref="ReplacementLibraryNotice"/>) and a
/// removal's both say it: each name in the quarantine as deleted at its old place, each name put back as created there, by
/// library and change, each path once, never a quarantine path.
/// </summary>
/// <remarks>
/// The place is the library folder that holds the old path (<see cref="ReplacementLibraryPlaces"/>, ADR 0003). A video one
/// library holds goes by its copy's path as that library reported it — the spelling the library finds its item by —;
/// otherwise, and for a companion, which no library reported, by each holding library's own path of it.
/// </remarks>
internal static class QuarantineNotices
{
    /// <summary>
    /// What <paramref name="names"/> tell each library, by library and change: the paths in the library's own terms, each
    /// once, in the names' order; and how many names no library folder in <paramref name="folders"/> holds, which no
    /// library has anything to hear of. A name neither in the quarantine nor back is no news.
    /// </summary>
    /// <param name="names">The names, in their order.</param>
    /// <param name="reportedPathOf">A video's path as its library reported it, when known.</param>
    /// <param name="folders">The library folders the app knows.</param>
    public static (Dictionary<(Guid IntegrationId, LibraryFileChange Change), IReadOnlyList<string>> Notices, int Unplaced) Of<TName>(
        IEnumerable<TName> names, Func<TName, string?> reportedPathOf, IReadOnlyList<LibraryFolder> folders)
        where TName : QuarantinedNameEntity
    {
        var paths = new Dictionary<(Guid IntegrationId, LibraryFileChange Change), List<string>>();
        var unplaced = 0;
        foreach (var name in names.Where(name => name.State is ReplacementNameState.Quarantined or ReplacementNameState.Restored))
        {
            var change = name.State == ReplacementNameState.Quarantined ? LibraryFileChange.Deleted : LibraryFileChange.Created;
            var places = NormalizedPath.TryParse(name.AppPath, out var appPath, out _) ? ReplacementLibraryPlaces.Of(appPath, folders) : [];
            if (places.Count == 0)
            {
                // In no library folder the app knows: no library holds it, so none has anything to hear.
                unplaced++;
                continue;
            }

            if (name.Kind == ReplacementNameKind.Video && places is [var only] && reportedPathOf(name) is { } servicePath)
            {
                PathsOf(paths, only.IntegrationId, change).Add(servicePath);
                continue;
            }

            foreach (var place in places)
                PathsOf(paths, place.IntegrationId, change).Add(place.LibraryPath);
        }

        return (paths.ToDictionary(notice => notice.Key, notice => (IReadOnlyList<string>)[.. notice.Value.Distinct(StringComparer.Ordinal)]), unplaced);
    }

    /// <summary>
    /// Tells each library its <paramref name="notices"/>, one request per library and change: whether one couldn't be told
    /// now (switched off, unreachable, failing) and whether one can never be told (removed, of a type without the
    /// capability). Never throws on a library's failure.
    /// </summary>
    public static async Task<(bool Waiting, bool GivenUp)> TellAsync(
        IReplacementLibraryReader library,
        IReadOnlyDictionary<(Guid IntegrationId, LibraryFileChange Change), IReadOnlyList<string>> notices,
        CancellationToken cancellationToken)
    {
        var waiting = false;
        var givenUp = false;
        foreach (var ((integrationId, change), paths) in notices)
        {
            var answer = await library.TellAsync(integrationId, paths, change, cancellationToken);
            waiting |= answer.Status == ReplacementCheckStatus.Wait;
            givenUp |= answer.Status == ReplacementCheckStatus.Review;
        }

        return (waiting, givenUp);
    }

    private static List<string> PathsOf(
        Dictionary<(Guid IntegrationId, LibraryFileChange Change), List<string>> paths, Guid integrationId, LibraryFileChange change)
    {
        if (!paths.TryGetValue((integrationId, change), out var list))
            paths[(integrationId, change)] = list = [];
        return list;
    }
}
