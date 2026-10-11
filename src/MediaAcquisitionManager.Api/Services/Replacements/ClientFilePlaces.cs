using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24, MAM-25 — where a download client's file is inside the app (ADR 0031's "read by path"): its job's save path
/// joined with the file's path in the job, mapped through the client's own pairs as a whole (<see cref="PathMapper"/>,
/// which also refuses a <c>..</c> in a name). One rule for telling which torrents hold an old copy and for placing a
/// replacement's new files, so the two can't place one file in two places.
/// </summary>
/// <remarks>
/// Never the save path's app path with the name appended: a pair deeper than the save path wouldn't map then. And never
/// the job's name, which the adapter cleans for display. Where a path says to look, not proof (ADR 0003): the disk
/// decides what is there.
/// </remarks>
public static class ClientFilePlaces
{
    /// <summary>The path mappings of <paramref name="client"/>, loaded with it, in their order.</summary>
    public static IReadOnlyList<PathMappingPair> PairsOf(IntegrationConnectionEntity client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return [.. client.PathMappings.OrderBy(mapping => mapping.Position).Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))];
    }

    /// <summary>
    /// The file <paramref name="relativePath"/> — <c>/</c>-separated, exactly as the client names it under its job's
    /// folder — of a job saving to <paramref name="savePath"/>, mapped into the app through <paramref name="pairs"/>.
    /// </summary>
    public static PathMapResult Map(NormalizedPath savePath, string relativePath, IReadOnlyList<PathMappingPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(savePath);
        ArgumentNullException.ThrowIfNull(relativePath);
        // Joined as the client names it and mapped whole: a ".." in a name maps to nothing rather than somewhere else.
        return PathMapper.Map(savePath.Append(relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToString(), pairs);
    }

    /// <summary>
    /// MAM-26 — whether a client with <paramref name="pairs"/> can name a file inside <paramref name="roots"/> (ADR 0031:
    /// the read before a move asks "each download client whose paths reach the allowed folders"): with no pairs, its paths
    /// are the app's own and may lie anywhere; with pairs, one of their app-side paths must lie under a root or hold one.
    /// </summary>
    /// <remarks>
    /// Every path a client names maps through its pairs (<see cref="PathMapper"/>), so a client whose pairs all lead
    /// elsewhere — a seedbox mapped to <c>/srv/seedbox</c> — names nothing inside the roots, and its torrents saving where
    /// no pair reaches are no uncertainty about them. An app-side path that doesn't parse reaches nothing.
    /// </remarks>
    public static bool Reaches(IReadOnlyList<PathMappingPair> pairs, IReadOnlyList<NormalizedPath> roots)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(roots);
        if (pairs.Count == 0)
            return true;

        return pairs.Any(pair =>
            NormalizedPath.TryParse(pair.AppPrefix, out var appPrefix, out _)
            && roots.Any(root => appPrefix.IsUnder(root) || root.IsUnder(appPrefix)));
    }
}
