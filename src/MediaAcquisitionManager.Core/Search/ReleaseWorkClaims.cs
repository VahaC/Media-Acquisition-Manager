using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// Reads which work a release's title claims to be — its titles and its year (MAM-12) — for matching the
/// release to the library's works by title and year. Pure, and read again whenever a release is shown,
/// like <see cref="ReleaseTitleClaims"/>, so a better reading later needs no migration.
/// </summary>
/// <remarks>
/// <para>
/// The year is what ends the title. A year in brackets first, the way Toloka and most trackers write it:
/// "Interstellar (2014) BDRemux 1080p" is "Interstellar" of 2014, and "Taken (2008) Remastered 2019" is
/// still 2008. Else a bare year the scene way, "Interstellar.2014.1080p.BluRay"; a film named by a year
/// keeps it ("1917 (2019)", "2012.2009.1080p"), and a run of years ends at the last one
/// ("Blade.Runner.2049.2017.1080p" is "Blade Runner 2049" of 2017).
/// </para>
/// <para>
/// No year, no title: in "Interstellar BDRip 1080p" nothing tells where the title ends and the quality
/// begins, and a guess would match the wrong work as readily as the right one. Such a release can match
/// a work only by an identifier its listing gives. A range, "(2010-2014)", is a collection's span, not
/// one work's year.
/// </para>
/// </remarks>
public static partial class ReleaseWorkClaims
{
    /// <summary>What a title alternative is trimmed of, besides whitespace: the dashes, colons, bars,
    /// commas and dots that stand between a title and what follows it.</summary>
    private const string TitleEdgeCharacters = "-–—:|,.";

    private static readonly ReleaseWorkClaim Nothing = new([], null);

    /// <summary>The titles and the year <paramref name="title"/> claims; no title and no year when it
    /// names no year.</summary>
    public static ReleaseWorkClaim Read(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        // The first bracketed year with title text before it: "(2014) Interstellar" has none, and a year
        // that starts the title says nothing about which title it is.
        foreach (Match year in BracketedYearPattern().Matches(title))
        {
            var titles = Alternatives(title[..year.Index]);
            if (titles.Count > 0)
                return new ReleaseWorkClaim(titles, Year(year.Groups["year"].Value));
        }

        var words = WordSeparatorPattern().Split(title).Where(word => word.Length > 0).ToList();
        // From the second word: a title can't be a year alone, and "2012.2009" is "2012" of 2009.
        for (var index = 1; index < words.Count; index++)
        {
            if (!IsYear(words[index]))
                continue;

            while (index + 1 < words.Count && IsYear(words[index + 1]))
                index++;
            var titles = Alternatives(string.Join(' ', words.Take(index)));
            return titles.Count > 0 ? new ReleaseWorkClaim(titles, Year(words[index])) : Nothing;
        }

        return Nothing;
    }

    /// <summary>
    /// The titles a title part lists: split at a slash with whitespace on both sides, Toloka's
    /// "Інтерстеллар / Interstellar" — "Face/Off" stays whole — each trimmed, blank ones dropped.
    /// </summary>
    private static List<string> Alternatives(string titlePart) =>
        AlternativeSeparatorPattern().Split(titlePart)
            .Select(TrimEdges)
            .Where(alternative => alternative.Length > 0)
            .ToList();

    private static string TrimEdges(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsEdge(text[start]))
            start++;
        while (end > start && IsEdge(text[end - 1]))
            end--;
        return text[start..end];
    }

    private static bool IsEdge(char character) =>
        char.IsWhiteSpace(character) || TitleEdgeCharacters.Contains(character, StringComparison.Ordinal);

    private static bool IsYear(string word) => BareYearPattern().IsMatch(word);

    private static int Year(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    // ASCII digits throughout: \d would take any script's. 1800 to 2099, the years a film can be from.

    /// <summary>"(2014)" or "[2014]", brackets that match; never a range, which has more inside.</summary>
    [GeneratedRegex(@"\((?<year>(?:18|19|20)[0-9]{2})\)|\[(?<year>(?:18|19|20)[0-9]{2})\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedYearPattern();

    [GeneratedRegex(@"^(?:18|19|20)[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BareYearPattern();

    /// <summary>What separates the words of a scene name: whitespace, dots and underscores.</summary>
    [GeneratedRegex(@"[\s._]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordSeparatorPattern();

    [GeneratedRegex(@"\s+/\s+", RegexOptions.CultureInvariant)]
    private static partial Regex AlternativeSeparatorPattern();
}
