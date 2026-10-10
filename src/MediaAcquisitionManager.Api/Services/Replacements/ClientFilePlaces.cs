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
}
