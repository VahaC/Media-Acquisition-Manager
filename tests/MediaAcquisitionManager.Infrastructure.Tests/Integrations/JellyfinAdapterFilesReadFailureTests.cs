using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-25 — what goes wrong while Jellyfin is read for a new file's item (IMediaLibraryFileLookup.ReadFilesAsync), and what
// the caller is told. A failure anywhere — the administrator's read, an item's, a series' — fails the whole call with that
// read's status and sentence and gives no reading at all, so an unavailable or odd Jellyfin can never let a file pass the
// check (specification §3.4, check 4). Only a 404 of an item's id is an answer, not a failure: no item has that id.
public sealed class JellyfinAdapterFilesReadFailureTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string FilmPath = "/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.mkv";

    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string LibraryForbiddenError =
        "Jellyfin accepted the key but won't let it read the library (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private const string NoAdministratorError = "Jellyfin has no enabled administrator account to read the item as.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };

    public JellyfinAdapterFilesReadFailureTests() => _server.Arm(_harness.JellyfinHttp);

    private IMediaLibraryFileLookup Lookup => _harness.Adapter<IMediaLibraryFileLookup>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, LibraryForbiddenError)]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin wasn't found at this address (404 Not Found for /Users). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.")]
    public async Task ReadFiles_whose_users_read_fails_fails_with_its_status_and_reads_no_item(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _server.Intercept = request => request.RequestUri!.AbsolutePath == "/Users" ? new HttpResponseMessage((HttpStatusCode)statusCode) : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((expectedStatus, expectedMessage), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task ReadFiles_without_an_enabled_administrator_is_refused()
    {
        //Arrange
        // Jellyfin reads one item only as a user, and only an administrator sees every library.
        foreach (var user in _server.Users.OfType<JsonObject>())
            user["Policy"]!["IsAdministrator"] = false;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.Refused, NoAdministratorError), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, LibraryForbiddenError)]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "Jellyfin answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(400, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 400 Bad Request, which it doesn't send for this request. Check the address.")]
    [InlineData(302, IntegrationCallStatus.UnexpectedResponse,
        "The address answered with a redirect. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    public async Task ReadFiles_whose_item_read_fails_fails_with_its_status_and_gives_no_reading(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        // The second path's first read fails: the first path's reading is not given either — never half a picture.
        var listedPath = (string)JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId)["Path"]!;
        var failing = JellyfinItemIds.Of(JellyfinItemIds.MovieType, FilmPath);
        _server.Intercept = request => request.RequestUri!.AbsolutePath == $"/Items/{failing}" ? new HttpResponseMessage((HttpStatusCode)statusCode) : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [listedPath, FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((expectedStatus, expectedMessage), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
        // The failed read is the last: no other type is tried after it, and nothing is sent again.
        Assert.Equal($"/Items/{failing}", _harness.JellyfinHttp.Requests[^1].Uri.AbsolutePath);
        Assert.Equal(3, _harness.JellyfinHttp.Requests.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"Name":"No type"}""")]
    [InlineData("""{"Type":"Movie","ProviderIds":"tt0078748"}""")]
    public async Task ReadFiles_of_an_item_answer_that_is_not_an_item_is_an_unexpected_response(string body)
    {
        //Arrange
        var movieId = JellyfinItemIds.Of(JellyfinItemIds.MovieType, FilmPath);
        _server.Intercept = request => request.RequestUri!.AbsolutePath == $"/Items/{movieId}"
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.UnexpectedResponse, NotJellyfinError), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin wasn't found at this address (404 Not Found for /Items). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.")]
    public async Task ReadFiles_whose_series_read_fails_fails_with_its_status_though_the_episode_was_read(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        // An episode without its show's ids can't be told to be the old copy's; a failed series read is a failed read.
        var episode = JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        _server.Episodes.Add(episode);
        _server.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _server.Intercept = request => request.RequestUri!.PathAndQuery.StartsWith("/Items?ids=", StringComparison.Ordinal)
            ? new HttpResponseMessage((HttpStatusCode)statusCode)
            : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [(string)episode["Path"]!], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((expectedStatus, expectedMessage), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"Items":{}}""")]
    [InlineData("not json")]
    public async Task ReadFiles_of_a_series_answer_that_is_not_a_list_is_an_unexpected_response(string body)
    {
        //Arrange
        var episode = JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        _server.Episodes.Add(episode);
        _server.Intercept = request => request.RequestUri!.PathAndQuery.StartsWith("/Items?ids=", StringComparison.Ordinal)
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [(string)episode["Path"]!], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.UnexpectedResponse, NotJellyfinError), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterTests.TransportFailures), MemberType = typeof(JellyfinAdapterTests))]
    public async Task ReadFiles_that_fails_on_the_way_has_the_connection_tests_sentence(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        // The item read fails, not the administrator's: an unreachable Jellyfin halfway through is no reading either.
        _server.Intercept = request => request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal)
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}")
            : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((expectedStatus, expectedMessage), (result.Status, TestTexts.En(result.Error)));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadFiles_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _harness.JellyfinHttp.Handler = (_, _) =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("The caller went away.");
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Lookup.ReadFilesAsync(Settings, [FilmPath], cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterFilesScanTests.LookupMistakes), MemberType = typeof(JellyfinAdapterFilesScanTests))]
    public async Task ReadFiles_refuses_a_callers_mistake_without_sending_anything(string mistake)
    {
        //Arrange
        var paths = JellyfinAdapterFilesScanTests.LookupMistake(mistake);

        //Act
        var exception = await Record.ExceptionAsync(() => Lookup.ReadFilesAsync(Settings, paths!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }
}
