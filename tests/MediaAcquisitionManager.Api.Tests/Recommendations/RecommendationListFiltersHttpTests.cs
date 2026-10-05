using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — what a page of a recommendation tab can be narrowed to (reason, available, genre, decade, kind), each by the
// server and together, a value it doesn't take being a 400 with its sentence (names only, never numbers); and what the
// filters offer: facets counted over the tab as the caller sees it — before the request's filters, so a picked option
// stays — in the cards' reason order, genres by name, decades from the earliest, films before shows. A fallback work
// waits for a check that found it before it is listed, or counted.
public sealed class RecommendationListFiltersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations";

    // The catalog's ids (TMDb's) and the Ukrainian names its lists give them.
    private const int Drama = 18;
    private const int Comedy = 35;
    private const int Animation = 16;
    private const int Family = 10751;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private IReleaseAvailabilitySeeder _checks = null!;
    private AuthorizedUser _anna = null!;

    public RecommendationListFiltersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _checks = new ReleaseAvailabilitySeeder(_factory.Scope);
        _anna = await new UserSeeder(_factory.Scope).CreateAuthorized("anna");
        var genres = new CatalogGenreSeeder(_factory.Scope);
        await genres.Create(MediaWorkKind.Movie, Drama, "Драма");
        await genres.Create(MediaWorkKind.Movie, Comedy, "Комедія");
        await genres.Create(MediaWorkKind.Movie, Animation, "Мультфільм", CatalogGenreRole.Animation);
        await genres.Create(MediaWorkKind.Movie, Family, "Сімейний");
        await genres.Create(MediaWorkKind.Series, Animation, "Мультфільм", CatalogGenreRole.Animation);
        await genres.Create(MediaWorkKind.Series, Family, "Сімейний");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_filtered_by_reason_keeps_the_works_with_a_reason_of_that_kind()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var collection = await RecommendAsync("1", "Вартові Галактики 3", reasons: [Reason(RecommendationReasonKind.Collection), Reason(RecommendationReasonKind.Recommended)]);
        await RecommendAsync("2", "Марсіянин", reasons: [Reason(RecommendationReasonKind.Director)]);
        await RecommendAsync("3", "Дюна", reasons: [Reason(RecommendationReasonKind.Recommended)]);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&reason=collection", _anna.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([collection.Id], list.Items.Select(item => item.Id));
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task List_filtered_by_availability_keeps_the_works_a_check_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var found = await RecommendAsync("1", "Знайдений");
        var notFound = await RecommendAsync("2", "Не знайдений");
        var neverChecked = await RecommendAsync("3", "Не перевірений");
        await _checks.Create(found, releaseCount: 3);
        await _checks.Create(notFound, releaseCount: 0);

        //Act
        var available = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&available=TRUE", _anna.AccessToken);
        var unticked = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&available=false", _anna.AccessToken);

        //Assert
        Assert.Equal([found.Id], (await ReadListAsync(available, cancellationToken)).Items.Select(item => item.Id));
        // False is the page's unticked box: every work.
        Assert.Equal(
            new HashSet<Guid> { found.Id, notFound.Id, neverChecked.Id },
            (await ReadListAsync(unticked, cancellationToken)).Items.Select(item => item.Id).ToHashSet());
    }

    [Fact]
    public async Task List_filtered_by_genre_keeps_the_works_the_catalog_files_under_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var drama = await RecommendAsync("1", "Драма", genreIds: [Drama, Comedy]);
        await RecommendAsync("2", "Комедія", genreIds: [Comedy]);
        await RecommendAsync("3", "Без жанру");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&genreId={Drama}", _anna.AccessToken);

        //Assert
        var item = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(drama.Id, item.Id);
        Assert.Equal(["Драма", "Комедія"], item.Genres);
    }

    [Fact]
    public async Task List_filtered_by_decade_keeps_the_works_released_in_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await RecommendAsync("1", "Початок", year: 1990);
        var last = await RecommendAsync("2", "Кінець", year: 1999);
        await RecommendAsync("3", "Наступне", year: 2000);
        await RecommendAsync("4", "Без дати", year: null);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&decade=1990", _anna.AccessToken);

        //Assert
        Assert.Equal(
            new HashSet<Guid> { first.Id, last.Id },
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id).ToHashSet());
    }

    [Fact]
    public async Task List_filtered_by_kind_keeps_the_films_or_the_shows_of_a_tab_holding_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = await RecommendAsync("862", "Історія іграшок", RecommendationTab.Cartoons);
        var show = await RecommendAsync("862", "Пінгвіни з Мадагаскару", RecommendationTab.Cartoons, MediaWorkKind.Series);

        //Act
        var films = await _factory.GetHttpRequest($"{BaseUrl}?tab=Cartoons&kind=movie", _anna.AccessToken);
        var shows = await _factory.GetHttpRequest($"{BaseUrl}?tab=Cartoons&kind=Series", _anna.AccessToken);

        //Assert
        // One catalog id, a film and a show: the catalog numbers the two apart.
        Assert.Equal([film.Id], (await ReadListAsync(films, cancellationToken)).Items.Select(item => item.Id));
        Assert.Equal([show.Id], (await ReadListAsync(shows, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_with_several_filters_keeps_the_works_passing_all_of_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var match = await RecommendAsync("1", "Усе так", genreIds: [Drama], year: 1994, reasons: [Reason(RecommendationReasonKind.Director)]);
        await RecommendAsync("2", "Не той жанр", genreIds: [Comedy], year: 1994, reasons: [Reason(RecommendationReasonKind.Director)]);
        await RecommendAsync("3", "Не те десятиліття", genreIds: [Drama], year: 2004, reasons: [Reason(RecommendationReasonKind.Director)]);
        await RecommendAsync("4", "Не та причина", genreIds: [Drama], year: 1994, reasons: [Reason(RecommendationReasonKind.Recommended)]);

        //Act
        var response = await _factory.GetHttpRequest(
            $"{BaseUrl}?tab=Movies&reason=Director&genreId={Drama}&decade=1990&kind=Movie", _anna.AccessToken);

        //Assert
        Assert.Equal([match.Id], (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("reason=Liked", "“Liked” is not a recommendation reason. Use Collection, Director, Creator, Recommended, MissingSeasons, TopRated or NewAtSource.")]
    // By name only: a number would be read as the reason with that value.
    [InlineData("reason=4", "“4” is not a recommendation reason. Use Collection, Director, Creator, Recommended, MissingSeasons, TopRated or NewAtSource.")]
    [InlineData("available=yes", "“yes” is not an availability filter. Use true or false.")]
    [InlineData("available=1", "“1” is not an availability filter. Use true or false.")]
    [InlineData("genreId=drama", "“drama” is not a genre id, which is a number such as 18.")]
    [InlineData("genreId=-18", "“-18” is not a genre id, which is a number such as 18.")]
    [InlineData("genreId=99999999999", "“99999999999” is not a genre id, which is a number such as 18.")]
    [InlineData("decade=1995", "“1995” is not a decade. Use its first year, such as 1990.")]
    [InlineData("decade=nineties", "“nineties” is not a decade. Use its first year, such as 1990.")]
    [InlineData("decade=10000", "“10000” is not a decade. Use its first year, such as 1990.")]
    [InlineData("kind=Episode", "“Episode” is not a kind of work. Use Movie or Series.")]
    [InlineData("kind=1", "“1” is not a kind of work. Use Movie or Series.")]
    [InlineData("kind=Tv", "“Tv” is not a kind of work. Use Movie or Series.")]
    public async Task List_with_a_filter_it_doesnt_take_is_400_with_why(string query, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("1", "Матриця");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&{query}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_offers_facets_in_reason_order_genres_by_name_decades_and_kinds_in_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("1", "Перший", RecommendationTab.Cartoons, genreIds: [Animation, Family], year: 2003,
            reasons: [Reason(RecommendationReasonKind.Recommended), Reason(RecommendationReasonKind.TopRated)]);
        await RecommendAsync("2", "Другий", RecommendationTab.Cartoons, genreIds: [Animation], year: 1995,
            reasons: [Reason(RecommendationReasonKind.Collection), Reason(RecommendationReasonKind.Recommended)]);
        await RecommendAsync("3", "Третій", RecommendationTab.Cartoons, MediaWorkKind.Series, genreIds: [Animation, Family], year: 2009,
            reasons: [Reason(RecommendationReasonKind.MissingSeasons), Reason(RecommendationReasonKind.NewAtSource)]);
        await RecommendAsync("4", "Четвертий", RecommendationTab.Cartoons, year: null, reasons: [Reason(RecommendationReasonKind.Recommended)]);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Cartoons", _anna.AccessToken);

        //Assert
        var facets = (await ReadListAsync(response, cancellationToken)).Facets;
        // The cards' order, strongest first; a kind no work has isn't offered.
        Assert.Equal(
            [
                new RecommendationReasonFacetDto(RecommendationReasonKind.MissingSeasons, 1),
                new RecommendationReasonFacetDto(RecommendationReasonKind.Collection, 1),
                new RecommendationReasonFacetDto(RecommendationReasonKind.Recommended, 3),
                new RecommendationReasonFacetDto(RecommendationReasonKind.NewAtSource, 1),
                new RecommendationReasonFacetDto(RecommendationReasonKind.TopRated, 1),
            ],
            facets.Reasons);
        // Films and shows share the catalog's ids of shared genres: one entry each, alphabetical.
        Assert.Equal([new RecommendationGenreFacetDto(Animation, "Мультфільм", 3), new RecommendationGenreFacetDto(Family, "Сімейний", 2)], facets.Genres);
        Assert.Equal(
            [new RecommendationDecadeFacetDto(1990, 1), new RecommendationDecadeFacetDto(2000, 2)],
            facets.Decades);
        Assert.Equal([new RecommendationKindFacetDto(MediaWorkKind.Movie, 3), new RecommendationKindFacetDto(MediaWorkKind.Series, 1)], facets.Kinds);
    }

    [Fact]
    public async Task List_counts_its_facets_before_the_requests_filters()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("1", "Драма", genreIds: [Drama], year: 1994, reasons: [Reason(RecommendationReasonKind.Director)]);
        await RecommendAsync("2", "Комедія", genreIds: [Comedy], year: 2004, reasons: [Reason(RecommendationReasonKind.Recommended)]);

        //Act
        var all = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&genreId={Drama}&decade=1990&reason=Director", _anna.AccessToken);

        //Assert
        // The comedy, its decade and its reason stay on offer while the drama is picked.
        var unfiltered = await ReadListAsync(all, cancellationToken);
        var filtered = await ReadListAsync(response, cancellationToken);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(unfiltered.Facets.Reasons, filtered.Facets.Reasons);
        Assert.Equal(unfiltered.Facets.Genres, filtered.Facets.Genres);
        Assert.Equal(unfiltered.Facets.Decades, filtered.Facets.Decades);
        Assert.Equal(unfiltered.Facets.Kinds, filtered.Facets.Kinds);
        Assert.Equal([new RecommendationGenreFacetDto(Drama, "Драма", 1), new RecommendationGenreFacetDto(Comedy, "Комедія", 1)], filtered.Facets.Genres);
    }

    [Fact]
    public async Task List_counts_its_facets_without_the_callers_dismissed_works()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("1", "Драма", genreIds: [Drama]);
        var dismissed = await RecommendAsync("2", "Комедія", genreIds: [Comedy], year: 1984, reasons: [Reason(RecommendationReasonKind.Collection)]);
        await new RecommendationDismissalSeeder(_factory.Scope).Dismiss(_anna.User, dismissed);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        // The tab as she sees it: an option that would list nothing isn't offered.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal([new RecommendationGenreFacetDto(Drama, "Драма", 1)], list.Facets.Genres);
        Assert.Equal([new RecommendationReasonFacetDto(RecommendationReasonKind.Recommended, 1)], list.Facets.Reasons);
        Assert.Equal([new RecommendationDecadeFacetDto(2010, 1)], list.Facets.Decades);
    }

    [Fact]
    public async Task List_leaves_a_fallback_out_until_a_check_found_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var neverChecked = await RecommendAsync("1", "Ще не перевірений", requiresAvailability: true, reasons: [Reason(RecommendationReasonKind.TopRated)]);
        var notFound = await RecommendAsync("2", "Не знайдений", requiresAvailability: true, reasons: [Reason(RecommendationReasonKind.TopRated)]);
        var found = await RecommendAsync("3", "Знайдений", requiresAvailability: true, reasons: [Reason(RecommendationReasonKind.TopRated)]);
        var ordinary = await RecommendAsync("4", "Звичайний");
        await _checks.Create(notFound, releaseCount: 0);
        await _checks.Create(found, releaseCount: 2);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        // A "top rated" work recommended for nothing the library holds earns its place only by being there to download —
        // and isn't counted in the facets before then either.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(new HashSet<Guid> { found.Id, ordinary.Id }, list.Items.Select(item => item.Id).ToHashSet());
        Assert.DoesNotContain(list.Items, item => item.Id == neverChecked.Id || item.Id == notFound.Id);
        Assert.Equal(2, list.TotalCount);
        Assert.Equal(new RecommendationReasonFacetDto(RecommendationReasonKind.TopRated, 1), Assert.Single(list.Facets.Reasons, facet => facet.Kind == RecommendationReasonKind.TopRated));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A catalog work recommended in <paramref name="tab"/> (Movies unless told) for <paramref name="reasons"/>
    /// (one Recommended reason unless told).</summary>
    private async Task<CatalogWorkEntity> RecommendAsync(
        string catalogId,
        string title,
        RecommendationTab tab = RecommendationTab.Movies,
        MediaWorkKind kind = MediaWorkKind.Movie,
        IReadOnlyList<int>? genreIds = null,
        int? year = 2015,
        IReadOnlyList<RecommendationReasonEntity>? reasons = null,
        bool requiresAvailability = false)
    {
        var work = await _works.Create(kind, catalogId, title, year is { } released ? new DateOnly(released, 3, 1) : null, genreIds);
        await _candidates.Create(work, tab, requiresAvailability: requiresAvailability, reasons: reasons);
        return work;
    }

    private static RecommendationReasonEntity Reason(RecommendationReasonKind kind) => new() { Kind = kind };

    private static async Task<RecommendationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
