using System.Net;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.Api.Tests.Http;

public sealed class SpaFallbackHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string UnknownApiUrl = "/api/nope";

    private readonly MediaAcquisitionManagerApiFactory _factory;

    public SpaFallbackHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("/")]
    [InlineData("/library")]
    [InlineData("/settings/profile")]
    public async Task Get_of_a_client_route_is_200_with_the_spa_shell(string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shell = await File.ReadAllTextAsync(RepositoryPaths.FrontendIndexHtml, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(url);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("<div id=\"root\">", body, StringComparison.Ordinal);
        // Byte for byte the copy in the factory's web root: proof the web-root override took effect.
        Assert.Equal(shell, body);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task Unknown_api_route_is_404_json_not_the_spa_shell(string method)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(new HttpMethod(method), UnknownApiUrl);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"No API endpoint matches {method} {UnknownApiUrl}.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Unknown_api_route_with_an_invalid_token_is_still_404_not_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(UnknownApiUrl, "not-a-token");

        //Assert
        // MAM-4 — the catch-all is anonymous: a typo must not read as an expired session.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"No API endpoint matches GET {UnknownApiUrl}.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/api/v2/libraries/42/items")]
    [InlineData("/api")]
    [InlineData("/api/health/ready/extra")]
    public async Task Unknown_api_route_at_any_depth_is_404_json(string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(url);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"No API endpoint matches GET {url}.", await response.ReadErrorAsync(cancellationToken));
    }
}
