using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// What the allowed roots may be when the installation owner saves them (MAM-9, ADR 0009): the folders inside the
/// app where it may read files. Each rule answers why the list is refused, as a message the Api renders (ADR 0042), or
/// <c>null</c> when it is acceptable — the rules-class pattern of <see cref="PathMappingRules"/>.
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

    private static readonly Message TooManyError = Message.Of("files.allowed-roots.too-many", ("count", MaximumCount));
    private static readonly Message BlankError = Message.Of("files.allowed-roots.path-missing");
    private static readonly Message TooLongError = Message.Of("files.allowed-roots.too-long", ("count", MaximumPathLength));
    private static readonly Message ControlCharacterError = Message.Of("files.allowed-roots.control-characters");

    /// <summary>
    /// The style of the paths this app opens: the operating system it runs on. A root of the other
    /// style could never contain a file the app opens.
    /// </summary>
    public static PathStyle PlatformStyle => OperatingSystem.IsWindows() ? PathStyle.Windows : PathStyle.Posix;

    /// <summary>
    /// Why <paramref name="paths"/> are refused, or <c>null</c>. Pass the paths as
    /// typed: surrounding whitespace is ignored. The first failure wins — the rules of each path in turn,
    /// then those between paths — and a path that parsed is echoed in its canonical form.
    /// </summary>
    /// <param name="paths">The folders as the owner typed them.</param>
    /// <param name="appStyle">The style of the paths the app opens; <see cref="PlatformStyle"/> outside
    /// tests.</param>
    public static Message? Validate(IReadOnlyList<string?> paths, PathStyle appStyle)
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
                    ? PathMappingRules.DotSegmentError(text)
                    : OnPlatform(appStyle, "files.allowed-roots.not-full-path-windows", "files.allowed-roots.not-full-path-linux", text);
            }

            // Root and app styles differ, so the app's style names the shape: a Linux path on Windows or the other way.
            if (root.Style != appStyle)
                return OnPlatform(appStyle, "files.allowed-roots.linux-path-on-windows", "files.allowed-roots.windows-path-on-linux", root.ToString());

            if (root.Segments.Count == 0)
                return OnPlatform(appStyle, "files.allowed-roots.whole-file-system-windows", "files.allowed-roots.whole-file-system-linux", root.ToString());

            parsed.Add(root);
        }

        for (var first = 0; first < parsed.Count; first++)
        {
            for (var second = first + 1; second < parsed.Count; second++)
            {
                if (parsed[first].IsSameAs(parsed[second]))
                    return Message.Of("files.allowed-roots.listed-twice", ("path", parsed[first].ToString()));
            }
        }

        foreach (var outer in parsed)
        {
            foreach (var inner in parsed)
            {
                if (inner.IsUnder(outer) && !inner.IsSameAs(outer))
                    return Message.Of("files.allowed-roots.nested", ("inner", inner.ToString()), ("outer", outer.ToString()));
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

    // MAM-47 — one whole sentence per platform rather than "Windows"/"Linux" and an example path filled into one frame:
    // the example is words of the template, never a value, and each language words the platform its own way.
    private static Message OnPlatform(PathStyle appStyle, string windowsKey, string linuxKey, string path) =>
        appStyle == PathStyle.Windows ? Message.Of(windowsKey, ("path", path)) : Message.Of(linuxKey, ("path", path));
}
