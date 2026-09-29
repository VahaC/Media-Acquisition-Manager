using System.IO.Pipelines;
using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

public sealed class JellyfinAdapterTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    private IIntegrationAdapter Adapter => _harness.Adapter(JellyfinAdapter.TypeId);

    /// <summary>
    /// A transport failure of each kind, thrown with text that names the address and the key: the
    /// sentence must come from the kind alone.
    /// </summary>
    public static TheoryData<HttpRequestError, IntegrationCallStatus, string> TransportFailures => new()
    {
        {
            HttpRequestError.NameResolutionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach Jellyfin at http://jellyfin.test:8096: the host name can't be found."
        },
        {
            HttpRequestError.ConnectionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach Jellyfin at http://jellyfin.test:8096: nothing accepted the connection. Check the address and port, and that Jellyfin is running."
        },
        {
            HttpRequestError.SecureConnectionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach Jellyfin at http://jellyfin.test:8096: the secure (HTTPS) connection failed, for example because its certificate isn't trusted."
        },
        {
            HttpRequestError.ConfigurationLimitExceeded, IntegrationCallStatus.UnexpectedResponse,
            "The answer from http://jellyfin.test:8096 is far larger than anything Jellyfin sends. Check the address."
        },
        {
            HttpRequestError.ResponseEnded, IntegrationCallStatus.Unreachable,
            "Couldn't reach Jellyfin at http://jellyfin.test:8096: the connection failed before it answered."
        },
        {
            HttpRequestError.InvalidResponse, IntegrationCallStatus.Unreachable,
            "Couldn't reach Jellyfin at http://jellyfin.test:8096: the connection failed before it answered."
        },
        {
            HttpRequestError.Unknown, IntegrationCallStatus.Unreachable,
            "Couldn't reach Jellyfin at http://jellyfin.test:8096: the connection failed before it answered."
        },
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TestConnection_reads_version_and_server_name_from_the_system_info_sample()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json; charset=utf-8");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Jellyfin 12.1.0 (VahaC media).", result.Message);
    }

    [Theory]
    [InlineData("""{"Version":"12.1.0","ProductName":"Jellyfin Server"}""")]
    [InlineData("""{"Version":"12.1.0","ProductName":"Jellyfin Server","ServerName":" \t "}""")]
    [InlineData("""{"Version":"12.1.0","ServerName":null}""")]
    public async Task TestConnection_without_a_server_name_leaves_it_out(string systemInfo)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(systemInfo, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Jellyfin 12.1.0.", result.Message);
    }

    [Fact]
    public async Task TestConnection_gets_system_info_with_the_media_browser_token_header()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json");

        //Act
        await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        // /System/Info, not /System/Info/Public: the public one answers without a key and proves nothing.
        Assert.Equal("http://jellyfin.test:8096/System/Info", request.Uri.AbsoluteUri);
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], request.HeaderValues("Accept"));
        // The legacy ways, off by default in Jellyfin 12 — and a key in the URL lands in proxy logs.
        Assert.Empty(request.HeaderValues("X-Emby-Token"));
        Assert.Empty(request.HeaderValues("X-MediaBrowser-Token"));
        Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnection_keeps_a_base_path_of_the_address()
    {
        //Arrange
        var settings = Settings with { BaseUrl = "http://jellyfin.test/jellyfin" };
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal("http://jellyfin.test/jellyfin/System/Info", request.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin accepted the key but won't show it the server's details (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin wasn't found at this address (404 Not Found for /System/Info). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.")]
    [InlineData(500, IntegrationCallStatus.ServiceError,
        "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(204, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 204 No Content, which it doesn't send for this request. Check the address.")]
    // No standard reason phrase: the number alone, never the phrase the server made up.
    [InlineData(418, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 418, which it doesn't send for this request. Check the address.")]
    public async Task TestConnection_answers_each_failure_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) =>
            new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made ‮up by the server" };

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Message);
    }

    [Fact]
    public async Task TestConnection_of_another_product_is_an_unexpected_response()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """{"Version":"4.8.10.0","ProductName":"Emby Server","ServerName":"Living room"}""", HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address is Emby Server, not Jellyfin.", result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("""{"Version":""")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Version":""}""")]
    [InlineData("""{"Version":"  "}""")]
    [InlineData("""{"Version":12}""")]
    [InlineData("""{"Version":"12.1.0","ProductName":12}""")]
    [InlineData("""{"Version":"12.1.0","ProductName":""}""")]
    public async Task TestConnection_of_a_body_without_a_version_or_not_json_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotJellyfinError, result.Message);
    }

    [Theory]
    // The query and the user info can hold a credential, so neither is repeated.
    [InlineData("https://owner:hunter2-secret@media.test/jellyfin/web/index.html?api_key=0123456789abcdef#home",
        "The address answered with a redirect to https://media.test/jellyfin/web/index.html. ")]
    [InlineData("/web/index.html?x=1", "The address answered with a redirect to http://jellyfin.test:8096/web/index.html. ")]
    [InlineData(null, "The address answered with a redirect. ")]
    public async Task TestConnection_reports_a_redirect_and_sends_one_request(string? location, string expectedStart)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) =>
            location is null ? new HttpResponseMessage(HttpStatusCode.Found) : VendorResponses.Redirect(location, HttpStatusCode.MovedPermanently);

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            expectedStart + "Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Message);
        // Never followed: the key went nowhere but the address the owner entered.
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TestConnection_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Message);
        Assert.DoesNotContain("Socket failure", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnection_of_a_huge_answer_is_an_unexpected_response()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[IntegrationsInfrastructureExtensions.MaximumResponseBytes + 1]),
        };

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The answer from http://jellyfin.test:8096 is far larger than anything Jellyfin sends. Check the address.", result.Message);
    }

    [Fact]
    public async Task TestConnection_when_nothing_listens_is_unreachable()
    {
        //Arrange
        // The real network: port 9 (discard) on the loopback address refuses at once.
        using var harness = new IntegrationAdapterHarness(fakeNetwork: false);
        var settings = Settings with { BaseUrl = "http://127.0.0.1:9" };

        //Act
        var result = await harness.Adapter(JellyfinAdapter.TypeId).TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(
            "Couldn't reach Jellyfin at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that Jellyfin is running.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        // The headers arrive, the body never does: only the client's timeout ends the wait.
        harness.JellyfinHttp.Handler = (_, _) => StalledAnswer();

        //Act
        var result = await harness.Adapter(JellyfinAdapter.TypeId).TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 0.1 seconds.", result.Message);
    }

    [Fact]
    public async Task TestConnection_whose_connection_is_never_accepted_is_timed_out_after_the_connect_timeout()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectTimeout = await ConnectTimeoutExceptionAsync(cancellationToken);
        _harness.JellyfinHttp.Handler = (_, _) => throw connectTimeout;

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, cancellationToken);

        //Assert
        // The handler's 5 seconds, not the client's 15, and no slow service: nothing took the connection.
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal(
            "Jellyfin at http://jellyfin.test:8096 didn't accept the connection within 5 seconds. Check the address and port, and that Jellyfin is running.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // The caller goes away while the answer is still arriving.
        _harness.JellyfinHttp.Handler = (_, _) =>
        {
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Adapter.TestConnectionAsync(Settings, cancellation.Token));

        //Assert
        // Not a "timed out" result: nobody is waiting for one.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task TestConnection_cleans_control_and_bidi_characters_out_of_the_server_name()
    {
        //Arrange
        // A right-to-left override, a bell, a line break with indentation and a zero-width space.
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """{"Version":"12.1.0\u0007","ProductName":"Jellyfin Server","ServerName":"Home‮gnp.exe\u0007\r\n  media​"}""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Jellyfin 12.1.0 (Homegnp.exe media).", result.Message);
    }

    [Fact]
    public async Task TestConnection_caps_a_long_server_name()
    {
        //Arrange
        var serverName = new string('n', 150);
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            $$"""{"Version":"12.1.0","ServerName":"{{serverName}}"}""", HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal($"Connected to Jellyfin 12.1.0 ({new string('n', 99)}…).", result.Message);
    }

    [Theory]
    [InlineData("")]
    // A zero-width space splits the key, and the cleaning joins it back together.
    [InlineData("\u200B")]
    public async Task TestConnection_masks_a_key_the_server_name_echoes_across_its_length_cap(string split)
    {
        //Arrange
        // The key starts 90 characters in, so the 100-character cap falls inside it.
        var serverName = new string('n', 90) + ApiKey[..16] + split + ApiKey[16..];
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            $$"""{"Version":"12.1.0","ServerName":"{{serverName}}"}""", HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal($"Connected to Jellyfin 12.1.0 ({new string('n', 90)}***).", result.Message);
        // Not 8 characters of the key in a row anywhere, the shortest value masking knows as a secret.
        Assert.All(
            Enumerable.Range(0, ApiKey.Length - 7),
            start => Assert.DoesNotContain(ApiKey.Substring(start, 8), result.Message, StringComparison.Ordinal));
    }

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

    // What SocketsHttpHandler itself throws when its ConnectTimeout runs out: the runtime's own
    // exception rather than a copy built by hand, so the test follows .NET if the shape changes.
    private static async Task<Exception> ConnectTimeoutExceptionAsync(CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(50),
            // A connection nothing ever accepts: the attempt waits until the connect timeout ends it.
            ConnectCallback = async (_, connectCancellation) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, connectCancellation);
                return Stream.Null;
            },
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://jellyfin.test:8096/System/Info");
        return (await Record.ExceptionAsync(() => invoker.SendAsync(request, cancellationToken)))!;
    }
}
