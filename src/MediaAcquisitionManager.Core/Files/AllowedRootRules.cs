using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// What the allowed roots may be when the installation owner saves them (MAM-9, ADR 0009): the folders inside the
/// app where it may read files. Each rule answers one readable sentence, or <c>null</c> when the list is
/// acceptable — the rules-class pattern of <see cref="PathMappingRules"/>.
/// </summary>
/// <remarks>
/// <para>
/// An empty list is accepted: it is the default, and it means the app reads nothing.
/// </para>
/// <para>
/// Refused even though harmless to the checks: a root inside another (the outer one already allows
/// it, and two entries for one folder only make the next reader wonder which one counts) and the whole
/// file system (an allowed root is a promise about where the media is; <c>/</c> promises nothing).
/// </para>
/// </remarks>
public static class AllowedRootRules
{
    /// <summary>A handful of mounts per installation; bounds the pairwise checks and the page.</summary>
    public const int MaximumCount = 20;

    /// <summary>Longer than any mount point anyone types, the <see cref="PathMappingRules"/> bound.</summary>
    public const int MaximumPathLength = PathMappingRules.MaximumPathLength;

    private const string TooManyError = "The app can have at most 20 media folders.";
    private const string BlankError = "Each media folder needs its path.";
    private const string TooLongError = "A media folder's path can be at most 1024 characters long.";
    private const string ControlCharacterError = "A media folder's path can't contain line breaks or other control characters.";

    /// <summary>
    /// The style of the paths this app opens: the operating system it runs on. A root of the other
    /// style could never contain a file the app opens.
    /// </summary>
    public static PathStyle PlatformStyle => OperatingSystem.IsWindows() ? PathStyle.Windows : PathStyle.Posix;

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="paths"/>, or <c>null</c>. Pass the paths as
    /// typed: surrounding whitespace is ignored. The first failure wins — the rules of each path in turn,
    /// then those between paths — and a path that parsed is echoed in its canonical form.
    /// </summary>
    /// <param name="paths">The folders as the owner typed them.</param>
    /// <param name="appStyle">The style of the paths the app opens; <see cref="PlatformStyle"/> outside
    /// tests.</param>
    public static string? Validate(IReadOnlyList<string?> paths, PathStyle appStyle)
    {
        if (paths.Count > MaximumCount)
            return TooManyError;

        var parsed = new List<NormalizedPath>(paths.Count);
        foreach (var path in paths)
        {
            var text = path?.Trim() ?? "";
            if (text.Length == 0)
                return BlankError;
            if (text.Length > MaximumPathLength)
                return TooLongError;
            if (text.Any(char.IsControl))
                return ControlCharacterError;
            if (!NormalizedPath.TryParse(text, out var root, out var error))
            {
                return error == PathParseError.DotSegment
                    ? $"“{text}” contains . or .. as a folder name. Write the folder's full path without them."
                    : $"“{text}” is not a full path. Start it with {Example(appStyle, root: true)}.";
            }

            if (root.Style != appStyle)
            {
                return $"“{root}” is a {StyleName(root.Style)} path, but this app runs on {StyleName(appStyle)}. " +
                    $"Give the folder's path inside the app, such as {Example(appStyle, root: false)}.";
            }

            if (root.Segments.Count == 0)
                return $"“{root}” is the whole file system. Name the folder the media is in, such as {Example(appStyle, root: false)}.";

            parsed.Add(root);
        }

        for (var first = 0; first < parsed.Count; first++)
        {
            for (var second = first + 1; second < parsed.Count; second++)
            {
                if (parsed[first].IsSameAs(parsed[second]))
                    return $"“{parsed[first]}” is listed twice. Keep one.";
            }
        }

        foreach (var outer in parsed)
        {
            foreach (var inner in parsed)
            {
                if (inner.IsUnder(outer) && !inner.IsSameAs(outer))
                    return $"“{inner}” is inside “{outer}”, which already allows it. Keep only “{outer}”.";
            }
        }

        return null;
    }

    /// <summary>The paths trimmed and in canonical form, in the order given. Only for paths
    /// <see cref="Validate"/> accepted.</summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string?> paths) =>
        [.. paths.Select(path => NormalizedPath.TryParse(path?.Trim(), out var root, out var error)
            ? root.ToString()
            : throw new ArgumentException($"An allowed root was normalised before it was validated ({error}).", nameof(paths)))];

    private static string StyleName(PathStyle style) => style == PathStyle.Windows ? "Windows" : "Linux";

    private static string Example(PathStyle style, bool root) => (style, root) switch
    {
        (PathStyle.Windows, true) => @"a drive letter such as D:\",
        (PathStyle.Windows, false) => @"D:\Media",
        (_, true) => "/",
        _ => "/media",
    };
}
