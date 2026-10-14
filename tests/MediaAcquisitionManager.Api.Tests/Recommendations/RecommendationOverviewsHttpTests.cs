using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// A recommended work's description (GET /api/recommendations/overviews/{kind}/{catalogId}?language=): asked of TMDb on
// the first request in the language asked — English when none is — then in English when TMDb has none in it, and kept
// per language, the "none" too; the next request reads the database. Only a work the catalog has, else a bare 404 with
// nothing asked; 400 for a kind, an id or a language the request doesn't take; read again once older than the catalog's
// refresh age, the kept one served while TMDb can't be asked; 409 with nothing kept and no catalog; 502 with TMDb's
// sentence for the owner, a plain one for anyone else. Signed in only.
public sealed class RecommendationOverviewsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations/overviews";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string English = "Thirty years after defeating the Galactic Empire, Han Solo and his allies face a new threat.";
    private const string Ukrainian = "Через тридцять років після перемоги над Галактичною імперією Хан Соло стикається з новою загрозою.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _anna = null!;
    private IntegrationConnectionEntity _tmdb = null!;
    private FakeTmdbServer _server = null!;
    private CatalogWorkEntity _film = null!;

    public RecommendationOverviewsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _anna = await users.CreateAuthorized("anna");
        _tmdb = await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _server = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _film = await _works.Create(MediaWorkKind.Movie, "140607", "Зоряні війни: Пробудження Сили", new DateOnly(2015, 12, 15), catalog: _tmdb);
        _server.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, 140607, "Зоряні війни: Пробудження Сили",
            OverviewsByLanguage: new Dictionary<string, string> { ["en"] = English, ["uk-UA"] = Ukrainian }));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Overview_without_a_token_is_401_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Overview_first_asked_for_is_fetched_from_tmdb_in_the_language_asked_and_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(response, cancellationToken));
        // One request, for the details in that language with nothing appended, with the token.
        var request = Assert.Single(_factory.TmdbHttp.Requests);
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/movie/140607?language=en", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {FakeTmdbServer.Token}"], request.HeaderValues("Authorization"));
        var kept = Assert.Single(await KeptAsync());
        Assert.Equal((_film.Id, "en", English, _factory.Time.GetUtcNow().UtcDateTime), (kept.CatalogWorkId, kept.Language, kept.Overview, kept.FetchedUtc));
    }

    [Fact]
    public async Task Overview_with_no_language_given_is_asked_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(response, cancellationToken));
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/movie/140607?language=en", Assert.Single(_factory.TmdbHttp.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Overview_asked_for_again_is_read_from_the_database_without_asking_tmdb()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(response, cancellationToken));
        Assert.Single(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Overview_in_another_language_is_asked_in_it_and_kept_beside_the_english_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=uk-UA", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(Ukrainian, "uk-UA"), await ReadAsync(response, cancellationToken));
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/movie/140607?language=uk-UA", _factory.TmdbHttp.Requests[^1].Uri.AbsoluteUri);
        Assert.Equal([("en", English), ("uk-UA", Ukrainian)], (await KeptAsync()).Select(row => (row.Language, row.Overview)).Order());
    }

    [Fact]
    public async Task Overview_tmdb_has_none_of_in_the_language_asked_falls_back_to_english_keeping_both_answers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, 140607, "Зоряні війни: Пробудження Сили",
            OverviewsByLanguage: new Dictionary<string, string> { ["en"] = English }));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=uk-UA", _anna.AccessToken);
        var again = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=uk-UA", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Marked English, so the dialog can say the text isn't in the language asked.
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(response, cancellationToken));
        Assert.Equal(
            [$"{FakeTmdbServer.BaseUrl}/3/movie/140607?language=uk-UA", $"{FakeTmdbServer.BaseUrl}/3/movie/140607?language=en"],
            _factory.TmdbHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        Assert.Equal([("en", English), ("uk-UA", (string?)null)], (await KeptAsync()).Select(row => (row.Language, row.Overview)).Order());
        // Both answers kept, the "none" too: the second request asks nothing.
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(again, cancellationToken));
        Assert.Equal(2, _factory.TmdbHttp.Requests.Count);
    }

    [Fact]
    public async Task Overview_tmdb_has_in_no_language_is_none_and_that_answer_is_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 140607, "Зоряні війни: Пробудження Сили", OverviewsByLanguage: new Dictionary<string, string>()));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);
        var again = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(null, null), await ReadAsync(response, cancellationToken));
        Assert.Equal(new RecommendationOverviewResponse(null, null), await ReadAsync(again, cancellationToken));
        Assert.Single(_factory.TmdbHttp.Requests);
        Assert.Null(Assert.Single(await KeptAsync()).Overview);
    }

    [Fact]
    public async Task Overview_of_a_show_asks_tmdb_for_the_show()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.Create(MediaWorkKind.Series, "1399", "Гра престолів", new DateOnly(2011, 4, 17), catalog: _tmdb);
        _server.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 1399, "Гра престолів", Overview: "Seven noble families fight for control of Westeros."));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Series/1399?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse("Seven noble families fight for control of Westeros.", "en"), await ReadAsync(response, cancellationToken));
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/tv/1399?language=en", Assert.Single(_factory.TmdbHttp.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Overview_kept_longer_than_the_refresh_age_is_asked_again_and_replaced()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);
        _server.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie, 140607, "Зоряні війни: Пробудження Сили",
            OverviewsByLanguage: new Dictionary<string, string> { ["en"] = "Rewritten." }));
        await AgeKeptAsync(CatalogStore.RefreshAge + TimeSpan.FromDays(1));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(new RecommendationOverviewResponse("Rewritten.", "en"), await ReadAsync(response, cancellationToken));
        Assert.Equal(2, _factory.TmdbHttp.Requests.Count);
        var kept = Assert.Single(await KeptAsync());
        Assert.Equal(("Rewritten.", _factory.Time.GetUtcNow().UtcDateTime), (kept.Overview, kept.FetchedUtc));
    }

    [Fact]
    public async Task Overview_kept_longer_than_the_refresh_age_is_served_while_tmdb_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);
        await AgeKeptAsync(CatalogStore.RefreshAge + TimeSpan.FromDays(1));
        _server.Intercept = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Overview_kept_is_served_with_no_catalog_connected()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);
        await SwitchCatalogOffAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationOverviewResponse(English, "en"), await ReadAsync(response, cancellationToken));
        Assert.Single(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Overview_not_kept_with_no_catalog_connected_is_409_and_keeps_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SwitchCatalogOffAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect a metadata catalog under Integrations first.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
        Assert.Empty(await KeptAsync());
    }

    [Fact]
    public async Task Overview_of_a_work_the_catalog_doesnt_have_is_a_bare_404_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var film = await _factory.GetHttpRequest($"{BaseUrl}/Movie/603?language=en", _anna.AccessToken);
        // The same number names a film here, never a show.
        var show = await _factory.GetHttpRequest($"{BaseUrl}/Series/140607?language=en", _anna.AccessToken);

        //Assert
        // Only a work the catalog has is asked about: the endpoint is no open proxy to TMDb.
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (film.StatusCode, show.StatusCode));
        Assert.Empty(await film.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData("Episode/140607?language=en", "“Episode” is not a kind of work. Use Movie or Series.")]
    [InlineData("Movie/tt2488496?language=en", "“tt2488496” is not a catalog id, which is a number such as 603.")]
    [InlineData("Movie/140607?language=english", "“english” is not a language code, which is two letters with a region when wanted, such as en or uk-UA.")]
    [InlineData("Movie/140607?language=uk-ua", "“uk-ua” is not a language code, which is two letters with a region when wanted, such as en or uk-UA.")]
    [InlineData("Movie/140607?language=en%26append_to_response%3Dcredits", "“en&append_to_response=credits” is not a language code, which is two letters with a region when wanted, such as en or uk-UA.")]
    public async Task Overview_with_a_kind_id_or_language_the_request_doesnt_take_is_400_and_asks_nothing(string path, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{path}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Overview_whose_request_fails_is_502_with_tmdbs_sentence_for_the_owner_and_nothing_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.Intercept = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _owner.AccessToken);
        var again = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("TMDb answered 500 Internal Server Error. Its own log says why.", await response.ReadErrorAsync(cancellationToken));
        // Nothing kept, so the next request asks again.
        Assert.Equal(HttpStatusCode.BadGateway, again.StatusCode);
        Assert.Equal(2, _factory.TmdbHttp.Requests.Count);
        Assert.Empty(await KeptAsync());
    }

    [Fact]
    public async Task Overview_whose_request_fails_is_502_with_a_plain_sentence_for_anyone_but_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.Intercept = _ => throw new HttpRequestException("Connection refused.");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/Movie/140607?language=en", _anna.AccessToken);

        //Assert
        // TMDb's sentence names the address the owner saved: the rule of a run's error, a job's and a poster's.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("The metadata catalog couldn't send this description.", await response.ReadErrorAsync(cancellationToken));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<RecommendationOverviewResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<RecommendationOverviewResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task<List<CatalogWorkOverviewEntity>> KeptAsync()
    {
        List<CatalogWorkOverviewEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.CatalogWorkOverviews.AsNoTracking().ToListAsync());
        return rows;
    }

    // Backdated in the database rather than by moving the clock, which would expire the users' tokens too.
    private Task AgeKeptAsync(TimeSpan by)
    {
        var fetchedUtc = _factory.Time.GetUtcNow().UtcDateTime - by;
        return _factory.WithDbAsync(db =>
            db.CatalogWorkOverviews.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FetchedUtc, fetchedUtc)));
    }

    // Straight to the column, as the owner's switch on the Integrations page leaves it: a switched-off catalog is none.
    private Task SwitchCatalogOffAsync() => _factory.WithDbAsync(db =>
        db.IntegrationConnections
            .Where(integration => integration.Id == _tmdb.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false)));
}
