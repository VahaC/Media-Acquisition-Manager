using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>
/// What a release search asks for, read by <see cref="ReleaseSearchRules"/> and trimmed (MAM-10). One
/// reading for the plain release search and the stored search (MAM-12), so both take exactly the same
/// values and answer the same sentences.
/// </summary>
/// <param name="Text">The title, trimmed.</param>
/// <param name="Year">The year; <c>null</c> when none was asked for.</param>
/// <param name="ImdbId">The IMDb id as asked, trimmed; <c>null</c> when none was.</param>
/// <param name="TmdbId">The TMDb id as asked, trimmed; <c>null</c> when none was.</param>
public sealed record ReleaseSearchTerms(string Text, int? Year, string? ImdbId, string? TmdbId)
{
    /// <summary>The ids asked for, IMDb before TMDb: the planning sends the first id a source takes.</summary>
    public IReadOnlyList<MediaIdentifier> Identifiers { get; } = IdentifiersOf(ImdbId, TmdbId);

    /// <summary>
    /// The sentence saying what is wrong with the first value that is wrong — the title, then the year,
    /// then the IMDb id, then the TMDb id — or <c>null</c> with every value read into
    /// <paramref name="terms"/> (<c>null</c> when there is a sentence). A blank year or id asks for none.
    /// </summary>
    public static string? Read(string? text, string? year, string? imdbId, string? tmdbId, out ReleaseSearchTerms? terms)
    {
        terms = null;
        if (ReleaseSearchRules.ValidateText(text) is { } textError)
            return textError;
        if (ReleaseSearchRules.ValidateYear(year, out var searchYear) is { } yearError)
            return yearError;
        if (ReleaseSearchRules.ValidateImdbId(imdbId) is { } imdbError)
            return imdbError;
        if (ReleaseSearchRules.ValidateTmdbId(tmdbId) is { } tmdbError)
            return tmdbError;

        terms = new ReleaseSearchTerms(text!.Trim(), searchYear, Blank(imdbId), Blank(tmdbId));
        return null;
    }

    /// <summary>An IMDb and a TMDb id as a list, IMDb first, leaving out the one that is <c>null</c> — a
    /// search's, or (MAM-12) the ones a stored release's listing gives.</summary>
    public static IReadOnlyList<MediaIdentifier> IdentifiersOf(string? imdbId, string? tmdbId)
    {
        var identifiers = new List<MediaIdentifier>();
        if (imdbId is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));
        if (tmdbId is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Tmdb, tmdbId));
        return identifiers;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
