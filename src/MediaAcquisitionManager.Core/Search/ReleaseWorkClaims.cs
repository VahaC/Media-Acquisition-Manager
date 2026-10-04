using System.Globalization;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Matching;

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
/// <para>
/// MAM-32 — a TV release's title is read by <see cref="ReadTv"/>: there a range is the show's span, and a
/// season marker says which seasons or episodes it holds and, without a year, where its title ends.
/// </para>
/// </remarks>
public static partial class ReleaseWorkClaims
{
    /// <summary>What a title alternative is trimmed of, besides whitespace: the dashes, colons, bars,
    /// commas and dots that stand between a title and what follows it.</summary>
    private const string TitleEdgeCharacters = "-–—:|,.";

    private static readonly ReleaseWorkClaim Nothing = new([], null, null, null);

    /// <summary>The titles and the year a movie release's <paramref name="title"/> claims; no title and no
    /// year when it names no year.</summary>
    public static ReleaseWorkClaim Read(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        // The first bracketed year with title text before it: "(2014) Interstellar" has none, and a year
        // that starts the title says nothing about which title it is.
        foreach (Match year in BracketedYearPattern().Matches(title))
        {
            var titles = Alternatives(title[..year.Index], false);
            if (titles.Count > 0)
                return new ReleaseWorkClaim(titles, Year(year.Groups["year"].Value), null, null);
        }

        return ByBareYear(title) ?? Nothing;
    }

    /// <summary>
    /// MAM-32 — the titles, the year or span, and the seasons or episodes a TV release's
    /// <paramref name="title"/> claims (ADR 0020).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the shapes trackers and Prowlarr write: Toloka's "Yellowstone (S1-5) (2018-2024) WEB-DL",
    /// which Prowlarr rewrote from "Єллоустоун (Сезони 1-5) / Yellowstone (Seasons 1-5) (2018-2024)" — both
    /// read alike — and a scene name's "Yellowstone.S01E01.1080p". The seasons and episodes are the first
    /// season marker's: <c>S01E01</c>, <c>S01E01-E10</c>, <c>S1E1-9 of 9</c>, <c>S1-3</c>, <c>Season 1</c>,
    /// <c>Seasons 1-2</c>, <c>Сезон 1</c>, <c>Сезони 1-3</c>, <c>Сезон 1, серії 1-9 з 9</c>, each bare or
    /// in brackets (<c>[S3]</c>); a later one, like Prowlarr's repeat of it at the end, says nothing new.
    /// </para>
    /// <para>
    /// A bracketed year ends the title as a movie's does, and so does a bracketed span — a show's first and
    /// last year, "(2018-2024)", which a single show has where a movie collection has a range. A season
    /// marker before it is cut from each alternative with what follows it. Without one, the title ends at
    /// a bare year, the scene way, or at the first season marker, whichever comes first: a marker can end
    /// a title where nothing else would ("Yellowstone S01E01 1080p" is "Yellowstone", of no year).
    /// </para>
    /// </remarks>
    public static ReleaseWorkClaim ReadTv(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var marker = SeasonMarkerPattern().Match(title);
        var episodes = EpisodesOf(marker);
        foreach (Match year in BracketedYearOrSpanPattern().Matches(title))
        {
            var first = Year(year.Groups["year"].Value);
            int? last = year.Groups["last"].Success ? Year(year.Groups["last"].Value) : null;
            // "(2024-2018)" is no span, and taken for no year at all, as a movie's "(2014]" is.
            if (last < first)
                continue;

            var titles = Alternatives(title[..year.Index], true);
            if (titles.Count > 0)
                return new ReleaseWorkClaim(titles, first, last == first ? null : last, episodes);
        }

        var head = marker.Success ? title[..marker.Index] : title;
        if (ByBareYear(head) is { } byYear)
            return byYear with { Episodes = episodes };
        if (marker.Success && Alternatives(string.Join(' ', Words(head)), false) is { Count: > 0 } markedTitles)
            return new ReleaseWorkClaim(markedTitles, null, null, episodes);
        // Still the seasons it claims: a release matched by an id needs them as one matched by its title does.
        return Nothing with { Episodes = episodes };
    }

