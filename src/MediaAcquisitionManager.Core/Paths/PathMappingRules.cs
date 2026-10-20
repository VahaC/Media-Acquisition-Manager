using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// What an integration's path mappings may be when the owner saves them (MAM-7, ADR 0003). Each rule
/// answers why they are refused, as a message the Api renders (ADR 0042), or <c>null</c> when the mappings are
/// acceptable.
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

    private static readonly Message TooManyError = Message.Of("integrations.path-mapping.too-many", ("count", MaximumCount));
    private static readonly Message AppPathMissingError = Message.Of("integrations.path-mapping.app-path-missing");
    private static readonly Message TooLongError = Message.Of("integrations.path-mapping.too-long", ("count", MaximumPathLength));
    private static readonly Message ControlCharacterError = Message.Of("integrations.path-mapping.control-characters");

    /// <summary>
    /// Why <paramref name="pairs"/> are refused for <paramref name="adapter"/>,
    /// or <c>null</c>. Pass the pairs as typed: surrounding whitespace is ignored. The first failure
    /// wins — the rules of each pair in turn, then those between pairs — and a path that parsed is
    /// echoed in its canonical form.
    /// </summary>
    public static Message? Validate(IReadOnlyList<PathMappingPair> pairs, IntegrationAdapterDescriptor adapter)
    {
        var display = adapter.DisplayName;
        // An indexer's paths are never trusted (specification §4), so a mapping for one would only
        // look like it does something.
        if (pairs.Count > 0 && !adapter.ReportsFilePaths)
            return Message.Of("integrations.path-mapping.no-file-paths", ("service", display));
        if (pairs.Count > MaximumCount)
            return TooManyError;

        var parsed = new List<(NormalizedPath Service, NormalizedPath App)>(pairs.Count);
        foreach (var pair in pairs)
        {
            var servicePrefix = pair?.ServicePrefix?.Trim() ?? "";
            var appPrefix = pair?.AppPrefix?.Trim() ?? "";
            if (servicePrefix.Length == 0)
                return Message.Of("integrations.path-mapping.service-path-missing", ("service", display));
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
            return Message.Of("integrations.path-mapping.mixed-styles", ("service", display));

        // The unique index is a case-sensitive backstop; this also catches D:\DL and d:\dl, which
        // name one Windows folder.
        for (var first = 0; first < parsed.Count; first++)
        {
            for (var second = first + 1; second < parsed.Count; second++)
            {
                if (parsed[first].Service.IsSameAs(parsed[second].Service))
                    return Message.Of("integrations.path-mapping.mapped-twice", ("path", parsed[first].Service.ToString()), ("service", display));
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
                    return Message.Of(
                        "integrations.path-mapping.nested-windows-on-linux",
                        ("inner", inner.Service.ToString()),
                        ("outer", outer.Service.ToString()),
                        ("service", display));
                }

                var viaOuter = outer.App.Append(inner.Service.SegmentsAfter(outer.Service));
                if (!viaOuter.IsSameAs(inner.App))
                {
                    return Message.Of(
                        "integrations.path-mapping.nested-disagree",
                        ("outer", outer.Service.ToString()),
                        ("inner", inner.Service.ToString()),
                        ("viaOuter", viaOuter.ToString()),
                        ("innerApp", inner.App.ToString()));
                }
            }
        }

        return null;
    }

    /// <summary>The pairs trimmed and in canonical form, in the order given. Only for pairs
    /// <see cref="Validate"/> accepted.</summary>
    public static IReadOnlyList<PathMappingPair> Normalize(IReadOnlyList<PathMappingPair> pairs) =>
        [.. pairs.Select(pair => new PathMappingPair(Canonical(pair.ServicePrefix), Canonical(pair.AppPrefix)))];

    private static Message? Parse(string text, out NormalizedPath? path)
    {
        if (NormalizedPath.TryParse(text, out path, out var error))
            return null;

        return error == PathParseError.DotSegment
            ? DotSegmentError(text)
            : Message.Of("integrations.path-mapping.not-full-path", ("path", text));
    }

    /// <summary>MAM-47 — a path holding <c>.</c> or <c>..</c> as a folder name: one wording, one key, for the path
    /// mappings and the media folders (<see cref="Files.AllowedRootRules"/>).</summary>
    internal static Message DotSegmentError(string path) => Message.Of("integrations.path.dot-segment", ("path", path));

    private static string Canonical(string text) =>
        NormalizedPath.TryParse(text.Trim(), out var path, out var error)
            ? path.ToString()
            : throw new ArgumentException($"A path mapping was normalised before it was validated ({error}).", nameof(text));
}
