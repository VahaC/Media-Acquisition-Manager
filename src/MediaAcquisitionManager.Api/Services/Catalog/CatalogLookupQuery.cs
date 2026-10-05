using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>
/// MAM-33 — what a lookup of a catalog work asks: a work of <paramref name="Kind"/> filed under an IMDb id, titled
/// so in a year, or titled so and begun by a year. Built through <see cref="ByImdbId"/>, <see cref="ByTitle"/> or
/// <see cref="ByTitleUpTo"/>, and cached by its <see cref="Key"/> (<see cref="CatalogLookupEntity.QueryKey"/>).
/// </summary>
/// <param name="Kind">A film or a show: the catalog numbers the two apart.</param>
/// <param name="ImdbId">The IMDb id looked up by; <c>null</c> for a lookup by title.</param>
/// <param name="Title">The title looked up by; <c>null</c> for a lookup by an id.</param>
/// <param name="Year">The year that goes with the title exactly; <c>null</c> for a lookup by an id or one
/// <see cref="ByTitleUpTo"/> a year.</param>
/// <param name="LatestYear">The latest year the work may have begun in, for <see cref="ByTitleUpTo"/>; <c>null</c>
/// otherwise.</param>
public sealed record CatalogLookupQuery(MediaWorkKind Kind, string? ImdbId, string? Title, int? Year, int? LatestYear = null)
{
    /// <summary>
    /// The most of a title's match key a cache key keeps: <see cref="CatalogLookupEntity.QueryKeyMaximumLength"/>
    /// less the kind, the "title" marker and a year. A longer title is told apart by its first 480 characters.
    /// </summary>
    private const int TitleKeyMaximumLength = 480;

    /// <summary>
    /// The cache key: <c>Movie|imdb|tt0090605</c>, <c>Series|title|YELLOWSTONE|2018</c> by the title's match key
    /// (<see cref="TitleMatchKey"/>), so two spellings the catalog's lookup can't tell apart share one answer, or
    /// <c>Series|title|YELLOWSTONE|&lt;=2022</c> for one up to a year — another answer than the exact year's.
    /// </summary>
    public string Key => ImdbId is { } imdbId
        ? $"{Kind}|imdb|{imdbId.ToLowerInvariant()}"
        : LatestYear is { } latest
            ? $"{Kind}|title|{Cut(TitleMatchKey.For(Title ?? ""))}|<={latest}"
            : $"{Kind}|title|{Cut(TitleMatchKey.For(Title ?? ""))}|{Year}";

    /// <summary>A lookup by <paramref name="imdbId"/>, which must be well formed (<c>MediaIdentifierRules.IsImdbId</c>).</summary>
    public static CatalogLookupQuery ByImdbId(MediaWorkKind kind, string imdbId) => new(kind, imdbId, null, null);

    /// <summary>A lookup by <paramref name="title"/> of <paramref name="year"/>; the title must have a letter or a
    /// digit.</summary>
    public static CatalogLookupQuery ByTitle(MediaWorkKind kind, string title, int year) => new(kind, null, title, year);

    /// <summary>
    /// A lookup by <paramref name="title"/> of a work begun in <paramref name="latestYear"/> or before it: the catalog
    /// is searched without a year, and the one work of the title it may be is taken. For a show's later-season pack,
    /// whose year is its seasons' and not the show's (ADR 0020, <c>ReleaseWorkClaims.YearsToMatch</c>); the title must
    /// have a letter or a digit.
    /// </summary>
    public static CatalogLookupQuery ByTitleUpTo(MediaWorkKind kind, string title, int latestYear) => new(kind, null, title, null, latestYear);

    private static string Cut(string key) => key.Length <= TitleKeyMaximumLength ? key : key[..TitleKeyMaximumLength];
}
