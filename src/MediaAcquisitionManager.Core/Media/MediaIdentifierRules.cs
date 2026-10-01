using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Media;

/// <summary>
/// What an IMDb and a TMDb id look like, wherever the app reads one: from a media library, or from a
/// search request.
/// </summary>
/// <remarks>
/// MAM-10 — one rule for both, lifted out of the Jellyfin adapter: a release search is run with the ids
/// a library work holds (MAM-12), and an id the library accepted must never be refused by the search.
/// </remarks>
public static partial class MediaIdentifierRules
{
    /// <summary>Whether <paramref name="value"/> is an IMDb title id: <c>tt</c> in any letter case and
    /// 5 to 62 digits, at most what an identifier column holds (64).</summary>
    public static bool IsImdbId(string value) => ImdbPattern().IsMatch(value);

    /// <summary>Whether <paramref name="value"/> is a TMDb movie id: 1 to 12 digits.</summary>
    public static bool IsTmdbId(string value) => TmdbPattern().IsMatch(value);

    /// <summary><c>\z</c> rather than <c>$</c>, which would also accept a trailing line break; ASCII
    /// digits, since <c>\d</c> would take any script's.</summary>
    [GeneratedRegex(@"^tt[0-9]{5,62}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImdbPattern();

    [GeneratedRegex(@"^[0-9]{1,12}\z")]
    private static partial Regex TmdbPattern();
}
