using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// Turns what was asked for into the queries one source is sent, from what that source can search by
/// (MAM-10). The adapter only translates a query; which identifier, year or search a source takes is
/// decided here, once for every source type.
/// </summary>
/// <remarks>
/// <para>
/// The queries are tiers, run in order until one lists a release (the way Radarr searches an indexer):
/// an identifier first, where the source's movie search takes one, because it is exact; then the title
/// and the year; then the title alone.
/// </para>
/// <para>
/// An identifier is sent alone. A source that takes both may require both to match, and a title in
/// another language than the tracker's would then find nothing; the title tiers still run when it
/// finds nothing. A year the source can't take as a parameter goes into the text ("Interstellar 2014"),
/// as ADR 0004 says Toloka is searched. A gateway that requires every word to match then misses a
/// release whose title lacks the year, and a source that takes the year may hold none for a release:
/// the title-alone tier finds both when nothing else did.
/// </para>
/// </remarks>
public static class ReleaseSearchPlanning
{
    /// <summary>
    /// The most releases asked of one source per query, and so stored per source per search: Toloka's
    /// feed answers at most 100 (its caps), and a source whose own limit is higher would otherwise write
    /// thousands of rows for one click.
    /// </summary>
    public const int MaximumResults = 100;

    /// <summary>
    /// The movie queries to send, in order; empty when the source offers no search this app can use (no
    /// general search, and a movie search that takes neither text nor an identifier given). MAM-32 asks
    /// for TV beside them (<see cref="ForTv"/>).
    /// </summary>
    /// <param name="text">The title, trimmed and validated (<see cref="ReleaseSearchRules"/>).</param>
    /// <param name="year">The year, or <c>null</c>.</param>
    /// <param name="identifiers">The registry ids asked for, IMDb before TMDb.</param>
    /// <param name="capabilities">What the source can search by.</param>
    public static IReadOnlyList<ReleaseSearchQuery> For(
        string text, int? year, IReadOnlyList<MediaIdentifier> identifiers, ReleaseSearchCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(capabilities);

        // The movie categories only: a text search finds every section, an audio-only release of the
        // film among them (ADR 0004). TV has queries of its own (ForTv, MAM-32). MAM-33 — a category the
        // source files films and series in (TV/Anime, TV/Documentary) holds movies too (ADR 0020).
        var categories = capabilities.Categories
            .Where(category => category.HoldsMovies)
            .Select(category => category.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var limit = capabilities.ResultLimit is > 0 and var sourceLimit ? Math.Min(sourceLimit, MaximumResults) : MaximumResults;
        var queries = new List<ReleaseSearchQuery>();

        if (capabilities.HasMovieSearch
            && identifiers.FirstOrDefault(identifier => capabilities.MovieSearchIdentifierSchemes.Contains(identifier.Scheme)) is { } usable)
            queries.Add(new ReleaseSearchQuery(ReleaseSearchMode.Movie, null, null, [usable], categories, limit));

        ReleaseSearchMode? textMode = capabilities is { HasMovieSearch: true, MovieSearchTakesText: true }
            ? ReleaseSearchMode.Movie
            : capabilities.HasTextSearch ? ReleaseSearchMode.General : null;
        if (textMode is not { } mode)
            return queries;

        if (year is { } knownYear)
        {
            queries.Add(mode == ReleaseSearchMode.Movie && capabilities.MovieSearchTakesYear
                ? new ReleaseSearchQuery(mode, text, knownYear, [], categories, limit)
                : new ReleaseSearchQuery(mode, string.Create(CultureInfo.InvariantCulture, $"{text} {knownYear}"), null, [], categories, limit));
        }

        queries.Add(new ReleaseSearchQuery(mode, text, null, [], categories, limit));
        return queries;
    }

    /// <summary>
    /// MAM-32 — the TV queries to send, in order, beside the movie ones (<see cref="For"/>): a series, its
    /// seasons and its episodes, cartoon series included, in the source's TV categories. Empty when the
    /// source has no TV category, or no search this app can use for TV.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An identifier first, where the source's TV search takes one, then the title — never the title with
    /// the year (ADR 0020): a release names the years of the seasons it holds ("Yellowstone (S5)
    /// (2022-2024)"), so the show's year in the query finds only the packs that hold season 1, and their
    /// finding stops the tiers before the rest. The year still matches the releases to the library.
    /// </para>
    /// <para>
    /// A season goes as a parameter of its own where the TV search takes one; a general search can't take
    /// it, and then every season comes back.
    /// </para>
    /// <para>
    /// No TV category means no TV query: with no category to keep, a general search would list every
    /// movie a second time as TV.
    /// </para>
    /// </remarks>
    /// <param name="text">The title, trimmed and validated (<see cref="ReleaseSearchRules"/>).</param>
    /// <param name="season">The season asked for, or <c>null</c>.</param>
    /// <param name="identifiers">The registry ids asked for, IMDb first.</param>
    /// <param name="capabilities">What the source can search by.</param>
    public static IReadOnlyList<ReleaseSearchQuery> ForTv(
        string text, int? season, IReadOnlyList<MediaIdentifier> identifiers, ReleaseSearchCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(capabilities);

        var categories = capabilities.Categories
            .Where(category => category.HoldsTv)
            .Select(category => category.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (categories.Count == 0)
            return [];

        var limit = capabilities.ResultLimit is > 0 and var sourceLimit ? Math.Min(sourceLimit, MaximumResults) : MaximumResults;
        var tvSearch = capabilities.TvSearch;
        var seasonParameter = tvSearch is { TakesSeason: true } ? season : null;
        var queries = new List<ReleaseSearchQuery>();

        if (tvSearch is not null && identifiers.FirstOrDefault(identifier => tvSearch.IdentifierSchemes.Contains(identifier.Scheme)) is { } usable)
            queries.Add(new ReleaseSearchQuery(ReleaseSearchMode.Tv, null, null, [usable], categories, limit, seasonParameter));

        if (tvSearch is { TakesText: true })
            queries.Add(new ReleaseSearchQuery(ReleaseSearchMode.Tv, text, null, [], categories, limit, seasonParameter));
        else if (capabilities.HasTextSearch)
            queries.Add(new ReleaseSearchQuery(ReleaseSearchMode.General, text, null, [], categories, limit));

        return queries;
    }

    /// <summary>
    /// MAM-33 — the one query for the source's newest releases (<see cref="ReleaseSearchMode.Latest"/>): its
    /// general search with no text, in its movie categories, its TV categories or both. <c>null</c> when it
    /// has no general search or none of the asked categories.
    /// </summary>
    /// <remarks>
    /// One query, never a page more: Toloka's gateway answers its latest 90 and an offset finds nothing
    /// (VendorSamples), and read every few hours that is days of releases. The categories keep out what the
    /// app never recommends — music, books, games — as they do for a search.
    /// </remarks>
    /// <param name="movies">Whether films are wanted: the movie categories, TV/Anime and TV/Documentary among
    /// them when the source files films there (ADR 0020).</param>
    /// <param name="tv">Whether shows are wanted: the TV categories.</param>
    /// <param name="capabilities">What the source can search by.</param>
    public static ReleaseSearchQuery? ForLatest(bool movies, bool tv, ReleaseSearchCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!capabilities.HasTextSearch)
            return null;

        var categories = capabilities.Categories
            .Where(category => movies && category.HoldsMovies || tv && category.HoldsTv)
            .Select(category => category.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (categories.Count == 0)
            return null;

        var limit = capabilities.ResultLimit is > 0 and var sourceLimit ? Math.Min(sourceLimit, MaximumResults) : MaximumResults;
        return new ReleaseSearchQuery(ReleaseSearchMode.Latest, null, null, [], categories, limit);
    }
}
