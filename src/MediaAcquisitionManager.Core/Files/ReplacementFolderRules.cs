using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// MAM-24 — what the library folders ticked for replacement may be when the installation owner saves the Media folders
/// page (ADR 0033). Each rule answers why the ticks are refused, as a message the Api renders (ADR 0042), or <c>null</c>
/// when they are acceptable — the rules-class pattern of <see cref="AllowedRootRules"/>.
/// </summary>
/// <remarks>
/// <para>
/// No ticks is accepted: it is the default, and it means no copy can be replaced.
/// </para>
/// <para>
/// Only the shape is checked here: each tick is a full path in either style, and no folder is ticked twice. A tick is
/// echoed from the page's own list, so the platform's style adds nothing — and a library folder the mappings bring into
/// the app keeps the style its mapping gives it. Whether the path is a library folder the app knows is the save's own
/// check, against the database. A path is taken exactly as listed, never trimmed: a folder name may end in a space.
/// </para>
/// </remarks>
public static class ReplacementFolderRules
{
    /// <summary>More library folders than any installation has; bounds the page and the comparisons.</summary>
    public const int MaximumCount = 50;

    /// <summary>The <see cref="AllowedRootRules"/> bound, which the column shares.</summary>
    public const int MaximumPathLength = AllowedRootRules.MaximumPathLength;

    private static readonly Message TooManyError = Message.Of("files.replacement-folders.too-many", ("count", MaximumCount));
    private static readonly Message BlankError = Message.Of("files.replacement-folders.path-missing");
    private static readonly Message TooLongError = Message.Of("files.replacement-folders.too-long", ("count", MaximumPathLength));
    private static readonly Message ControlCharacterError = Message.Of("files.replacement-folders.control-characters");

    /// <summary>
    /// Why <paramref name="folders"/> are refused, or <c>null</c>. The first failure wins — the
    /// rules of each path in turn, then a folder ticked twice — and a path that parsed is echoed in its canonical form.
    /// </summary>
    /// <param name="folders">The ticked library folders as the page sent them.</param>
    public static Message? Validate(IReadOnlyList<string?> folders)
    {
        if (folders.Count > MaximumCount)
            return TooManyError;

        var parsed = new List<NormalizedPath>(folders.Count);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return BlankError;
            if (folder.Length > MaximumPathLength)
                return TooLongError;
            if (!NormalizedPath.TryParse(folder, out var path, out var error))
            {
                return error switch
                {
                    PathParseError.ControlCharacter => ControlCharacterError,
                    PathParseError.DotSegment => Message.Of("files.replacement-folders.dot-segment", ("path", folder)),
                    _ => Message.Of("files.replacement-folders.not-full-path", ("path", folder)),
                };
            }

            parsed.Add(path);
        }

        // By the path's own rules, as the allowed roots are: two spellings of one Windows folder are one folder.
        for (var first = 0; first < parsed.Count; first++)
        {
            for (var second = first + 1; second < parsed.Count; second++)
            {
                if (parsed[first].IsSameAs(parsed[second]))
                    return Message.Of("files.replacement-folders.ticked-twice", ("path", parsed[first].ToString()));
            }
        }

        return null;
    }

    /// <summary>The paths in canonical form, in the order given. Only for paths <see cref="Validate"/>
    /// accepted.</summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string?> folders) =>
        [.. folders.Select(folder => NormalizedPath.TryParse(folder, out var path, out var error)
            ? path.ToString()
            : throw new ArgumentException($"A ticked library folder was normalised before it was validated ({error}).", nameof(folders)))];
}
