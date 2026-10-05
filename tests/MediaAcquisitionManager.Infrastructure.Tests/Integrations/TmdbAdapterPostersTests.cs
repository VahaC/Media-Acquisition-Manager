using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — a poster from TMDb's image host: through the adapter's own named client (so the test host's
// handler answers it and nothing reaches the real host), without the token (ADR 0007), and kept only as
// a JPEG or a PNG.
public sealed class TmdbAdapterPostersTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJwb3N0ZXJzIn0.c2lnbmF0dXJlLW9mLXBvc3RlcnM";
    private const string PosterPath = "/esaQNJKjE8IDk5G0tmEOJL7AaEP.jpg";

    private const string NotAnImageError =
        "TMDb's image host answered the poster with something other than a JPEG or PNG image.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token);

    private readonly IntegrationAdapterHarness _harness = new();

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetPoster_fetches_the_w342_poster_from_tmdbs_image_host_without_the_token()
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);

        //Act
        var result = await Catalog.GetPosterAsync(Settings, PosterPath, TestContext.Current.CancellationToken);

        //Assert
        // The tmdb client's handler took it: the test host never reaches the real image host.
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        // The image host is the adapter's constant, never the saved address or anything an answer named.
        Assert.Equal("https://image.tmdb.org/t/p/w342/esaQNJKjE8IDk5G0tmEOJL7AaEP.jpg", request.Uri.AbsoluteUri);
        // A credential goes only to the address the owner saved (ADR 0007).
        Assert.Empty(request.HeaderValues("Authorization"));
        Assert.DoesNotContain(request.Headers, header => header.Value.Contains(Token, StringComparison.Ordinal));
        Assert.Equal(["image/jpeg", "image/png"], request.HeaderValues("Accept"));
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("image/jpeg", result.Value!.MediaType);
        Assert.Equal(FakeTmdbServer.PosterJpeg(), result.Value.Content);
    }

    [Theory]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("image/png", "image/png")]
    [InlineData("IMAGE/PNG", "image/png")]
    public async Task GetPoster_keeps_a_jpeg_or_a_png(string contentType, string expectedMediaType)
    {
        //Arrange
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        _harness.TmdbHttp.Handler = (_, _) => Image(bytes, contentType);

        //Act
        var result = await Catalog.GetPosterAsync(Settings, "/poster.png", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new CatalogImage(result.Value!.Content, expectedMediaType), result.Value);
        Assert.Equal(bytes, result.Value.Content);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("image/webp")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task GetPoster_of_anything_but_a_jpeg_or_a_png_is_an_unexpected_response(string? contentType)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => Image([0xFF, 0xD8, 0xFF, 0xD9], contentType);

        //Act
        var result = await Catalog.GetPosterAsync(Settings, PosterPath, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotAnImageError, result.Error);
    }

    [Fact]
    public async Task GetPoster_with_an_empty_body_is_an_unexpected_response()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => Image([], "image/jpeg");

        //Act
        var result = await Catalog.GetPosterAsync(Settings, PosterPath, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotAnImageError, result.Error);
    }

    [Fact]
    public async Task GetPoster_the_image_host_does_not_have_is_not_found()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text("<html>404</html>", HttpStatusCode.NotFound, "text/html");

        //Act
        var result = await Catalog.GetPosterAsync(Settings, PosterPath, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal("TMDb's image host has no poster at /esaQNJKjE8IDk5G0tmEOJL7AaEP.jpg.", result.Error);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, "Couldn't reach TMDb at https://image.tmdb.org: nothing accepted the connection. Check the address and port, and that TMDb is running.")]
    [InlineData(HttpRequestError.NameResolutionError, "Couldn't reach TMDb at https://image.tmdb.org: the host name can't be found.")]
    public async Task GetPoster_that_cannot_reach_the_image_host_names_the_image_host_not_the_saved_address(HttpRequestError error, string expected)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => throw new HttpRequestException(error, "The connection failed.");

        //Act
        var result = await Catalog.GetPosterAsync(Settings, PosterPath, TestContext.Current.CancellationToken);

        //Assert
        // MAM-33 — the poster goes to TMDb's image host, never to the address the owner saved, so a sentence naming that
        // address would send them checking the wrong one.
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(expected, result.Error);
        Assert.DoesNotContain(BaseUrl, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPoster_answered_with_a_redirect_follows_none()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Redirect("https://cdn.elsewhere.test/poster.jpg");

        //Act
        var result = await Catalog.GetPosterAsync(Settings, PosterPath, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Single(_harness.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("poster.jpg")]
    [InlineData("/")]
    [InlineData("/.jpg")]
    [InlineData("/poster.gif")]
    [InlineData("/poster.JPG")]
    [InlineData("/../poster.jpg")]
    [InlineData("/t/p/original/poster.jpg")]
    [InlineData("//evil.test/poster.jpg")]
    [InlineData("https://evil.test/poster.jpg")]
    [InlineData("/poster.jpg?size=original")]
    [InlineData("/poster.jpg#x")]
    [InlineData("/poster.jpg\n")]
    [InlineData("/poster%2F..%2Fx.jpg")]
    [InlineData("/постер.jpg")]
    public async Task GetPoster_with_a_path_tmdb_never_gives_throws_without_sending_anything(string posterPath)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Catalog.GetPosterAsync(Settings, posterPath, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }

    private static HttpResponseMessage Image(byte[] bytes, string? contentType)
    {
        var content = new ByteArrayContent(bytes);
        if (contentType is not null)
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
