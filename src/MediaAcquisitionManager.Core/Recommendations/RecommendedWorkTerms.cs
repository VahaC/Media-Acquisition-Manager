using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — a recommended catalog work as a release is matched against it (ADR 0012's rules): its titles, its ids
/// and, for a held series, its missing seasons. One reading for the availability check and for the list's download
/// jobs, so a release that counts as the work for one counts for the other.
/// </summary>
public static class RecommendedWorkTerms
{
    /// <summary>Its catalog title, and its original title when that differs, each with <paramref name="year"/>.</summary>
    public static IReadOnlyList<ReportedTitle> TitlesOf(string title, string? originalTitle, int? year)
    {
        ArgumentNullException.ThrowIfNull(title);
        List<ReportedTitle> titles = [new ReportedTitle(title, MediaTitleKind.Display, year)];
        if (originalTitle is { } original && !string.Equals(original, title, StringComparison.Ordinal))
            titles.Add(new ReportedTitle(original, MediaTitleKind.Original, year));
        return titles;
    }

    /// <summary>Its catalog id in its kind's numbering (ADR 0017: a show's is a TMDb TV id), and its IMDb id when
    /// known.</summary>
    public static IReadOnlyList<MediaIdentifier> IdentifiersOf(MediaWorkKind kind, string catalogId, string? imdbId)
    {
        ArgumentNullException.ThrowIfNull(catalogId);
        List<MediaIdentifier> identifiers =
            [new MediaIdentifier(kind == MediaWorkKind.Series ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb, catalogId)];
        if (imdbId is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));
        return identifiers;
    }

    /// <summary>The missing seasons the run wrote ("4,5") as numbers; a part that isn't a number names no season, and
    /// a work that isn't a held series has none.</summary>
    public static IReadOnlySet<int> MissingSeasonsOf(string? missingSeasons) =>
        new HashSet<int>(
            (missingSeasons ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var season) ? season : (int?)null)
                .OfType<int>());
}
