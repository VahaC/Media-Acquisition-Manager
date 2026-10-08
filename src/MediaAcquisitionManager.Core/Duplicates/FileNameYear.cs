using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-37 — the year a file name claims in brackets: the 1982 of "[01] Blade Runner (1982) [The Final Cut] UHD-BDRip
/// 1080p H.265.mkv", the 2017 of "[02] Blade Runner 2049 (2017) [US Transfer].mkv". Vasyl, 2026-10-08: copies whose
/// names claim years more than one apart are not copies of each other — Blade Runner 2049, filed under the 1982 film's
/// ids, is another film.
/// </summary>
/// <remarks>
/// <para>
/// Not <c>ReleaseWorkClaims.Read</c>, which <c>FileNameClaims</c> reads a name by: it too takes a year from a bracket
/// only when the year is alone in it, but without one it reads a bare year the scene way, so "Blade.Runner.2049.1080p"
/// would be of 2049.
/// </para>
/// <para>
/// Read narrowly, as a year read by mistake can only keep copies apart — take a real copy off the Duplicates page —
/// never make a group. A year counts only alone in round or square brackets, "(1982)", "[1962]", "( 1962 )" — never
/// beside a word or a number in them: a year that shares its bracket is a release's, a remaster's or a cut's as often
/// as the film's ("[2001 Redux]" of a 1979 film, "[2019 Remaster]", "[1997 Special Edition]"), a title's in round ones
/// ("(2001 A Space Odyssey)"), or a resolution's ("[1920 x 1080]"), so production's "Seksmisja [1983 DVDRemux].mkv"
/// claims none. A bare year is a title's as often as a release's ("Blade Runner 2049", "1917"), and a list or a span,
/// "(2004, 2007)", "(1940-1967)", a collection's: none is read. A name whose brackets claim two different years claims
/// none, as nothing says which is the film's. Resolutions, codecs and numbers — "[2160p]", "H.265", "[2xUKR_ENG]",
/// "[02.3]", "107-2." — never stand as four digits alone in a bracket.
/// </para>
/// <para>
/// Four ASCII digits from 1880, before the first films, to 2099, the last year the app's other readers take — fixed,
/// not the clock's year and one: the grouping reads no clock (ADR 0028), so a name reads the same every year, and a
/// group the years split keeps, from one New Year to the next, the key the Duplicates page addresses it by.
/// </para>
/// </remarks>
public static partial class FileNameYear
{
    /// <summary>
    /// The year <paramref name="fileName"/> claims alone in brackets ("(1982)", "[1962]"); <c>null</c> for a blank name,
    /// one that claims no year so, or one that claims two different years.
    /// </summary>
    public static int? Read(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        List<int> years =
        [
            .. YearPattern().Matches(fileName)
                .Select(match => int.Parse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture))
                .Distinct(),
        ];
        return years.Count == 1 ? years[0] : null;
    }

    // ASCII digits only ([0-9], not \d, which takes any script's); 1880 to 2099; the year alone in round or square
    // brackets, with nothing but whitespace beside it.
    [GeneratedRegex(
        @"\(\s*(?<year>18[89][0-9]|19[0-9]{2}|20[0-9]{2})\s*\)|\[\s*(?<year>18[89][0-9]|19[0-9]{2}|20[0-9]{2})\s*\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();
}
