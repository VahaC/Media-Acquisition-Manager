using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — the season and episode a title or a file name marks: <c>S01E08</c> or <c>s1e8</c> (both), <c>_E08</c>,
/// <c> E08</c> or <c>.E08.</c> (the episode only). What prefills the numbers of an episode-to-show suggestion; nothing
/// else is read as a marker.
/// </summary>
/// <remarks>
/// A hint the owner confirms, never a fact: ADR 0017 — a name decides nothing on its own. A marker stands on its own,
/// between characters that are neither letters nor digits (the boundary <c>SeedTitleCleaning</c> ends a title at), so a
/// word like "THE08" or "Sequel2E3" marks nothing. Episode 0 marks nothing: TMDb numbers a season's episodes from 1.
/// </remarks>
public static partial class EpisodeMarker
{
    /// <summary>
    /// The first marker of <paramref name="text"/>: its season (<c>null</c> for an episode-only marker) and its episode;
    /// <c>null</c> when it has none.
    /// </summary>
    public static (int? Season, int Episode)? Read(string? text)
    {
        if (string.IsNullOrEmpty(text) || MarkerPattern().Match(text) is not { Success: true } match)
            return null;

        var episode = int.Parse(match.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        if (episode == 0)
            return null;
        int? season = match.Groups["season"].Success
            ? int.Parse(match.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;
        return (season, episode);
    }

    /// <summary>Season up to three digits and episode up to four, ASCII digits only (<c>[0-9]</c>, not <c>\d</c>, which
    /// takes any script's).</summary>
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:S(?<season>[0-9]{1,3})\s*)?E(?<episode>[0-9]{1,4})(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();
}
