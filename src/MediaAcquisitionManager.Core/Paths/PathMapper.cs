namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// Works out where a path a service reports is inside the app, through that integration's current
/// prefix mappings (ADR 0003). MAM-30 — and the way back: where a path of the app is as the service sees it
/// (<see cref="MapBack"/>), for a download client whose destinations are the media library's folders (ADR 0039).
/// </summary>
/// <remarks>
/// <para>
/// Every pair that covers the path is applied, and they must agree. The longest prefix never wins on
/// its own: that would be the silent guess ADR 0003 forbids. Save-time rules
/// (<see cref="PathMappingRules"/>) keep pairs saved through the API from disagreeing, but a row of an
/// older build or a hand-edited database still gets an honest <see cref="PathMapStatus.Ambiguous"/>.
/// MAM-11 and Phase C treat anything without an app path as "automatic replacement off".
/// </para>
/// <para>
/// MAM-30 — <see cref="MapBack"/> mirrors <see cref="Map"/> step for step, with the app prefix covering and the service
/// prefix written. Pairs the save rules accept agree backwards too where they nest, so a folder mapped back maps forward
/// onto itself; two service folders mounted onto one app folder (accepted, two mounts of one folder) lead back to two
/// places and are <see cref="PathMapStatus.Ambiguous"/>, never settled by a guess either.
/// </para>
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

    /// <summary>
    /// MAM-30 — where <paramref name="appPath"/> is as the service sees it: <see cref="Map"/> run the other way, each pair
    /// whose app prefix covers the path giving its service prefix with the rest of the path below it. Never throws: a
    /// stored pair that doesn't parse is skipped, and a path that doesn't is <see cref="PathMapStatus.Invalid"/>.
    /// </summary>
    public static ServicePathMapResult MapBack(string appPath, IReadOnlyList<PathMappingPair> pairs)
    {
        if (!NormalizedPath.TryParse(appPath, out var path, out _))
            return new ServicePathMapResult(PathMapStatus.Invalid, null, $"“{appPath}” is not a full path the app can map back.");

        // The owner declared no mapping, so the service sees the files where the app does.
        if (pairs.Count == 0)
            return new ServicePathMapResult(PathMapStatus.Identity, path.ToString(), null);

        var results = new List<NormalizedPath>();
        foreach (var pair in pairs)
        {
            // The app prefix covers by the app path's own rules: letter case counts in the Docker image's POSIX paths.
            if (pair is null
                || !NormalizedPath.TryParse(pair.ServicePrefix, out var servicePrefix, out _)
                || !NormalizedPath.TryParse(pair.AppPrefix, out var appPrefix, out _)
                || !path.IsUnder(appPrefix))
                continue;

            var remainder = path.SegmentsAfter(appPrefix);
            // Map's hazard the other way round: a POSIX folder name holding a backslash, written under a Windows
            // service prefix, would turn into two folders and name a different place.
            if (servicePrefix.Style == PathStyle.Windows && remainder.FirstOrDefault(segment => segment.Contains('\\')) is { } unwritable)
            {
                return new ServicePathMapResult(PathMapStatus.Invalid, null,
                    $"“{path}” can't be written under “{servicePrefix}”: its folder name “{unwritable}” contains \\, which separates folders in a Windows path.");
            }

            // Agreeing pairs collapse by the service's own rules: two Windows spellings in another letter case are one
            // folder, and the first pair's spelling is kept.
            var result = servicePrefix.Append(remainder);
            if (!results.Any(known => known.IsSameAs(result)))
                results.Add(result);
        }

        return results.Count switch
        {
            // Pairs exist, so identity is not a fallback, as in Map.
            0 => new ServicePathMapResult(PathMapStatus.NotCovered, null, $"No path mapping of this integration leads to “{path}”."),
            1 => new ServicePathMapResult(PathMapStatus.Mapped, results[0].ToString(), null),
            _ => new ServicePathMapResult(PathMapStatus.Ambiguous, null,
                $"The path mappings of this integration lead to “{path}” from different places: “{results[0]}” and “{results[1]}”."),
        };
    }
}
