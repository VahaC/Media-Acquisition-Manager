using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-37 — the segment letter a file name glues to its first <c>SxxEyy</c> marker: the "a" of
/// "Mickey Mouse Clubhouse_S03E09a_-_Road Rally.mkv". Vasyl, 2026-10-08: an episode's files whose names carry different
/// segment letters are not copies of each other.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin reads "S03E09a" and "S03E09b" both as season 3, episode 9 — two items with one episode's ids and place,
/// which the sync makes one work of two versions (ADR 0017) — so only the file names tell the segments apart. Not
/// <c>EpisodeMarker</c>: MAM-34's prefill reader, whose boundary rejects a letter glued to the number.
/// </para>
/// <para>
/// Read narrowly, as a letter read by mistake can only keep two copies apart, never make a group. The first compact
/// marker decides, and a later one says nothing. The letter is a lowercase a to d — "e" would be a multi-episode name's
/// next marker ("S01E01e02"), and an uppercase letter is likelier a title's first ("S01E05A Day at the Beach") — standing
/// on its own: no letter or digit after it ("S01E05arrival", "S03E09a1080p"), and no range or pair, a joiner (- – + &amp;
/// _ ,) and then another letter, an episode number or a marker ("S03E09a-b", "S03E09a-10", "S03E09a-S03E09b"). Season up
/// to three digits and episode up to four, as <c>EpisodeMarker</c> reads them; "S03 E09a", "1x09a" and "E08a" are read
/// as nothing.
/// </para>
/// </remarks>
public static partial class EpisodeSegments
{
    /// <summary>
    /// The segment letter <paramref name="fileName"/> glues to its first <c>SxxEyy</c> ("S03E09a" is a); <c>null</c> for a
    /// blank name, one with no such marker, or one whose first marker carries no letter.
    /// </summary>
    public static string? Read(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // The segment is optional, so the match stops at the first marker whether or not it carries one.
        var segment = SegmentPattern().Match(fileName).Groups["segment"];
        return segment.Success ? segment.Value : null;
    }

    // ASCII digits only ([0-9], not \d, which takes any script's); the letter is matched case-sensitively, (?-i:…), in a
    // pattern that reads S and E in any case.
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])S[0-9]{1,3}E[0-9]{1,4}(?![0-9])(?<segment>(?-i:[a-d])(?![\p{L}\p{N}])(?![-–+&_,](?:(?-i:[a-d])(?![\p{L}\p{N}])|[0-9]{1,4}(?![\p{L}\p{N}])|[SE][0-9])))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
}