    /// <summary>
    /// MAM-32 — the years a library work's title may be of to match <paramref name="claim"/> by title and
    /// year (ADR 0020); <c>null</c> when the title names no year. A movie's claim, and a TV one that names
    /// no season, is its year or the span of years it names, as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A TV release names the years of the seasons it holds, not the show's (VendorSamples: "Yellowstone
    /// (S5) (2022-2024)" of the show begun in 2018), so with a season marker its first year says this much
    /// of the show (Vasyl's choice):
    /// </para>
    /// <list type="bullet">
    /// <item>With season 1 among the seasons, it is the year the show began, exactly: "Yellowstone (S1-5)
    /// (2018-2024)" is of 2018, and the BBC's "Yellowstone (S1) (2009)" is a show of 2009 — a span's later
    /// years being its later seasons', the 2018 show can't be "(S1-5) (2017-2024)" either.</item>
    /// <item>With later seasons only, or the specials alone, the show began that year or before it, so
    /// any year up to it: "(S5) (2022-2024)" and "(S4) (2021)" are of the show of 2018, never of one begun
    /// in 2023.</item>
    /// </list>
    /// <para>
    /// The cost: a show of the same title that began earlier is as good a match for a later-season pack,
    /// so if the library held an older show of that title, a newer show's later seasons would match it by
    /// title and year too. Every match is listed, never acted on (ADR 0012), and an id still outranks it.
    /// </para>
    /// </remarks>
    public static ClaimedYears? YearsToMatch(ReleaseWorkClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Year is not { } first)
            return null;

