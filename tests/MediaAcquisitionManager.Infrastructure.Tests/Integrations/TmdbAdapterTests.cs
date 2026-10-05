using System.IO.Pipelines;
using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — TMDb's registration and connection test: the request it sends (the token in the header and
// nowhere else), TMDb's real answers to a good and a wrong token (VendorSamples/Tmdb), and every other
// outcome a status and a sentence. The catalog's calls have files of their own.
public sealed class TmdbAdapterTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ0ZXN0In0.c2lnbmF0dXJlLW9mLXRoZS10b2tlbg";

    private const string NotTmdbError =
        "The service at this address answered, but not the way TMDb's API does. Check the address: TMDb's is https://api.themoviedb.org.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token);

    private readonly IntegrationAdapterHarness _harness = new();

    private IIntegrationAdapter Adapter => _harness.Adapter(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Descriptor_is_a_metadata_type_that_takes_an_api_key_only_and_reports_no_paths()
    {
        //Arrange

        //Act
        var descriptor = Adapter.Descriptor;

        //Assert
        // ADR 0005: "tmdb" is stored on every connection row; renaming it orphans them all.
        Assert.Equal(
            new IntegrationAdapterDescriptor(
                "tmdb", "TMDb", IntegrationKind.Metadata,
                CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false),
            descriptor);
    }

    [Fact]
    public async Task TestConnection_reads_the_authentication_sample_with_the_token_in_the_bearer_header()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.AuthenticationFile, TmdbSamples.MediaType);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to TMDb.", result.Message);
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BaseUrl}/3/authentication", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], request.HeaderValues("Accept"));
        // v3's api_key= would put the token into every URI a proxy logs.
        Assert.DoesNotContain(Token, request.Uri.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", request.Uri.OriginalString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://api.themoviedb.test", "https://api.themoviedb.test/3/authentication")]
    // An address copied with the version is taken as is, like Torznab's /api.
    [InlineData("https://api.themoviedb.test/3", "https://api.themoviedb.test/3/authentication")]
    [InlineData("http://proxy.test/tmdb", "http://proxy.test/tmdb/3/authentication")]
    [InlineData("http://proxy.test/tmdb/3", "http://proxy.test/tmdb/3/authentication")]
    public async Task TestConnection_adds_the_api_version_to_an_address_without_it(string baseUrl, string expectedUri)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.AuthenticationFile, TmdbSamples.MediaType);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings with { BaseUrl = baseUrl }, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(expectedUri, Assert.Single(_harness.TmdbHttp.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task TestConnection_with_a_wrong_token_is_authentication_failed_from_the_recorded_401()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Recorded(TmdbSamples.WrongKeyResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(
            "TMDb refused the key (401 Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API, not the shorter API key.",
            result.Message);
        // One attempt: a refused token is not sent again.
        Assert.Single(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_against_the_fake_tmdb_with_its_token_succeeds_and_with_another_is_refused()
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var accepted = await Adapter.TestConnectionAsync(settings, cancellationToken);
        var refused = await Adapter.TestConnectionAsync(settings with { ApiKey = "another-read-access-token" }, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, accepted.Status);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, refused.Status);
    }

    [Theory]
    // A 404 without TMDb's JSON: not TMDb's API, whatever answers there.
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "TMDb's API wasn't found at this address (404 Not Found). Use https://api.themoviedb.org.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "TMDb answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "TMDb answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(403, IntegrationCallStatus.UnexpectedResponse,
        "TMDb answered 403 Forbidden, which it doesn't send for this request. Check the address.")]
    [InlineData(418, IntegrationCallStatus.UnexpectedResponse, "TMDb answered 418, which it doesn't send for this request. Check the address.")]
    public async Task TestConnection_answers_each_failure_with_its_status_and_sentence_after_one_request(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made up by the server" };

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Message);
        // Only a 429 is sent again.
        Assert.Single(_harness.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("""{"success":""")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"success":false}""")]
    [InlineData("""{"success":"true"}""")]
    public async Task TestConnection_of_a_body_that_is_not_tmdbs_success_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTmdbError, result.Message);
    }

    [Fact]
    public async Task TestConnection_reports_a_redirect_and_follows_none()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Redirect("https://www.themoviedb.test/login?token=leaked-token-0001");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to https://www.themoviedb.test/login. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Message);
        // The token went to the saved address only.
        Assert.Single(_harness.TmdbHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_maps_a_transport_error_without_the_exception_text()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (request, _) =>
            throw new HttpRequestException(HttpRequestError.NameResolutionError, $"Socket failure on {request.RequestUri} with {Token}");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal("Couldn't reach TMDb at https://api.themoviedb.test: the host name can't be found.", result.Message);
    }

    [Fact]
    public async Task TestConnection_when_nothing_listens_is_unreachable()
    {
        //Arrange
        // The real network: port 9 (discard) on the loopback address refuses at once.
        using var harness = new IntegrationAdapterHarness(fakeNetwork: false);
        var settings = Settings with { BaseUrl = "http://127.0.0.1:9" };

        //Act
        var result = await harness.Adapter(TmdbAdapter.TypeId).TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(
            "Couldn't reach TMDb at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that TMDb is running.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        // The headers arrive, the body never does: only the client's timeout ends the wait.
        harness.TmdbHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

        //Act
        var result = await harness.Adapter(TmdbAdapter.TypeId).TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("TMDb at https://api.themoviedb.test didn't answer within 0.1 seconds.", result.Message);
    }

    [Fact]
    public void The_adapter_is_a_metadata_resolver_and_catalog_found_by_its_type()
    {
        //Arrange

        //Act
        var resolver = _harness.Adapter<IMetadataResolver>(TmdbAdapter.TypeId);
        var catalog = _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

        //Assert
        // One instance: the pacing gate is shared by every contract it serves.
        Assert.Same(Adapter, resolver);
        Assert.Same(Adapter, catalog);
    }
}
