namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// Works out where a path a service reports is inside the app, through that integration's current
/// prefix mappings (ADR 0003). Service → app is the only direction Phase A needs.
/// </summary>
/// <remarks>
/// Every pair that covers the path is applied, and they must agree. The longest prefix never wins on
/// its own: that would be the silent guess ADR 0003 forbids. Save-time rules
/// (<see cref="PathMappingRules"/>) keep pairs saved through the API from disagreeing, but a row of an
/// older build or a hand-edited database still gets an honest <see cref="PathMapStatus.Ambiguous"/>.
/// MAM-11 and Phase C treat anything without an app path as "automatic replacement off".
/// </remarks>
public static class PathMapper
{
    /// <summary>
    /// Where <paramref name="servicePath"/> is inside the app. Never throws: a stored pair that doesn't
    /// parse is skipped, and a path that doesn't is <see cref="PathMapStatus.Invalid"/>.
    /// </summary>
    public static PathMapResult Map(string servicePath, IReadOnlyList<PathMappingPair> pairs)
    {
        if (!NormalizedPath.TryParse(servicePath, out var path, out _))
            return new PathMapResult(PathMapStatus.Invalid, null, $"“{servicePath}” is not a full path the app can map.");

        // The owner declared no mapping, so the service sees the files where the app does.
        if (pairs.Count == 0)
            return new PathMapResult(PathMapStatus.Identity, path.ToString(), null);

        var results = new List<NormalizedPath>();
        foreach (var pair in pairs)
        {
            if (pair is null
                || !NormalizedPath.TryParse(pair.ServicePrefix, out var servicePrefix, out _)
                || !NormalizedPath.TryParse(pair.AppPrefix, out var appPrefix, out _)
                || !path.IsUnder(servicePrefix))
                continue;

            var remainder = path.SegmentsAfter(servicePrefix);
            // A POSIX folder name may hold a backslash; written under a Windows prefix it would turn
            // into two folders and name a different place.
            if (appPrefix.Style == PathStyle.Windows && remainder.FirstOrDefault(segment => segment.Contains('\\')) is { } unwritable)
            {
                return new PathMapResult(PathMapStatus.Invalid, null,
                    $"“{path}” can't be written under “{appPrefix}”: its folder name “{unwritable}” contains \\, which separates folders in a Windows path.");
            }

            var result = appPrefix.Append(remainder);
            if (!results.Any(known => known.IsSameAs(result)))
                results.Add(result);
        }

        return results.Count switch
        {
            // Pairs exist, so identity is not a fallback: the owner said how this service's paths
            // map, and this one isn't covered.
            0 => new PathMapResult(PathMapStatus.NotCovered, null, $"No path mapping of this integration covers “{path}”."),
            1 => new PathMapResult(PathMapStatus.Mapped, results[0].ToString(), null),
            _ => new PathMapResult(PathMapStatus.Ambiguous, null,
                $"The path mappings of this integration send “{path}” to different places: “{results[0]}” and “{results[1]}”."),
        };
    }
}
