using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// MAM-24 — what the library folders ticked for replacement may be when the installation owner saves the Media folders
/// page (ADR 0033). Each rule answers one readable sentence, or <c>null</c> when the ticks are acceptable — the
/// rules-class pattern of <see cref="AllowedRootRules"/>.
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

    private const string TooManyError = "The app can have at most 50 library folders ticked for replacement.";
    private const string BlankError = "Each library folder ticked for replacement needs its path.";
    private const string TooLongError = "A library folder's path can be at most 1024 characters long.";
    private const string ControlCharacterError = "A library folder's path can't contain line breaks or other control characters.";

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="folders"/>, or <c>null</c>. The first failure wins — the
    /// rules of each path in turn, then a folder ticked twice — and a path that parsed is echoed in its canonical form.
    /// </summary>
    /// <param name="folders">The ticked library folders as the page sent them.</param>
    public static string? Validate(IReadOnlyList<string?> folders)
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
                    PathParseError.DotSegment => $"“{folder}” contains . or .. as a folder name, so it can't be ticked for replacement.",
                    _ => $"“{folder}” is not a full path, so it can't be ticked for replacement.",
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
                    return $"“{parsed[first]}” is ticked twice. Keep one.";
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
