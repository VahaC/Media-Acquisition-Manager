using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// What an integration's path mappings may be when the owner saves them (MAM-7, ADR 0003). Each rule
/// answers one readable sentence, or <c>null</c> when the mappings are acceptable.
/// </summary>
/// <remarks>
/// Accepted on purpose: no pairs (the identity), nested pairs that agree
/// (<c>/downloads → /media</c> with <c>/downloads/movies → /media/movies</c>), two service folders
/// mapped to one app folder (two mounts of one folder), and POSIX folders that differ only in letter
/// case (two folders on ext4). Refused even when they agree as typed: nested pairs from Windows service
/// paths onto POSIX app paths, which <see cref="PathMapper"/> would find ambiguous for a path the
/// service reports in another letter case.
/// </remarks>
public static class PathMappingRules
{
    /// <summary>A handful of mounts per service; bounds the pairwise overlap check and the dialog.</summary>
    public const int MaximumCount = 20;

    /// <summary>Longer than any mount point anyone types; bounds what is stored.</summary>
    public const int MaximumPathLength = 1024;

    private const string TooManyError = "An integration can have at most 20 path mappings.";
    private const string AppPathMissingError = "Each path mapping needs the folder as this app sees it.";
    private const string TooLongError = "A path in a path mapping can be at most 1024 characters long.";
    private const string ControlCharacterError = "A path in a path mapping can't contain line breaks or other control characters.";

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="pairs"/> for <paramref name="adapter"/>,
    /// or <c>null</c>. Pass the pairs as typed: surrounding whitespace is ignored. The first failure
    /// wins — the rules of each pair in turn, then those between pairs — and a path that parsed is
    /// echoed in its canonical form.
    /// </summary>
    public static string? Validate(IReadOnlyList<PathMappingPair> pairs, IntegrationAdapterDescriptor adapter)
    {
        var display = adapter.DisplayName;
        // An indexer's paths are never trusted (specification §4), so a mapping for one would only
        // look like it does something.
        if (pairs.Count > 0 && !adapter.ReportsFilePaths)
            return $"{display} reports no file paths, so it takes no path mappings.";
        if (pairs.Count > MaximumCount)
            return TooManyError;

        var parsed = new List<(NormalizedPath Service, NormalizedPath App)>(pairs.Count);
        foreach (var pair in pairs)
        {
            var servicePrefix = pair?.ServicePrefix?.Trim() ?? "";
            var appPrefix = pair?.AppPrefix?.Trim() ?? "";
            if (servicePrefix.Length == 0)
                return $"Each path mapping needs the folder as {display} sees it.";
            if (appPrefix.Length == 0)
                return AppPathMissingError;
            if (servicePrefix.Length > MaximumPathLength || appPrefix.Length > MaximumPathLength)
                return TooLongError;
            if (servicePrefix.Any(char.IsControl) || appPrefix.Any(char.IsControl))
                return ControlCharacterError;
            if (Parse(servicePrefix, out var service) is { } serviceError)
                return serviceError;
            if (Parse(appPrefix, out var app) is { } appError)
                return appError;
            parsed.Add((service!, app!));
        }

        // One service runs on one operating system; a mix means one of the paths was typed for
        // another service, and the two styles never cover each other.
        if (parsed.Select(pair => pair.Service.Style).Distinct().Count() > 1)
            return $"All the paths of {display} in one integration must be of one kind: Linux-style (/…) or Windows-style (D:\\…).";

        // The unique index is a case-sensitive backstop; this also catches D:\DL and d:\dl, which
        // name one Windows folder.
        for (var first = 0; first < parsed.Count; first++)
        {
            for (var second = first + 1; second < parsed.Count; second++)
            {
                if (parsed[first].Service.IsSameAs(parsed[second].Service))
                    return $"The path “{parsed[first].Service}” of {display} is mapped twice. Keep one mapping for it.";
            }
        }

        // Nested pairs must agree, so resolving a path never has to pick one (PathMapper refuses to).
        foreach (var outer in parsed)
        {
            foreach (var inner in parsed)
            {
                if (!inner.Service.IsUnder(outer.Service) || inner.Service.IsSameAs(outer.Service))
                    continue;

                // MAM-7 — a Windows service path matches in any letter case, and the outer pair copies
                // the case the service reports into a POSIX path, where case counts; the inner pair
                // gives its app path as typed. The two agree only while the service reports the case
                // the owner typed: with D:\Torrents → /media and D:\Torrents\movies → /media/movies,
                // a reported D:\Torrents\Movies\x.mkv maps to /media/Movies/x.mkv and
                // /media/movies/x.mkv at once, and PathMapper answers Ambiguous.
                if (outer.Service.Style == PathStyle.Windows && outer.App.Style == PathStyle.Posix)
                {
                    return $"“{inner.Service}” is inside “{outer.Service}”. " +
                        $"With Windows paths on the {display} side and Linux paths in this app, keep only one of them.";
                }

                var viaOuter = outer.App.Append(inner.Service.SegmentsAfter(outer.Service));
                if (!viaOuter.IsSameAs(inner.App))
                {
                    return $"The mappings for “{outer.Service}” and “{inner.Service}” send “{inner.Service}” to different places: " +
                        $"“{viaOuter}” and “{inner.App}”. Remove one, or make them agree.";
                }
            }
        }

        return null;
    }

    /// <summary>The pairs trimmed and in canonical form, in the order given. Only for pairs
    /// <see cref="Validate"/> accepted.</summary>
    public static IReadOnlyList<PathMappingPair> Normalize(IReadOnlyList<PathMappingPair> pairs) =>
        [.. pairs.Select(pair => new PathMappingPair(Canonical(pair.ServicePrefix), Canonical(pair.AppPrefix)))];

    private static string? Parse(string text, out NormalizedPath? path)
    {
        if (NormalizedPath.TryParse(text, out path, out var error))
            return null;

        return error == PathParseError.DotSegment
            ? $"“{text}” contains . or .. as a folder name. Write the folder's full path without them."
            : $"“{text}” is not a full path. Start it with / (Linux) or a drive letter such as D:\\ (Windows).";
    }

    private static string Canonical(string text) =>
        NormalizedPath.TryParse(text.Trim(), out var path, out var error)
            ? path.ToString()
            : throw new ArgumentException($"A path mapping was normalised before it was validated ({error}).", nameof(text));
}
