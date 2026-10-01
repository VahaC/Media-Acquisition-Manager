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
    /// The queries to send, in order; empty when the source offers no search this app can use (no
    /// general search, and a movie search that takes neither text nor an identifier given).
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

        // Movies only in Phase A (specification §2): a text search finds every section, an audio-only
        // release of the film among them (ADR 0004).
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
}
