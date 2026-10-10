using System.Globalization;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — what a file's name says about the episodes it covers (ADR 0032, ADR 0035): a range of episodes
/// (<c>S02E05-E06</c>), or one half of an episode (<c>S03E09a</c>). Hints that may only stop a replacement, never let one
/// go: which episode a file is stays the library's word (ADR 0017), and these are read only where it gives no end number
/// of its own.
/// </summary>
/// <remarks>
/// <para>
/// The range is read the way the reference library reads it, so a name it takes for two episodes is taken for two here
/// too: Jellyfin 12.2's ten multi-episode expressions (<c>Emby.Naming/Common/NamingOptions.cs</c>,
/// <c>MultipleEpisodeExpressions</c>), tried in its order, with its parser's rules (<c>Emby.Naming/TV/EpisodePathParser.cs</c>):
/// an end number followed by a digit, an <c>i</c> or a <c>p</c> is a resolution's ("s09e14-1080p"), an end before the
/// start belongs to the episode's title ("S03E21 - E2"), and a season from 200 to 1927, or past 2500, is a picture size
/// ("1920x1080"). Each expression is Jellyfin's but for its start, <c>.*(\\|\/)</c>, which needs a folder before the
/// name; here the folder may be there or not, which matches the same last part of a path. The groups keep Jellyfin's
/// names, so the two still compare side by side.
/// </para>
/// <para>
/// A half is MAM-37's segment letter (<see cref="EpisodeSegments"/>), an <c>a</c> or a <c>b</c>: the same files the
/// Duplicates page keeps apart as two halves are the ones a replacement won't replace one of on its own.
/// </para>
/// </remarks>
public static partial class EpisodeRangeNames
{
    /// <summary>Jellyfin's multi-episode expressions, in its order: the first that reads an end number decides.</summary>
    private static readonly Regex[] Expressions =
    [
        SeasonXEpisodeDashSeasonEpisodePattern(),
        SeasonXEpisodeDashSeasonXEPattern(),
        SeasonXEpisodeMarkerPattern(),
        SeasonXEpisodeDashPattern(),
        NamedSeasonXEpisodeDashSeasonEpisodePattern(),
        NamedSeasonXEpisodeDashSeasonXEPattern(),
        NamedSeasonXEpisodeMarkerPattern(),
        NamedSeasonXEpisodeDashPattern(),
        NamedSeasonEpisodeMarkerPattern(),
        NamedSeasonEpisodeDashPattern(),
    ];

    /// <summary>Jellyfin's season numbers that are no season: a picture's width (<c>EpisodePathParser</c>).</summary>
    private const int FirstFalseSeason = 200;

    /// <summary>The first season past Jellyfin's false ones: the years shows are named by.</summary>
    private const int FirstYearSeason = 1928;

    /// <summary>The last season Jellyfin takes for a year.</summary>
    private const int LastYearSeason = 2500;

    /// <summary>
    /// The season and episodes <paramref name="fileName"/> (a file's name, or a path whose last part it is) names as a
    /// range, its last never before its first; <c>null</c> for a blank name and one that names no range.
    /// <c>S02E05-E06</c>, <c>S02E05E06</c>, <c>S02E05-06</c>, <c>S02E05x06</c>, <c>2x05-2x06</c> and <c>2x05-06</c> are
    /// season 2, episodes 5 to 6, in any letter case; a range of three or more gives its last end. <c>S02E05xE06</c> is
    /// none, as Jellyfin reads no range in it either.
    /// </summary>
    public static EpisodeNameRange? RangeOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        foreach (var expression in Expressions)
        {
            if (Read(expression, fileName) is { } range)
                return range;
        }

