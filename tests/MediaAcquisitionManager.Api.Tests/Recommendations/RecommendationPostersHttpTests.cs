using System.Net;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — a recommended work's poster (GET /api/recommendations/posters/{fileName}), served by the app: only a name
// that is a poster file name and some catalog work's poster path, else a bare 404 with nothing asked of anyone; fetched
// from TMDb's image host once, without the token, and kept in <data>/posters/ — the next request reads the disk, a
// catalog connected or not; 404 when the image host has none or no catalog could fetch it; 502 with the catalog's
// sentence for the owner, a plain one for anyone else, when the fetch fails. Signed in only; cached privately for 30 days.
public sealed class RecommendationPostersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations/posters";
    private const string FileName = "r2J02Z2OpNTctfOSN1Ydgii51I3.jpg";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _anna = null!;
    private IntegrationConnectionEntity _tmdb = null!;
    private FakeTmdbServer _server = null!;

    public RecommendationPostersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    private string PostersDirectory => Path.Combine(_factory.DataDirectory, "posters");

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        // The data directory outlives a reset: each test starts with no poster kept.
        if (Directory.Exists(PostersDirectory))
            Directory.Delete(PostersDirectory, recursive: true);
        _works = new CatalogWorkSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _anna = await users.CreateAuthorized("anna");
        _tmdb = await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _server = FakeTmdbServer.Arm(_factory.TmdbHttp);
        await PlantWorkAsync("/" + FileName);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Poster_without_a_token_is_401_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Poster_first_asked_for_is_fetched_from_the_image_host_without_the_token_and_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakeTmdbServer.PosterJpeg(), await response.Content.ReadAsByteArrayAsync(cancellationToken));
        // One request, to TMDb's image host at the adapter's size, and the token never goes there (ADR 0007).
        var request = Assert.Single(_factory.TmdbHttp.Requests);
        Assert.Equal($"https://{FakeTmdbServer.ImageHost}/t/p/w342/{FileName}", request.Uri.AbsoluteUri);
        Assert.Empty(request.HeaderValues("Authorization"));
        // Kept whole, under the data directory, and nothing else left there.
        Assert.Equal(FakeTmdbServer.PosterJpeg(), await File.ReadAllBytesAsync(Path.Combine(PostersDirectory, FileName), cancellationToken));
        Assert.Equal([FileName], Directory.GetFiles(PostersDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Poster_is_cached_privately_for_thirty_days_with_the_security_headers()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Private: the answer needs the user's token. Immutable: a catalog's poster path names one image for good.
        Assert.Equal("private, max-age=2592000, immutable", response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal([SecurityHeadersMiddleware.ContentSecurityPolicy], response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal(["DENY"], response.Headers.GetValues("X-Frame-Options"));
    }

    [Fact]
    public async Task Poster_asked_for_again_is_read_from_disk_without_asking_the_image_host()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);
        var second = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(FakeTmdbServer.PosterJpeg(), await second.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal("private, max-age=2592000, immutable", second.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Single(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Poster_kept_on_disk_is_served_with_no_catalog_connected()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(PostersDirectory);
        byte[] kept = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];
        await File.WriteAllBytesAsync(Path.Combine(PostersDirectory, FileName), kept, cancellationToken);
        await SwitchCatalogOffAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(kept, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Poster_of_a_png_is_served_as_a_png()
    {
        //Arrange
        await PlantWorkAsync("/kZ9DZp4oYxQ5Vo7x0bIHBzlLMhN.png", "604");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/kZ9DZp4oYxQ5Vo7x0bIHBzlLMhN.png", _anna.AccessToken);

        //Assert
        // By the file name's extension, the only two the adapter keeps.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Poster_no_catalog_work_has_is_a_bare_404_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/kqjL17yufvn9OVLyXYpvtyrFfak.jpg", _anna.AccessToken);

        //Assert
        // Only a poster the catalog named is fetched: the endpoint is no open proxy to the image host.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory));
    }

    [Theory]
    [InlineData("poster.gif")]
    [InlineData("poster")]
    [InlineData(".jpg")]
    [InlineData("a.b.jpg")]
    [InlineData("poster.JPG")]
    [InlineData("poster.jpg.png.exe")]
    [InlineData("..%5Cmam.db")]
    [InlineData("a%20b.jpg")]
    [InlineData("mam.db")]
    public async Task Poster_with_a_name_that_is_no_poster_file_name_is_a_bare_404_even_when_a_work_has_it(string name)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Planted as a work's poster path, so only the name's pattern stands between it and the disk or the image host.
        await PlantWorkAsync("/" + Uri.UnescapeDataString(name), "605");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{name}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Poster_not_kept_with_no_catalog_connected_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SwitchCatalogOffAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        // Nothing to fetch it from: the page shows its placeholder, as for no poster.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Poster_the_image_host_doesnt_have_is_a_bare_404_and_nothing_is_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.Intercept = request => request.RequestUri!.Host == FakeTmdbServer.ImageHost ? new HttpResponseMessage(HttpStatusCode.NotFound) : null;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(File.Exists(Path.Combine(PostersDirectory, FileName)));
    }

    [Fact]
    public async Task Poster_whose_fetch_fails_is_502_with_the_catalogs_sentence_for_the_owner_and_nothing_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.Intercept = request =>
            request.RequestUri!.Host == FakeTmdbServer.ImageHost ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);
        var again = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("TMDb answered 500 Internal Server Error. Its own log says why.", await response.ReadErrorAsync(cancellationToken));
        Assert.False(response.Headers.NonValidated.Contains("Cache-Control"));
        // Nothing kept, so the next request asks again.
        Assert.Equal(HttpStatusCode.BadGateway, again.StatusCode);
        Assert.Equal(2, _factory.TmdbHttp.Requests.Count);
        Assert.False(Directory.Exists(PostersDirectory) && Directory.EnumerateFileSystemEntries(PostersDirectory).Any());
    }

    [Fact]
    public async Task Poster_whose_fetch_fails_is_502_with_a_plain_sentence_for_anyone_but_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _server.Intercept = request => request.RequestUri!.Host == FakeTmdbServer.ImageHost
            ? throw new HttpRequestException("Connection refused.")
            : (HttpResponseMessage?)null;

        //Act
        var member = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);
        var owner = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        // The catalog's sentence can quote the address the owner saved: the rule of a run's error and a job's.
        Assert.Equal(HttpStatusCode.BadGateway, member.StatusCode);
        Assert.Equal("The metadata catalog couldn't send this poster.", await member.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadGateway, owner.StatusCode);
        // MAM-33 — the image host the poster was fetched from, never the address the owner saved for the API.
        Assert.StartsWith($"Couldn't reach TMDb at https://{FakeTmdbServer.ImageHost}:", await owner.ReadErrorAsync(cancellationToken), StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A catalog work whose poster is at <paramref name="posterPath"/>; no list need recommend it.</summary>
    private Task<CatalogWorkEntity> PlantWorkAsync(string posterPath, string catalogId = "603") =>
        _works.Create(MediaWorkKind.Movie, catalogId, $"Фільм {catalogId}", new DateOnly(1999, 3, 31), catalog: _tmdb, posterPath: posterPath);

    // Straight to the column, as the owner's switch on the Integrations page leaves it: a switched-off catalog is none.
    private Task SwitchCatalogOffAsync() => _factory.WithDbAsync(db =>
        db.IntegrationConnections
            .Where(integration => integration.Id == _tmdb.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false)));
}