        return claim.Episodes switch
        {
            null => new ClaimedYears(first, claim.LastYear ?? first),
            { FirstSeason: <= 1, LastSeason: >= 1 } => new ClaimedYears(first, first),
            _ => new ClaimedYears(null, first),
        };
    }

    /// <summary>
    /// The titles and the year of <paramref name="title"/> by a bare year, the scene way; no title when
    /// nothing stands before that year, and <c>null</c> when it names no bare year.
    /// </summary>
    private static ReleaseWorkClaim? ByBareYear(string title)
    {
        var words = Words(title);
        // From the second word: a title can't be a year alone, and "2012.2009" is "2012" of 2009.
        for (var index = 1; index < words.Count; index++)
        {
            if (!IsYear(words[index]))
                continue;

            while (index + 1 < words.Count && IsYear(words[index + 1]))
                index++;
            var titles = Alternatives(string.Join(' ', words.Take(index)), false);
            return titles.Count > 0 ? new ReleaseWorkClaim(titles, Year(words[index]), null, null) : Nothing;
        }

        return null;
    }

    private static List<string> Words(string title) => WordSeparatorPattern().Split(title).Where(word => word.Length > 0).ToList();

    /// <summary>
    /// The titles a title part lists: split at a slash with whitespace on both sides, Toloka's
    /// "Інтерстеллар / Interstellar" — "Face/Off" stays whole — each trimmed, blank ones dropped. MAM-32 —
    /// <paramref name="cutAtSeasonMarker"/>: each without its first season marker and what follows it, so
    /// "Єллоустоун (Сезони 1-5) / Yellowstone (Seasons 1-5)" lists "Єллоустоун" and "Yellowstone".
    /// </summary>
    private static List<string> Alternatives(string titlePart, bool cutAtSeasonMarker) =>
        AlternativeSeparatorPattern().Split(titlePart)
            .Select(alternative => cutAtSeasonMarker && SeasonMarkerPattern().Match(alternative) is { Success: true } marker
                ? alternative[..marker.Index]
                : alternative)
            .Select(WithoutTrailingTags)
            .Where(alternative => alternative.Length > 0)
            .ToList();

    /// <summary>
    /// MAM-32 — what <paramref name="marker"/> claims; <c>null</c> when there is none. A range that runs
    /// backwards ("S5-3") claims its first alone, and episodes only within one season: "Сезони 1-2,
    /// серії 1-5" doesn't say whose episodes they are, so it claims the two seasons whole, and so does
    /// "S01E05-S02E03", whose episodes run from one season into the next.
    /// </summary>
    private static ClaimedEpisodes? EpisodesOf(Match marker)
    {
        if (!marker.Success)
            return null;

        var firstSeason = Number(marker.Groups["season"].Value);
        var lastSeason = marker.Groups["lastSeason"].Success ? Math.Max(firstSeason, Number(marker.Groups["lastSeason"].Value)) : firstSeason;
        if (!marker.Groups["episode"].Success || lastSeason != firstSeason)
            return new ClaimedEpisodes(firstSeason, lastSeason, null, null);

        var firstEpisode = Number(marker.Groups["episode"].Value);
        var lastEpisode = marker.Groups["lastEpisode"].Success ? Math.Max(firstEpisode, Number(marker.Groups["lastEpisode"].Value)) : firstEpisode;
        return new ClaimedEpisodes(firstSeason, firstSeason, firstEpisode, lastEpisode);
    }

    /// <summary>
    /// <paramref name="alternative"/> trimmed, without the square-bracketed tags that end it:
    /// "Rambo: Last Blood [Extended Cut]" is "Rambo: Last Blood" (ADR 0012, Vasyl's decision 4). Only at
    /// the end, where an edition is written: a title opening with or holding square brackets keeps them,
    /// "[REC] 2", and so does one that is nothing else, "[REC]". Round brackets are never a tag: they hold
    /// titles, "Birdman or (The Unexpected Virtue of Ignorance)".
    /// </summary>
    private static string WithoutTrailingTags(string alternative)
    {
        var title = TrimEdges(alternative);
        Match tag;
        // A tag at index 0 is all that is left: kept, as the title is the tag.
        while ((tag = TrailingTagPattern().Match(title)).Success && tag.Index > 0)
            title = TrimEdges(title[..tag.Index]);
        return title;
    }

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

    private static int Year(string digits) => Number(digits);

    private static int Number(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    // ASCII digits throughout: \d would take any script's. 1800 to 2099, the years a film can be from.

    /// <summary>"(2014)" or "[2014]", brackets that match; never a range, which has more inside.</summary>
    [GeneratedRegex(@"\((?<year>(?:18|19|20)[0-9]{2})\)|\[(?<year>(?:18|19|20)[0-9]{2})\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedYearPattern();

    /// <summary>MAM-32 — "(2014)" or "[2014]" as <see cref="BracketedYearPattern"/>, or a span of two years
    /// in them, "(2018-2024)", joined by a hyphen or a dash.</summary>
    [GeneratedRegex(
        @"\((?<year>(?:18|19|20)[0-9]{2})(?:\s*[-–—]\s*(?<last>(?:18|19|20)[0-9]{2}))?\)|\[(?<year>(?:18|19|20)[0-9]{2})(?:\s*[-–—]\s*(?<last>(?:18|19|20)[0-9]{2}))?\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex BracketedYearOrSpanPattern();

    /// <summary>
    /// MAM-32 — a season marker (<see cref="ReadTv"/>), in any letter case, as a whole word and with its
    /// brackets when it has them: a season ("S01", "Season 1", "Сезон 1"), a range of them ("S1-5",
    /// "Seasons 1-2", "Сезони 1-3"), or episodes of one ("S01E01", "S01E01-E10", "S1E1-9 of 9", "Сезон 1,
    /// серії 1-9 з 9") or of two ("S01E05-S02E03"). The short forms are written without spaces, so
    /// "S1 - 2160p" is season 1 alone; a number has at most four digits.
    /// </summary>
    [GeneratedRegex(
        """
        (?<![\p{L}\p{N}])
        (?:(?<open>[(\[])\s*)?
        (?:
            S(?<season>[0-9]{1,4})E(?<episode>[0-9]{1,4})
                (?:-?(?:S(?<lastSeason>[0-9]{1,4}))?E(?<lastEpisode>[0-9]{1,4})|-(?<lastEpisode>[0-9]{1,4}))?
                (?:\s+(?:of|з|із)\s+[0-9]{1,4})?
          | S(?<season>[0-9]{1,4})(?:-S?(?<lastSeason>[0-9]{1,4}))?
          | (?:Seasons?|Сезон(?:и|ів)?)\s*(?<season>[0-9]{1,4})(?:\s*-\s*(?<lastSeason>[0-9]{1,4}))?
                (?:\s*[,;]?\s*(?:Episodes?|Сері[яїй]|Епізод(?:и|ів)?)\s*(?<episode>[0-9]{1,4})(?:\s*-\s*(?<lastEpisode>[0-9]{1,4}))?
                    (?:\s+(?:of|з|із)\s+[0-9]{1,4})?)?
        )
        (?(open)\s*[)\]])
        (?![\p{L}\p{N}])
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SeasonMarkerPattern();

    [GeneratedRegex(@"^(?:18|19|20)[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BareYearPattern();

    /// <summary>What separates the words of a scene name: whitespace, dots and underscores.</summary>
    [GeneratedRegex(@"[\s._]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordSeparatorPattern();

    [GeneratedRegex(@"\s+/\s+", RegexOptions.CultureInvariant)]
    private static partial Regex AlternativeSeparatorPattern();

    /// <summary>A square-bracketed group with no bracket inside, ending the text.</summary>
    [GeneratedRegex(@"\[[^\[\]]*\]\z", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingTagPattern();
}
