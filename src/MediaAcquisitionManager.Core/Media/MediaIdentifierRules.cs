using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Media;

/// <summary>
/// What an IMDb, a TMDb and a TVDB id look like, wherever the app reads one: from a media library, a
/// release listing, or a search request.
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

    /// <summary>Whether <paramref name="value"/> is a TMDb id, of a movie or of a show (which scheme it is
    /// depends on what it names, MAM-32): 1 to 12 digits.</summary>
    public static bool IsTmdbId(string value) => TmdbPattern().IsMatch(value);

    /// <summary>Whether <paramref name="value"/> is a TVDB id, of a show or of an episode (MAM-32): 1 to 12
    /// digits, like a TMDb id.</summary>
    public static bool IsTvdbId(string value) => TmdbPattern().IsMatch(value);

    /// <summary><c>\z</c> rather than <c>$</c>, which would also accept a trailing line break; ASCII
    /// digits, since <c>\d</c> would take any script's.</summary>
    [GeneratedRegex(@"^tt[0-9]{5,62}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImdbPattern();

    [GeneratedRegex(@"^[0-9]{1,12}\z")]
    private static partial Regex TmdbPattern();
}
