using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 — the dialog's two ways past the prepared suggestions (GET works/{id}/tmdb/search and /lookup): the owner's free
// search (TMDb's first page, films and shows by the work's kind, the year narrowing it) and a pasted id in every form the
// ticket names — a TMDb film or show link (a show's with its season and episode), an IMDb link or id, a bare number with
// the owner's film-or-show. Each answer is ranked with its evidence the way the suggestions are, and stored in the catalog
// cache (its poster serves). Unreadable input is a 400 with its sentence, an id TMDb doesn't have a 404 with its
// sentence (R18), no catalog a 409, a failing TMDb a 502.
public sealed class LibraryLinkFindHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string LandingId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string LandingPath = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)/120. Landing Stripling (1962).mkv";
    private const string PasteInvalidError = "Paste a TMDb or IMDb link, an IMDb id like tt0056164, or a TMDb number.";
    private const string ImdbNotFoundError = "TMDb has no film or show with that IMDb id. If it is an episode's, paste the show's link instead.";
    private const int LandingFilm = 223021;
    private const int LandingRemake = 5000;
    private const int PlanetEarthShow = 1044;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeTmdbServer _tmdb = null!;
    private Guid _workId;

    public LibraryLinkFindHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(LandingId, "120. Landing Stripling", null, 1962, LandingPath));
        jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, LandingFilm, "Посадка салаги", "Landing Stripling", "1962-04-20", PosterPath: "/landing.jpg", ImdbId: "tt0056164", RuntimeMinutes: 6));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, LandingRemake, "Landing Stripling", ReleaseDate: "1999-01-01", RuntimeMinutes: 95));
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, PlanetEarthShow, "Планета Земля", "Planet Earth", "2006-03-05", ImdbId: "tt0795176",
            Seasons: [new FakeTmdbSeason(0, EpisodeCount: 24), new FakeTmdbSeason(1, "2006-03-05", 11)], RuntimeMinutes: 53));
        _tmdb.AddFind("tt0056164", MediaWorkKind.Movie, LandingFilm);
        _tmdb.AddFind("tt0795176", MediaWorkKind.Series, PlanetEarthShow);
        var library = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await _factory.SyncLibraryAsync(library.Id, _owner.AccessToken, cancellationToken);
        await _factory.WithDbAsync(async db => _workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == LandingId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Search_finds_the_films_and_shows_of_a_title_ranked_for_the_work_and_stores_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", null, LandingRemake, LandingFilm);
        _tmdb.AddSearch(MediaWorkKind.Series, "Landing Stripling", null, PlanetEarthShow);

        //Act
        var response = await _factory.GetHttpRequest(SearchUrl("Landing Stripling"), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<LinkCandidateListResponse>(response, cancellationToken);
        // The 1962 film first: its year is the work's; TMDb listed the remake first. A show for a film is an episode link.
        Assert.Equal(
            [(MediaWorkKind.Movie, "223021", true), (MediaWorkKind.Movie, "5000", true), (MediaWorkKind.Series, "1044", false)],
            list.Items.Select(item => (item.CatalogKind, item.CatalogId, item.Evidence.KindAgrees)));
        Assert.Equal("/api/recommendations/posters/landing.jpg", list.Items[0].PosterUrl);
        // Films and shows, the first page each, without a year.
        Assert.Equal(
            [("movie", "Landing Stripling", (string?)null), ("tv", "Landing Stripling", null)],
            Searches());
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.CatalogWorks.AsNoTracking().Where(work => work.DetailsUtc != null).Select(work => work.CatalogId).ToListAsync(cancellationToken);
            Assert.Equal(["1044", "223021", "5000"], stored.Order(StringComparer.Ordinal));
            // A search shown is not a suggestion stored for the work.
            Assert.Equal(0, await db.LibraryLinkSearches.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Search_with_a_year_sends_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, LandingFilm);

        //Act
        var response = await _factory.GetHttpRequest(SearchUrl("  Landing Stripling ", " 1962"), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadAsync<LinkCandidateListResponse>(response, cancellationToken)).Items);
        Assert.Equal("223021", item.CatalogId);
        Assert.Equal([("movie", "Landing Stripling", (string?)"1962"), ("tv", "Landing Stripling", "1962")], Searches());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_without_a_title_is_400_and_asks_tmdb_nothing(string? title)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(SearchUrl(title), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Type a title to search for.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData("nineteen")]
    [InlineData("0")]
    [InlineData("-1962")]
    [InlineData("19620")]
    public async Task Search_with_a_year_that_is_not_one_is_400(string year)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(SearchUrl("Landing Stripling", year), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"“{year}” is not a year.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Search_without_a_catalog_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(SearchUrl("Landing Stripling"), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect a metadata catalog under Integrations first.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Search_of_an_unknown_work_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/tmdb/search?title=Landing", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Search_when_tmdb_fails_is_502_with_its_sentence_and_never_the_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        _tmdb.Intercept = request => request.RequestUri!.AbsolutePath.StartsWith("/3/search/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;

        //Act
        var response = await _factory.GetHttpRequest(SearchUrl("Landing Stripling"), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = await response.ReadErrorAsync(cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain(FakeTmdbServer.Token, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/movie/223021-landing-stripling", null, MediaWorkKind.Movie, "223021", null, null)]
    [InlineData("themoviedb.org/tv/1044-planet-earth", null, MediaWorkKind.Series, "1044", 1, null)]
    [InlineData("https://www.themoviedb.org/tv/1044/season/1/episode/8", null, MediaWorkKind.Series, "1044", 1, 8)]
    [InlineData("https://www.imdb.com/title/tt0056164/", null, MediaWorkKind.Movie, "223021", null, null)]
    [InlineData("https://m.imdb.com/title/tt0795176/?ref_=nv", "Movie", MediaWorkKind.Series, "1044", 1, null)]
    [InlineData(" TT0056164 ", null, MediaWorkKind.Movie, "223021", null, null)]
    [InlineData("223021", "Movie", MediaWorkKind.Movie, "223021", null, null)]
    [InlineData("1044", "series", MediaWorkKind.Series, "1044", 1, null)]
    public async Task Lookup_reads_every_form_of_a_pasted_id(
        string text, string? kind, MediaWorkKind expectedKind, string expectedId, int? expectedSeason, int? expectedEpisode)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl(text, kind), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadAsync<LinkCandidateListResponse>(response, cancellationToken)).Items);
        // A show for this film is an episode-to-show link: the season the link names, else the show's only one.
        Assert.Equal((expectedKind, expectedId, expectedSeason, expectedEpisode), (item.CatalogKind, item.CatalogId, item.SeasonNumber, item.EpisodeNumber));
        await _factory.WithDbAsync(async db => Assert.True(await db.CatalogWorks.AnyAsync(
            work => work.Kind == expectedKind && work.CatalogId == expectedId && work.DetailsUtc != null, cancellationToken)));
    }

    [Fact]
    public async Task Lookup_of_an_imdb_id_of_a_film_and_a_show_answers_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        _tmdb.AddFind("tt0000001", MediaWorkKind.Movie, LandingRemake);
        _tmdb.AddFind("tt0000001", MediaWorkKind.Series, PlanetEarthShow);

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl("tt0000001", null), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<LinkCandidateListResponse>(response, cancellationToken);
        Assert.Equal(
            [(MediaWorkKind.Movie, "5000"), (MediaWorkKind.Series, "1044")],
            list.Items.Select(item => (item.CatalogKind, item.CatalogId)));
    }

    [Fact]
    public async Task Lookup_of_a_bare_number_without_film_or_show_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl("223021", null), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Choose film or show for a bare number.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Lookup_of_a_bare_number_of_another_kind_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl("223021", "Episode"), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("“Episode” is not a TMDb kind. Use Movie or Series.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Landing Stripling")]
    [InlineData("https://example.com/movie/223021")]
    public async Task Lookup_of_text_that_names_no_id_is_400(string? text)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl(text, "Movie"), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PasteInvalidError, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/movie/999999", "TMDb has no film 999999.")]
    [InlineData("https://www.themoviedb.org/tv/999999", "TMDb has no show 999999.")]
    [InlineData("tt9999999", ImdbNotFoundError)]
    public async Task Lookup_of_an_id_tmdb_does_not_have_is_404_with_the_sentence(string text, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl(text, null), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Lookup_without_a_catalog_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(LookupUrl("tt0056164", null), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect a metadata catalog under Integrations first.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Lookup_of_an_unknown_work_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/tmdb/lookup?text=tt0056164", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    private string SearchUrl(string? title, string? year = null) =>
        QueryHelpers.AddQueryString($"{BaseUrl}/{_workId}/tmdb/search", Parameters(("title", title), ("year", year)));

    private string LookupUrl(string? text, string? kind) =>
        QueryHelpers.AddQueryString($"{BaseUrl}/{_workId}/tmdb/lookup", Parameters(("text", text), ("kind", kind)));

    private static Dictionary<string, string?> Parameters(params (string Name, string? Value)[] parameters) =>
        parameters.Where(parameter => parameter.Value is not null).ToDictionary(parameter => parameter.Name, parameter => parameter.Value);

    /// <summary>The catalog searches sent, in order: the kind's path, the query, the year.</summary>
    private List<(string Kind, string Query, string? Year)> Searches() =>
    [
        .. _factory.TmdbHttp.Requests
            .Where(request => request.Uri.AbsolutePath.StartsWith("/3/search/", StringComparison.Ordinal))
            .Select(request =>
            {
                var query = QueryHelpers.ParseQuery(request.Uri.Query);
                var year = query.TryGetValue("year", out var film) ? film.ToString()
                    : query.TryGetValue("first_air_date_year", out var show) ? show.ToString()
                    : null;
                return (request.Uri.AbsolutePath["/3/search/".Length..], query["query"].ToString(), year);
            }),
    ];

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
