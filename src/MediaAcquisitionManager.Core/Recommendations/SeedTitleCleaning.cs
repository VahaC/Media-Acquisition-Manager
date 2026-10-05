using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — the title a library work is looked up by in the catalog when it carries no id of it: the work's
/// display title cleaned of what a file name or a library's listing adds around a title, so the catalog's
/// search can find it. Pure; used for that lookup only, never shown and never stored on the library.
/// </summary>
/// <remarks>
/// <para>
/// What the reference library's titles carry (Vasyl's samples): a numbering in front, "120. Landing Stripling",
/// "02. Merry Madagascar"; underscores for spaces and an episode's number, "Planet_Earth_E08 - Jungles"; and a
/// release's whole name, "The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng]
/// [ Hurtom ] by Jatx 11".
/// </para>
/// <para>
/// The numbering and the underscores go; bracketed tags in front go. Then the title ends at the first of what
/// only follows a title: an episode or season marker ("E08", "S01E02", "(Season 1-3)"), a year or span in
/// brackets, a bracketed tag, a resolution ("1080p") or a release's type ("WEBDL", "BDRip"). An episode marker
/// ends the title like the rest — what follows it is the episode's name, not the work's ("Planet Earth" of
/// "Planet_Earth_E08 - Jungles"). A tracker's "by …" credit goes with the tail it follows; a bare "by" is never
/// a marker, as "Stand by Me" is a title. A marker in front of any title text ends nothing.
/// </para>
/// </remarks>
public static partial class SeedTitleCleaning
{
    /// <summary>What a cleaned title is trimmed of, besides whitespace: what stands between a title and what
    /// followed it (the <c>ReleaseWorkClaims</c> edge characters).</summary>
    private const string EdgeCharacters = "-–—:|,.";

    /// <summary>
    /// <paramref name="title"/> cleaned for a lookup: "Landing Stripling" of "120. Landing Stripling",
    /// "Planet Earth" of "Planet_Earth_E08 - Jungles", "The Penguins Of Madagascar" of its release's name;
    /// <c>""</c> when nothing of a title is left.
    /// </summary>
    public static string Clean(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var text = title.Replace('_', ' ');
        text = LeadingTagsPattern().Replace(text, "", 1);
        text = LeadingNumberingPattern().Replace(text, "", 1);

        // The first marker with title text before it: "(2014) Interstellar" says nothing about where its title ends.
        foreach (Match marker in EndMarkerPattern().Matches(text))
        {
            if (marker.Index > 0 && text[..marker.Index].Any(char.IsLetterOrDigit))
            {
                text = text[..marker.Index];
                break;
            }
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var cleaned = string.Join(' ', words).Trim().Trim(EdgeCharacters.ToCharArray()).Trim();
        return cleaned.Any(char.IsLetterOrDigit) ? cleaned : "";
    }

    /// <summary>
    /// The first year a bracketed year or span of <paramref name="text"/> names — 2008 of "(2008-2015)", 1986 of
    /// "(1986)" — for a title whose library row gives no year; <c>null</c> when it names none.
    /// </summary>
    public static int? BracketedYear(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = BracketedYearPattern().Match(text);
        return match.Success ? int.Parse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>"120. ", "02) " in front: a playlist's or a box set's numbering, never a title's.</summary>
    [GeneratedRegex(@"^\s*\d{1,4}\s*[.)]\s+")]
    private static partial Regex LeadingNumberingPattern();

    /// <summary>"[02.1] " in front: a collection's place of the file, before its title.</summary>
    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)+")]
    private static partial Regex LeadingTagsPattern();

    /// <summary>
    /// What only follows a title: an episode marker (S01E02, E08), a season marker in brackets or bare ((Season
    /// 1-3), (S1-5), Seasons 1-2, Сезон 1, S1), a bracketed year or span, a bracketed tag, a resolution, or a
    /// release's type. Whole words only, so "S1m0ne" and "Dark Seasons" keep their titles.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])S\d{1,2}\s*E\d{1,3}(?![\p{L}\p{N}])"
        + @"|(?<![\p{L}\p{N}])E\d{1,3}(?![\p{L}\p{N}])"
        + @"|\(\s*(?:Seasons?|Сезони?|S)\s*\d[^)]*\)"
        + @"|(?<![\p{L}\p{N}])(?:Seasons?|Сезони?)\s+\d{1,2}(?![\p{L}\p{N}])"
        + @"|(?<![\p{L}\p{N}])S\d{1,2}(?:\s*-\s*\d{1,2})?(?![\p{L}\p{N}])"
        + @"|\(\s*(?:19|20)\d{2}(?:\s*[-–]\s*(?:19|20)\d{2})?\s*\)"
        + @"|\["
        + @"|(?<![\p{L}\p{N}])\d{3,4}[pi](?![\p{L}\p{N}])"
        + @"|(?<![\p{L}\p{N}])(?:WEB-?DL|WEB-?Rip|BDRip|BD-?Remux|Remux|Blu-?Ray|HDTV|HDRip|DVDRip|DVD-?[59])(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndMarkerPattern();

    /// <summary>A year, or a span's first year, in round brackets.</summary>
    [GeneratedRegex(@"\(\s*(?<year>(?:19|20)\d{2})(?:\s*[-–]\s*(?:19|20)\d{2})?\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedYearPattern();
}
