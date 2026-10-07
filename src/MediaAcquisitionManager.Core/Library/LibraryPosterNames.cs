using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-35 — which file names the app serves a library work's poster under: the library item's id and its poster tag
/// (<c>{sourceItemId}-{posterTag}.jpg</c>), each letters and digits only. The tag names one version of the item's image,
/// so a name never stands for two pictures and the browser may keep it for good; the library list hands out a URL only
/// for such a name, and the poster endpoint serves nothing else, so no request can name a path outside the posters'
/// folder.
/// </summary>
public static partial class LibraryPosterNames
{
    /// <summary>
    /// Whether <paramref name="part"/> can be one half of a poster's file name: 1 to 64 letters and digits — a library's
    /// item id and image tag as Jellyfin gives them (32 hexadecimal digits each). No separator, dot or dash can pass, so
    /// the dash between the halves is never ambiguous.
    /// </summary>
    public static bool IsPart(string? part) => part is not null && PartPattern().IsMatch(part);

    /// <summary>The file name of the poster <paramref name="posterTag"/> of the item <paramref name="sourceItemId"/>;
    /// <c>null</c> for no tag, or a half the endpoint wouldn't serve.</summary>
    public static string? FileNameOf(string? sourceItemId, string? posterTag) =>
        IsPart(sourceItemId) && IsPart(posterTag) ? $"{sourceItemId}-{posterTag}.jpg" : null;

    /// <summary>Splits <paramref name="fileName"/>, a name <see cref="FileNameOf"/> gives, into the item id and the poster
    /// tag; <c>false</c> for any other name.</summary>
    public static bool TryParse(string? fileName, out string sourceItemId, out string posterTag)
    {
        var match = fileName is null ? Match.Empty : FileNamePattern().Match(fileName);
        sourceItemId = match.Success ? match.Groups["item"].Value : "";
        posterTag = match.Success ? match.Groups["tag"].Value : "";
        return match.Success;
    }

    /// <summary><c>\z</c>, not <c>$</c>, which would also accept a trailing line break.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}\z")]
    private static partial Regex PartPattern();

    /// <summary>Both halves as <see cref="PartPattern"/> takes them, a dash between, and the one extension the app keeps
    /// a library poster as.</summary>
    [GeneratedRegex(@"^(?<item>[A-Za-z0-9]{1,64})-(?<tag>[A-Za-z0-9]{1,64})\.jpg\z")]
    private static partial Regex FileNamePattern();
}
