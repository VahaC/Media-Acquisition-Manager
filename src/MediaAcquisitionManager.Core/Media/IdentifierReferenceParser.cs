using System.Globalization;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Media;

/// <summary>
/// MAM-34 — what the owner pasted into a work's TMDb dialog, read as an id: a TMDb film, show, season or episode link,
/// an IMDb link or id, or a bare TMDb number.
/// </summary>
/// <remarks>
/// <para>
/// Forgiving about how a link was copied — with or without <c>https://</c> and <c>www.</c>, any letter case, a trailing
/// slash, a query, a fragment, TMDb's title slug after the number (<c>/movie/223021-landing-stripling</c>), IMDb's mobile
/// host and a language folder before <c>title</c> — and strict about the id itself: every id passes
/// <see cref="MediaIdentifierRules"/>, the rule every other id the app reads passes, and anything else reads as nothing
/// rather than a guess.
/// </para>
/// <para>
/// Hosts are compared as names, never as text inside the address, so <c>themoviedb.org.example.com</c> names no TMDb
/// work.
/// </para>
/// </remarks>
public static class IdentifierReferenceParser
{
    private const string TmdbHost = "themoviedb.org";
    private const string ImdbHost = "imdb.com";

    /// <summary>
    /// The id <paramref name="text"/> names; <c>null</c> for blank text, a link to anything else, or an id that isn't
    /// well formed.
    /// </summary>
    public static IdentifierReference? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (MediaIdentifierRules.IsImdbId(trimmed))
            return Imdb(trimmed);
        if (MediaIdentifierRules.IsTmdbId(trimmed))
            return new IdentifierReference(null, trimmed);

        var address = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (IsHost(uri.Host, TmdbHost))
            return Tmdb(segments);
        if (IsHost(uri.Host, ImdbHost))
            return ImdbPath(segments);
        return null;
    }

    // ── TMDb ─────────────────────────────────────────────────────────────────

    // /movie/{id}[-slug], /tv/{id}[-slug], /tv/{id}[-slug]/season/{n}, /tv/{id}[-slug]/season/{n}/episode/{n}; any
    // further folder (/cast, /images) is still the work's.
    private static IdentifierReference? Tmdb(string[] segments)
    {
        if (segments.Length < 2)
            return null;

        var scheme = segments[0].ToLowerInvariant() switch
        {
            "movie" => MediaIdentifierScheme.Tmdb,
            "tv" => MediaIdentifierScheme.TmdbTv,
            _ => (MediaIdentifierScheme?)null,
        };
        if (scheme is null)
            return null;

        // The number before TMDb's slug: "223021-landing-stripling".
        var dash = segments[1].IndexOf('-', StringComparison.Ordinal);
        var id = dash < 0 ? segments[1] : segments[1][..dash];
        if (!MediaIdentifierRules.IsTmdbId(id))
            return null;
        if (scheme == MediaIdentifierScheme.Tmdb)
            return new IdentifierReference(scheme, id);

        int? season = null;
        int? episode = null;
        if (segments.Length >= 4 && string.Equals(segments[2], "season", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryNumber(segments[3], out var seasonNumber))
                return null;
            season = seasonNumber;
            if (segments.Length >= 6 && string.Equals(segments[4], "episode", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryNumber(segments[5], out var episodeNumber))
                    return null;
                episode = episodeNumber;
            }
        }

        return new IdentifierReference(scheme, id, season, episode);
    }

    // ── IMDb ─────────────────────────────────────────────────────────────────

    // /title/tt0056164/, or with a language folder first (/uk/title/tt0056164/).
    private static IdentifierReference? ImdbPath(string[] segments)
    {
        var title = Array.FindIndex(segments, segment => string.Equals(segment, "title", StringComparison.OrdinalIgnoreCase));
        if (title is < 0 or > 1 || title + 1 >= segments.Length)
            return null;
        return MediaIdentifierRules.IsImdbId(segments[title + 1]) ? Imdb(segments[title + 1]) : null;
    }

    // "TT0056164" and "tt0056164" are one title; IMDb writes the prefix in lower case.
    private static IdentifierReference Imdb(string id) => new(MediaIdentifierScheme.Imdb, string.Concat("tt", id.AsSpan(2)));

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IsHost(string host, string name) =>
        string.Equals(host, name, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + name, StringComparison.OrdinalIgnoreCase);

    // ASCII digits only, like the ids: a season or episode number of another script is no number here.
    private static bool TryNumber(string text, out int number)
    {
        number = 0;
        return text.Length is > 0 and <= 6
            && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