        return null;
    }

    /// <summary>
    /// The half of an episode <paramref name="fileName"/> names, <c>a</c> or <c>b</c> — "Mickey Mouse
    /// Clubhouse_S03E09a_-_Road Rally.mkv" is <c>a</c> —; <c>null</c> for any other name, and for a segment letter past
    /// <c>b</c>. Read as MAM-37 reads it (<see cref="EpisodeSegments.Read"/>): a lowercase letter right after the first
    /// <c>SxxEyy</c>, standing on its own and in no range, so "S03E09ab", "S03E09a-b" and "S03E09.avi" are none.
    /// </summary>
    public static char? HalfOf(string? fileName) => EpisodeSegments.Read(fileName) switch
    {
        "a" => 'a',
        "b" => 'b',
        _ => null,
    };

    // Jellyfin's EpisodePathParser.Parse for a named expression, its range part: the season, the episode and the end
    // number, kept only after its two checks of the end, and dropped for a false season.
    private static EpisodeNameRange? Read(Regex expression, string name)
    {
        var match = expression.Match(name);
        if (!match.Success
            || !int.TryParse(match.Groups["seasonnumber"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var season)
            || !int.TryParse(match.Groups["epnumber"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var episode)
            || season is >= FirstFalseSeason and < FirstYearSeason or > LastYearSeason)
            return null;

        var ending = match.Groups["endingepnumber"];
        if (!ending.Success)
            return null;

        // An end number running on into another digit, an "i" or a "p" is the start of a resolution: "-1080p".
        var next = ending.Index + ending.Length;
        if (next < name.Length && "0123456789iIpP".Contains(name[next], StringComparison.Ordinal))
            return null;

        // A range can't end before it starts: a lower number belongs to the episode's title.
        return int.TryParse(ending.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var last) && last >= episode
            ? new EpisodeNameRange(season, episode, last)
            : null;
    }

    // ── Jellyfin 12.2's MultipleEpisodeExpressions, in its order ─────────────────────────────────────────────────

    // "2x03 - 2x04 - 2x15", "2x03-2E15".
    [GeneratedRegex(
        @"^(?:.*[\\/])?[sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3})((-| - )[0-9]{1,4}[eExX](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonXEpisodeDashSeasonEpisodePattern();

    // "2x03 - 2xE04".
    [GeneratedRegex(
        @"^(?:.*[\\/])?[sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3})((-| - )[0-9]{1,4}[xX][eE](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonXEpisodeDashSeasonXEPattern();

    // "2x03x04x15", "2x03 - x04 - x15".
    [GeneratedRegex(
        @"^(?:.*[\\/])?[sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3})((-| - )?[xXeE](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonXEpisodeMarkerPattern();

    // "2x03-04-15", "2x03-E15".
    [GeneratedRegex(
        @"^(?:.*[\\/])?[sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3})(-[xE]?[eE]?(?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonXEpisodeDashPattern();

    // The four above after a series' name: "Elementary - 02x03 - 02x04 - 02x15".
    [GeneratedRegex(
        @"^(?:.*[\\/])?(?<seriesname>((?![sS]?[0-9]{1,4}[xX][0-9]{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3}))((-| - )[0-9]{1,4}[xXeE](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSeasonXEpisodeDashSeasonEpisodePattern();

    [GeneratedRegex(
        @"^(?:.*[\\/])?(?<seriesname>((?![sS]?[0-9]{1,4}[xX][0-9]{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3}))((-| - )[0-9]{1,4}[xX][eE](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSeasonXEpisodeDashSeasonXEPattern();

    [GeneratedRegex(
        @"^(?:.*[\\/])?(?<seriesname>((?![sS]?[0-9]{1,4}[xX][0-9]{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3}))((-| - )?[xXeE](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSeasonXEpisodeMarkerPattern();

    [GeneratedRegex(
        @"^(?:.*[\\/])?(?<seriesname>((?![sS]?[0-9]{1,4}[xX][0-9]{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]{1,3}))(-[xX]?[eE]?(?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSeasonXEpisodeDashPattern();

    // "S01E23-E24-E26", "S01E01E02", "S01E01x02", "S01.E01-E02".
    [GeneratedRegex(
        @"^(?:.*[\\/])?(?<seriesname>[^\\\/]*)[sS](?<seasonnumber>[0-9]{1,4})[xX\.]?[eE](?<epnumber>[0-9]{1,3})((-| - )?[xXeE](?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSeasonEpisodeMarkerPattern();

    // "S01E01-02", "s01e01-e04".
    [GeneratedRegex(
        @"^(?:.*[\\/])?(?<seriesname>[^\\\/]*)[sS](?<seasonnumber>[0-9]{1,4})[xX\.]?[eE](?<epnumber>[0-9]{1,3})(-[xX]?[eE]?(?<endingepnumber>[0-9]{1,3}))+[^\\\/]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSeasonEpisodeDashPattern();
}
