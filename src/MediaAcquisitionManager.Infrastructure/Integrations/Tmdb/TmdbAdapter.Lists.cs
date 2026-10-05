using System.Globalization;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

// MAM-33 — the catalog's lists: a search by title, the works filed under an IMDb id, a page of the
// best-rated works of some genres, and the genres themselves with the role each plays for the core.
internal sealed partial class TmdbAdapter
{
    /// <summary>
    /// The fewest votes a work needs to be discovered: an average of a handful of votes says little, and
    /// sorted by rating the list would open with a cult film's ten 10s. The discover samples were recorded
    /// with it (VendorSamples): the fewest votes among their results are 311 and 353, works many people
    /// rated.
    /// </summary>
    public const int DiscoverMinimumVoteCount = 300;

    /// <summary>The last page TMDb's discovery serves; it answers a later one with an error.</summary>
    public const int DiscoverMaximumPage = 500;

    /// <summary>TMDb's genre id of Animation, the same in its movie and its TV list (VendorSamples).</summary>
    private const int AnimationGenreId = 16;

    /// <summary>TMDb's genre id of Documentary, the same in its movie and its TV list (VendorSamples).</summary>
    private const int DocumentaryGenreId = 99;

    /// <summary>
    /// <c>GET /3/search/movie?query=…&amp;year=…</c> or <c>GET /3/search/tv?query=…&amp;first_air_date_year=…</c>,
    /// the first page as TMDb ranks it.
    /// </summary>
    public Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> SearchAsync(
        IntegrationConnectionSettings settings, MediaWorkKind kind, string title, int? year, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var pathAndQuery = SearchPathAndQuery(kind, title, year);

        return CallAsync<IReadOnlyList<CatalogWorkSummary>>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync<IReadOnlyList<CatalogWorkSummary>>(
                client,
                settings,
                sessionSecrets,
                pathAndQuery,
                notFoundError: null,
                (root, secrets) => ReadSummaries(root, "results", kind, secrets),
                token),
            cancellationToken);
    }

    /// <summary>
    /// <c>GET /3/find/{imdbId}?external_source=imdb_id</c>: TMDb's films and shows filed under the id, the
    /// films first. Its people, episodes and seasons are not read: an episode's id finds none of the works.
    /// </summary>
    public Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> FindByImdbIdAsync(
        IntegrationConnectionSettings settings, string imdbId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RequireImdbId(imdbId, nameof(imdbId));

        return CallAsync<IReadOnlyList<CatalogWorkSummary>>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync<IReadOnlyList<CatalogWorkSummary>>(
                client,
                settings,
                sessionSecrets,
                FindPathAndQuery(imdbId),
                notFoundError: null,
                ReadFound,
                token),
            cancellationToken);
    }

    /// <summary>
    /// <c>GET /3/discover/{movie|tv}</c>: one page of the works with every genre of
    /// <paramref name="genreIds"/> (<c>with_genres</c>, comma-separated: all of them) and none of
    /// <paramref name="excludedGenreIds"/>, sorted by rating, of <see cref="DiscoverMinimumVoteCount"/>
    /// votes or more, adult titles left out.
    /// </summary>
    public Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> DiscoverAsync(
        IntegrationConnectionSettings settings,
        MediaWorkKind kind,
        IReadOnlyList<int> genreIds,
        IReadOnlyList<int> excludedGenreIds,
        int page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var segment = KindSegment(kind);
        ArgumentNullException.ThrowIfNull(genreIds);
        ArgumentNullException.ThrowIfNull(excludedGenreIds);
        if (genreIds.Concat(excludedGenreIds).Any(id => id < 1))
            throw new ArgumentException("A TMDb genre id is a positive number.", nameof(genreIds));
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, DiscoverMaximumPage);

        var parameters = new List<string>();
        if (genreIds.Count > 0)
            parameters.Add($"with_genres={GenreList(genreIds)}");
        if (excludedGenreIds.Count > 0)
            parameters.Add($"without_genres={GenreList(excludedGenreIds)}");
        parameters.Add("sort_by=vote_average.desc");
        parameters.Add(string.Create(CultureInfo.InvariantCulture, $"vote_count.gte={DiscoverMinimumVoteCount}"));
        parameters.Add("include_adult=false");
        parameters.Add($"language={CatalogLanguage}");
        parameters.Add(string.Create(CultureInfo.InvariantCulture, $"page={page}"));
        var pathAndQuery = $"discover/{segment}?{string.Join('&', parameters)}";

        return CallAsync<IReadOnlyList<CatalogWorkSummary>>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync<IReadOnlyList<CatalogWorkSummary>>(
                client,
                settings,
                sessionSecrets,
                pathAndQuery,
                notFoundError: null,
                (root, secrets) => ReadSummaries(root, "results", kind, secrets),
                token),
            cancellationToken);
    }

    /// <summary><c>GET /3/genre/{movie|tv}/list</c>: every genre of the kind, named in the catalog's
    /// language, with its role (<see cref="RoleOf"/>).</summary>
    public Task<IntegrationCallResult<IReadOnlyList<CatalogGenre>>> GetGenresAsync(
        IntegrationConnectionSettings settings, MediaWorkKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var segment = KindSegment(kind);

        return CallAsync<IReadOnlyList<CatalogGenre>>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync<IReadOnlyList<CatalogGenre>>(
                client,
                settings,
                sessionSecrets,
                $"genre/{segment}/list?language={CatalogLanguage}",
                notFoundError: null,
                ReadGenres,
                token),
            cancellationToken);
    }

    /// <summary>
    /// The search's path and query, every value escaped so none can end the parameter early or add one
    /// of its own. Throws for a search no caller should make: an episode, a blank title, a year no work
    /// has.
    /// </summary>
    private static string SearchPathAndQuery(MediaWorkKind kind, string title, int? year)
    {
        var segment = KindSegment(kind);
        ArgumentNullException.ThrowIfNull(title);
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A search needs a title.", nameof(title));
        if (year is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(year), year, "A year is 1 to 9999.");

        // TMDb names the year filter of a show's search after its first air date.
        var yearParameter = kind == MediaWorkKind.Series ? "first_air_date_year" : "year";
        var yearPart = year is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"&{yearParameter}={value}")
            : "";
        return $"search/{segment}?query={Uri.EscapeDataString(title.Trim())}{yearPart}&language={CatalogLanguage}";
    }

    private static string FindPathAndQuery(string imdbId) =>
        $"find/{imdbId}?external_source=imdb_id&language={CatalogLanguage}";

    /// <summary>
    /// Throws unless <paramref name="imdbId"/> is an IMDb title id (<see cref="MediaIdentifierRules.IsImdbId"/>):
    /// it goes into the request's path.
    /// </summary>
    private static void RequireImdbId(string imdbId, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(imdbId, parameterName);
        if (!MediaIdentifierRules.IsImdbId(imdbId))
            throw new ArgumentException("An IMDb id is tt and 5 to 62 digits.", parameterName);
    }

    private static string GenreList(IReadOnlyList<int> genreIds) =>
        string.Join(',', genreIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The films (<c>movie_results</c>) and the shows (<c>tv_results</c>) a find lists; an answer
    /// without either list is not TMDb's.</summary>
    private static IReadOnlyList<CatalogWorkSummary>? ReadFound(JsonElement root, string?[] secrets)
    {
        if (ReadSummaries(root, "movie_results", MediaWorkKind.Movie, secrets) is not { } movies
            || ReadSummaries(root, "tv_results", MediaWorkKind.Series, secrets) is not { } shows)
            return null;
        return [.. movies, .. shows];
    }

    /// <summary>Each genre with an id and a name, once by its id, in TMDb's order; an answer without the
    /// <c>genres</c> list is not TMDb's.</summary>
    private static IReadOnlyList<CatalogGenre>? ReadGenres(JsonElement root, string?[] secrets)
    {
        if (!root.TryGetProperty("genres", out var genresElement) || genresElement.ValueKind != JsonValueKind.Array)
            return null;

        var genres = new List<CatalogGenre>();
        foreach (var entry in genresElement.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.Number
                && idElement.TryGetInt32(out var id)
                && id > 0
                && TextProperty(entry, "name", NameMaximumLength, secrets) is { Length: > 0 } name
                && !genres.Exists(known => known.Id == id))
                genres.Add(new CatalogGenre(id, name, RoleOf(id)));
        }

        return genres;
    }

    /// <summary>
    /// What a genre makes a work for the core (ADR 0004's rule: the source knows its numbering, the core
    /// doesn't): TMDb's Animation a cartoon, its Documentary a documentary, any other genre nothing.
    /// </summary>
    private static CatalogGenreRole RoleOf(int genreId) => genreId switch
    {
        AnimationGenreId => CatalogGenreRole.Animation,
        DocumentaryGenreId => CatalogGenreRole.Documentary,
        _ => CatalogGenreRole.Other,
    };
}
