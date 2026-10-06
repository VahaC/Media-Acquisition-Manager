using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — the catalog's lists: a search, a find by IMDb id, a discovery page and the genres, from TMDb's
// real answers (VendorSamples/Tmdb), with the exact request each sends. MAM-34 — the searches without a year the
// owner's suggestions run, from the answers the live check recorded (Planet Earth, Landing Stripling).
public sealed class TmdbAdapterListsTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJsaXN0cyJ9.c2lnbmF0dXJlLW9mLWxpc3Rz";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token);

    private readonly IntegrationAdapterHarness _harness = new();

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Search_of_movies_reads_the_search_sample()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.SearchMovieFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.SearchAsync(Settings, MediaWorkKind.Movie, "Landing Stripling", 1962, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/search/movie?query=Landing%20Stripling&year=1962&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var found = Assert.Single(result.Value!);
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, "223021", "Landing Stripling", "Landing Stripling", "en", new DateOnly(1962, 5, 18),
                found.GenreIds, 4.579, 19, "/ooZQbu20BBYxInF542jHonKz4Pl.jpg"),
            found);
        Assert.Equal([16, 35], found.GenreIds);
    }

    [Fact]
    public async Task Search_of_shows_reads_the_tv_search_sample_and_names_the_year_after_the_first_air_date()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.SearchTvFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.SearchAsync(Settings, MediaWorkKind.Series, "Пінгвіни Мадагаскару", 2008, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(
            $"{BaseUrl}/3/search/tv?query={Uri.EscapeDataString("Пінгвіни Мадагаскару")}&first_air_date_year=2008&language=uk-UA",
            request.Uri.AbsoluteUri);
        var found = Assert.Single(result.Value!);
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Series, "7869", "Пінгвіни Мадагаскару", "The Penguins of Madagascar", "en", new DateOnly(2008, 11, 28),
                found.GenreIds, 7.519, 472, "/uzGJu5VKP96jm9aemgUv1EOMIgZ.jpg"),
            found);
        Assert.Equal([16, 35, 10751, 10762], found.GenreIds);
    }

    [Fact]
    public async Task Search_without_a_year_sends_none_and_escapes_the_title_so_it_adds_no_parameter()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.SearchMovieFile, TmdbSamples.MediaType);

        //Act
        await Catalog.SearchAsync(Settings, MediaWorkKind.Movie, "  Fast & Furious?language=en#1 ", null, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/search/movie?query=Fast%20%26%20Furious%3Flanguage%3Den%231&language=uk-UA", request.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(MediaWorkKind.Episode, "Yellowstone", null)]
    [InlineData(MediaWorkKind.Movie, "", null)]
    [InlineData(MediaWorkKind.Movie, "   ", null)]
    [InlineData(MediaWorkKind.Movie, "Alien", 0)]
    [InlineData(MediaWorkKind.Movie, "Alien", 10000)]
    public async Task Search_no_caller_should_make_throws_without_sending_anything(MediaWorkKind kind, string title, int? year)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Catalog.SearchAsync(Settings, kind, title, year, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Search_against_the_fake_answers_the_added_results_in_order_and_nothing_for_another_year()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 679, "Чужі", "Aliens", "1986-07-18"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 348, "Чужий", "Alien", "1979-05-25"));
        tmdb.AddSearch(MediaWorkKind.Movie, "aliens", 1986, 679, 348);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var found = await Catalog.SearchAsync(settings, MediaWorkKind.Movie, "Aliens", 1986, cancellationToken);
        var otherYear = await Catalog.SearchAsync(settings, MediaWorkKind.Movie, "Aliens", 1987, cancellationToken);

        //Assert
        Assert.Equal(["679", "348"], found.Value!.Select(work => work.Id));
        Assert.Empty(otherYear.Value!);
    }

    [Fact]
    public async Task Search_of_shows_without_a_year_reads_the_planet_earth_sample_in_tmdbs_order()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.SearchTvPlanetEarthFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.SearchAsync(Settings, MediaWorkKind.Series, "Planet Earth", null, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/search/tv?query=Planet%20Earth&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // The first page: 20 of the 41 shows, the 2006 series first, then its sequels, as TMDb ranks them.
        var found = result.Value!;
        Assert.Equal(20, found.Count);
        Assert.All(found, work => Assert.Equal(MediaWorkKind.Series, work.Kind));
        Assert.Equal(["1044", "68595", "116156"], found.Take(3).Select(work => work.Id));
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Series, TmdbSamples.TvDetailsPlanetEarthId, "Планета Земля", "Planet Earth", "en", new DateOnly(2006, 3, 5),
                found[0].GenreIds, 8.639, 1294, "/ajNcvf2fvOmus8rxPdpoGrdL4OI.jpg"),
            found[0]);
        Assert.Equal([99], found[0].GenreIds);
        // TMDb's fallback to the original title, a show it has no poster of, one with no air date and no votes.
        Assert.Contains(found, work => work is { Id: "15723", Title: "Hard Time on Planet Earth" });
        Assert.Null(Assert.Single(found, work => work.Id == "99101").PosterPath);
        var undated = Assert.Single(found, work => work.Id == "36896");
        Assert.Equal(((DateOnly?)null, (double?)null, 0), (undated.ReleaseDate, undated.Rating, undated.VoteCount));
    }

    [Fact]
    public async Task Search_of_movies_without_a_year_reads_the_one_film_of_the_sample()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.SearchMovieWithoutYearFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.SearchAsync(Settings, MediaWorkKind.Movie, "Landing Stripling", null, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/search/movie?query=Landing%20Stripling&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // The same film the search with its year finds: a year left out narrows nothing here.
        var found = Assert.Single(result.Value!);
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, TmdbSamples.MovieDetailsShortId, "Landing Stripling", "Landing Stripling", "en", new DateOnly(1962, 5, 18),
                found.GenreIds, 4.579, 19, "/ooZQbu20BBYxInF542jHonKz4Pl.jpg"),
            found);
        Assert.Equal([16, 35], found.GenreIds);
    }

    [Fact]
    public async Task FindByImdbId_reads_the_find_sample()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.FindImdbFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.FindByImdbIdAsync(Settings, TmdbSamples.FindImdbId, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/find/tt0071562?external_source=imdb_id&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var found = Assert.Single(result.Value!);
        Assert.Equal(
            new CatalogWorkSummary(
                MediaWorkKind.Movie, TmdbSamples.FindMovieId, "Хрещений батько 2", "The Godfather Part II", "en", new DateOnly(1974, 12, 20),
                found.GenreIds, 8.573, 14408, "/iYbToXLBnSZ7Q0WniqSlSpzPprs.jpg"),
            found);
        Assert.Equal([18, 80], found.GenreIds);
    }

    [Fact]
    public async Task FindByImdbId_against_the_fake_lists_the_films_then_the_shows_filed_under_the_id()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 1, "A show"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 2, "A film"));
        tmdb.AddFind("tt0000001", MediaWorkKind.Series, 1);
        tmdb.AddFind("tt0000001", MediaWorkKind.Movie, 2);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var found = await Catalog.FindByImdbIdAsync(settings, "tt0000001", cancellationToken);
        var none = await Catalog.FindByImdbIdAsync(settings, "tt0000002", cancellationToken);

        //Assert
        Assert.Equal([(MediaWorkKind.Movie, "2"), (MediaWorkKind.Series, "1")], found.Value!.Select(work => (work.Kind, work.Id)));
        Assert.Equal(IntegrationCallStatus.Succeeded, none.Status);
        Assert.Empty(none.Value!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0071562")]
    [InlineData("tt007")]
    [InlineData("tt0071562/../../movie/1")]
    [InlineData("tt0071562?external_source=tvdb_id")]
    public async Task FindByImdbId_with_an_id_that_is_not_imdbs_throws_without_sending_anything(string imdbId)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Catalog.FindByImdbIdAsync(Settings, imdbId, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task FindByImdbId_of_an_answer_without_its_lists_is_an_unexpected_response()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text("""{"movie_results":[]}""", HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.FindByImdbIdAsync(Settings, TmdbSamples.FindImdbId, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
    }

    [Fact]
    public async Task Discover_of_documentaries_reads_the_discover_sample_with_the_parameters_it_sends()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.DiscoverMovieDocumentaryFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.DiscoverAsync(Settings, MediaWorkKind.Movie, [99], [], 1, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(
            $"{BaseUrl}/3/discover/movie?with_genres=99&sort_by=vote_average.desc&vote_count.gte=300&include_adult=false&language=uk-UA&page=1",
            request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(20, result.Value!.Count);
        Assert.All(result.Value, work =>
        {
            Assert.Equal(MediaWorkKind.Movie, work.Kind);
            Assert.Contains(99, work.GenreIds);
            Assert.True(work.VoteCount >= 300);
        });
        Assert.Equal(("1022256", "Селена Ґомес: я та мій світ", 8.5), (result.Value[0].Id, result.Value[0].Title, result.Value[0].Rating!.Value));
    }

    [Fact]
    public async Task Discover_of_animated_shows_reads_the_discover_sample_and_excludes_the_genres_it_is_told()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.DiscoverTvAnimationFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.DiscoverAsync(Settings, MediaWorkKind.Series, [16, 10765], [99, 10762], 3, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(
            $"{BaseUrl}/3/discover/tv?with_genres=16,10765&without_genres=99,10762&sort_by=vote_average.desc&vote_count.gte=300&include_adult=false&language=uk-UA&page=3",
            request.Uri.AbsoluteUri);
        Assert.Equal(20, result.Value!.Count);
        Assert.All(result.Value, work => Assert.Equal(MediaWorkKind.Series, work.Kind));
        Assert.Equal(("209867", "Фрірен: після кінця пригоди", new DateOnly(2023, 9, 29)), (result.Value[0].Id, result.Value[0].Title, result.Value[0].ReleaseDate!.Value));
    }

    [Fact]
    public async Task Discover_without_genres_sends_no_genre_filter()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text("""{"page":2,"results":[],"total_pages":2,"total_results":20}""", HttpStatusCode.OK, "application/json");

        //Act
        var result = await Catalog.DiscoverAsync(Settings, MediaWorkKind.Movie, [], [], 2, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            $"{BaseUrl}/3/discover/movie?sort_by=vote_average.desc&vote_count.gte=300&include_adult=false&language=uk-UA&page=2",
            Assert.Single(_harness.TmdbHttp.Requests).Uri.AbsoluteUri);
        Assert.Empty(result.Value!);
    }

    [Theory]
    [InlineData(MediaWorkKind.Episode, 1, 16)]
    [InlineData(MediaWorkKind.Movie, 0, 16)]
    [InlineData(MediaWorkKind.Movie, 501, 16)]
    [InlineData(MediaWorkKind.Movie, 1, 0)]
    [InlineData(MediaWorkKind.Movie, 1, -16)]
    public async Task Discover_no_caller_should_make_throws_without_sending_anything(MediaWorkKind kind, int page, int genreId)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(
            () => Catalog.DiscoverAsync(Settings, kind, [genreId], [], page, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Discover_against_the_fake_answers_each_page_it_was_given()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 246, "Аватар: Останній Захисник", "Avatar: The Last Airbender", "2005-02-21", [16, 10759]));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 94605, "Аркейн", "Arcane", "2021-11-06", [16, 10765]));
        tmdb.AddDiscover(MediaWorkKind.Series, 1, 246);
        tmdb.AddDiscover(MediaWorkKind.Series, 2, 94605);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await Catalog.DiscoverAsync(settings, MediaWorkKind.Series, [16], [99], 1, cancellationToken);
        var second = await Catalog.DiscoverAsync(settings, MediaWorkKind.Series, [16], [99], 2, cancellationToken);
        var third = await Catalog.DiscoverAsync(settings, MediaWorkKind.Series, [16], [99], 3, cancellationToken);

        //Assert
        Assert.Equal(["246"], first.Value!.Select(work => work.Id));
        Assert.Equal(["94605"], second.Value!.Select(work => work.Id));
        Assert.Empty(third.Value!);
    }

    [Fact]
    public async Task GetGenres_of_movies_reads_the_recorded_list_with_animation_and_documentary_marked()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.GenresMovieFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetGenresAsync(Settings, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/genre/movie/list?language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var genres = result.Value!;
        Assert.Equal(TmdbSamples.MovieGenreCount, genres.Count);
        Assert.Equal(new CatalogGenre(28, "Бойовик", CatalogGenreRole.Other), genres[0]);
        Assert.Equal(new CatalogGenre(16, "Мультфільм", CatalogGenreRole.Animation), Assert.Single(genres, genre => genre.Id == 16));
        Assert.Equal(new CatalogGenre(99, "Документальний", CatalogGenreRole.Documentary), Assert.Single(genres, genre => genre.Id == 99));
        Assert.Equal(TmdbSamples.MovieGenreCount - 2, genres.Count(genre => genre.Role == CatalogGenreRole.Other));
    }

    [Fact]
    public async Task GetGenres_of_shows_reads_the_recorded_list_with_animation_and_documentary_marked()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.GenresTvFile, TmdbSamples.MediaType);

        //Act
        var result = await Catalog.GetGenresAsync(Settings, MediaWorkKind.Series, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/genre/tv/list?language=uk-UA", request.Uri.AbsoluteUri);
        var genres = result.Value!;
        Assert.Equal(TmdbSamples.TvGenreCount, genres.Count);
        Assert.Equal(new CatalogGenre(10759, "Екшн і Пригоди", CatalogGenreRole.Other), genres[0]);
        Assert.Equal(CatalogGenreRole.Animation, Assert.Single(genres, genre => genre.Id == 16).Role);
        Assert.Equal(CatalogGenreRole.Documentary, Assert.Single(genres, genre => genre.Id == 99).Role);
    }

    [Fact]
    public async Task GetGenres_skips_an_entry_without_an_id_or_a_name_and_keeps_the_rest()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(
            """{"genres":[{"id":16,"name":"Мультфільм"},{"name":"No id"},{"id":"99","name":"Text id"},{"id":35,"name":""},{"id":16,"name":"Again"},7,{"id":99,"name":"Документальний"}]}""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Catalog.GetGenresAsync(Settings, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [new CatalogGenre(16, "Мультфільм", CatalogGenreRole.Animation), new CatalogGenre(99, "Документальний", CatalogGenreRole.Documentary)],
            result.Value!);
    }

    [Theory]
    [InlineData("""{"results":{}}""")]
    [InlineData("""{"page":1}""")]
    [InlineData("""[{"id":1,"title":"A"}]""")]
    [InlineData("not json")]
    public async Task A_list_whose_answer_is_not_tmdbs_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var search = await Catalog.SearchAsync(Settings, MediaWorkKind.Movie, "Alien", null, cancellationToken);
        var discover = await Catalog.DiscoverAsync(Settings, MediaWorkKind.Movie, [16], [], 1, cancellationToken);
        var genres = await Catalog.GetGenresAsync(Settings, MediaWorkKind.Movie, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, search.Status);
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, discover.Status);
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, genres.Status);
    }

    [Fact]
    public async Task A_list_with_a_refused_token_is_authentication_failed()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Recorded(TmdbSamples.WrongKeyResponseFile);

        //Act
        var result = await Catalog.SearchAsync(Settings, MediaWorkKind.Movie, "Alien", null, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.DoesNotContain(Token, result.Error, StringComparison.Ordinal);
    }
}
