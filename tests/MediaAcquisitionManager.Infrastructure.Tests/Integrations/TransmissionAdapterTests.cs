using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — Transmission's connection test: the 409 handshake, the Basic credentials, one session-get of six fields, the
// version gate (4.0 or newer, D2), the temporary folder (D6), the address the owner types (D17), every refusal of
// Transmission's own in its sentence, and every secret masked. TransmissionAdapterSessionsTests has the kept session ids and
// the remembered refusals; the other TransmissionAdapter*Tests classes have each member of the download client contract.
public sealed class TransmissionAdapterTests : IDisposable
{
    private const string BaseUrl = FakeTransmissionServer.DefaultBaseUrl;
    private const string RpcAddress = BaseUrl + FakeTransmissionServer.RpcPath;
    private const string UserName = FakeTransmissionServer.DefaultUserName;
    private const string Password = FakeTransmissionServer.DefaultPassword;

    private const string NotTransmissionError = "The service at this address answered, but not the way Transmission does.";
    private const string SessionIdRefusedError = "Transmission refused the session id it had just sent.";

    private static readonly IntegrationConnectionSettings SignedIn = new(null, BaseUrl, UserName, Password, null);
    private static readonly IntegrationConnectionSettings Anonymous = new(null, BaseUrl, null, null, null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterTests() => _transmission.Arm(_harness.TransmissionHttp);

    private IIntegrationAdapter Adapter => _harness.Adapter(TransmissionAdapter.TypeId);

    /// <summary>A <c>session-get</c> answer's <c>arguments</c> that a version this app works with sends, but for what a row
    /// changes.</summary>
    private static JsonObject SessionArguments => new()
    {
        ["download-dir"] = "/downloads",
        ["incomplete-dir"] = "/downloads/incomplete",
        ["incomplete-dir-enabled"] = false,
        ["rpc-version"] = 19,
        ["rpc-version-semver"] = "6.0.1",
        ["version"] = "4.1.3 (838877323f)",
    };

    /// <summary>Bodies of a 200 to the RPC address that aren't Transmission's envelope.</summary>
    public static TheoryData<string> NotEnvelopes => new()
    {
        "",
        FakeTransmissionServer.WebInterfacePage,
        "[]",
        "{}",
        """{"arguments":{},"result":1}""",
        """{"result":"success"}""",
        """{"arguments":[],"result":"success"}""",
        // 4.1's answer to a body it can't parse: JSON-RPC, not the legacy envelope.
        """{"jsonrpc":"2.0","error":{"code":-32700,"message":"Parse error"},"id":null}""",
    };

    /// <summary>session-get answers whose version or folders aren't Transmission's: each row names the field it takes away
    /// or spoils.</summary>
    public static TheoryData<string> BrokenSessions => new()
    {
        "no version",
        "a blank version",
        "a version that isn't text",
        "no rpc version at all",
        "no download-dir",
        "no incomplete-dir-enabled",
        "incomplete-dir-enabled as text",
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TestConnection_on_4_1_3_shakes_hands_then_reads_six_session_fields_with_the_basic_credentials()
    {
        //Arrange
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Password}"));

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Transmission 4.1.3.", result.Message);
        Assert.Collection(
            _harness.TransmissionHttp.Requests,
            handshake =>
            {
                Assert.Equal(HttpMethod.Post, handshake.Method);
                Assert.Equal(RpcAddress, handshake.Uri.AbsoluteUri);
                Assert.Equal([$"Basic {basic}"], handshake.HeaderValues("Authorization"));
                Assert.Empty(handshake.HeaderValues(FakeTransmissionServer.SessionIdHeader));
            },
            read =>
            {
                Assert.Equal(RpcAddress, read.Uri.AbsoluteUri);
                Assert.Equal([$"Basic {basic}"], read.HeaderValues("Authorization"));
                Assert.Equal([_transmission.SessionId], read.HeaderValues(FakeTransmissionServer.SessionIdHeader));
                Assert.Equal(["application/json; charset=utf-8"], read.HeaderValues("Content-Type"));
            });
        var call = Assert.Single(_transmission.Calls);
        Assert.Equal("session-get", call.Method);
        Assert.Equal(["fields"], call.ArgumentNames);
        Assert.Equal(
            ["version", "rpc-version", "rpc-version-semver", "download-dir", "incomplete-dir", "incomplete-dir-enabled"],
            call.Arguments["fields"]!.AsArray().Select(field => (string)field!));
        // The resent request is the same one: the tag the first carried.
        Assert.Equal(1L, call.Tag);
    }

    [Fact]
    public async Task TestConnection_on_4_0_6_connects_with_its_short_version()
    {
        //Arrange
        _transmission.Version = FakeTransmissionVersion.V4_0_6;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Transmission 4.0.6.", result.Message);
    }

    [Fact]
    public async Task TestConnection_of_transmission_3_00_is_an_unsupported_version_that_names_it()
    {
        //Arrange
        _transmission.Version = FakeTransmissionVersion.V3_00;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal("This is Transmission 3.00; the app needs Transmission 4.0 or newer. Update Transmission, then test again.", result.Message);
    }

    [Theory]
    [InlineData("5.3.0", 17, true)]
    [InlineData("6.0.1", 19, true)]
    [InlineData("7.0.0", null, true)]
    [InlineData(null, 17, true)]
    [InlineData(null, 16, false)]
    // The semantic version decides when it is sent: a 5.2 RPC is 3.00's, whatever rpc-version says.
    [InlineData("5.2.9", 99, false)]
    // One this app can't read falls back to rpc-version.
    [InlineData("five", 17, true)]
    [InlineData("five", 16, false)]
    public async Task TestConnection_gates_on_the_rpc_semantic_version_else_the_rpc_version(string? semver, int? rpcVersion, bool connects)
    {
        //Arrange
        var arguments = SessionArguments;
        arguments["rpc-version-semver"] = semver;
        arguments["rpc-version"] = rpcVersion;
        arguments["version"] = "4.9.9 (0000000000)";
        AnswerSessionGet(arguments);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(connects ? IntegrationCallStatus.Succeeded : IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal(
            connects
                ? "Connected to Transmission 4.9.9."
                : "This is Transmission 4.9.9; the app needs Transmission 4.0 or newer. Update Transmission, then test again.",
            result.Message);
    }

    [Theory]
    [InlineData("4.1.3", "4.1.3")]
    [InlineData("  4.2.0-beta.1 (deadbeef00)", "4.2.0-beta.1")]
    [InlineData("4.1.3(838877323f)", "4.1.3")]
    [InlineData("4.1.3‮ (838877323f)", "4.1.3")]
    public async Task TestConnection_names_the_version_without_its_build_and_cleaned(string reported, string shown)
    {
        //Arrange
        var arguments = SessionArguments;
        arguments["version"] = reported;
        AnswerSessionGet(arguments);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal($"Connected to Transmission {shown}.", result.Message);
    }

    [Fact]
    public async Task TestConnection_without_a_user_name_sends_no_credentials_and_connects_to_a_transmission_without_a_password()
    {
        //Arrange
        _transmission.Password = null;

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Transmission 4.1.3.", result.Message);
        Assert.All(_harness.TransmissionHttp.Requests, request => Assert.Empty(request.HeaderValues("Authorization")));
    }

    [Fact]
    public async Task TestConnection_sends_a_password_with_a_colon_and_letters_of_any_script_as_utf_8()
    {
        //Arrange
        const string password = "p:ss wörd д";
        _transmission.Password = password;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn with { Password = password }, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [$"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{password}"))}"],
            _harness.TransmissionHttp.Requests[0].HeaderValues("Authorization"));
    }

    [Fact]
    public async Task TestConnection_with_a_wrong_password_is_authentication_failed_before_any_session()
    {
        //Arrange

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn with { Password = "wrong-horse-battery" }, TestContext.Current.CancellationToken);

        //Assert
        // Transmission checks the credentials before the session id: the 401 comes first, and nothing was read.
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("Transmission refused the user name or password.", result.Message);
        Assert.Single(_harness.TransmissionHttp.Requests);
        Assert.Empty(_transmission.Calls);
    }

    [Fact]
    public async Task TestConnection_with_the_recorded_401_is_authentication_failed()
    {
        //Arrange
        _harness.TransmissionHttp.Handler = (_, _) => VendorResponses.Recorded(TransmissionSamples.WrongPasswordResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("Transmission refused the user name or password.", result.Message);
    }

    [Fact]
    public async Task TestConnection_without_credentials_to_a_transmission_with_a_password_asks_for_them()
    {
        //Arrange

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("Transmission asks for a user name and password. Enter them under Integrations.", result.Message);
    }

    [Theory]
    [InlineData("whitelist")]
    [InlineData("anti-brute-force")]
    public async Task TestConnection_refused_with_403_says_why_it_may_be_and_how_to_fix_it(string cause)
    {
        //Arrange
        if (cause == "whitelist")
            _transmission.AddressAllowed = false;
        else
            _transmission.AntiBruteForceThreshold = 0;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        // The two answer the same bare page: one sentence names both.
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "Transmission refused this app (403 Forbidden): this app's address isn't on its RPC whitelist (WHITELIST in the linuxserver image, rpc-whitelist in its settings), or it locked its RPC after too many wrong passwords, which only a restart of Transmission ends.",
            result.Message);
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_by_a_host_name_transmission_without_a_password_does_not_take_is_refused_with_how_to_fix_it()
    {
        //Arrange
        _transmission.Password = null;
        _transmission.HostWhitelist = [];

        //Act
        var result = await Adapter.TestConnectionAsync(Anonymous, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "Transmission doesn't accept the host name in this address (421 Misdirected Request); it checks host names only while it has no password. Give Transmission a user name and password (USER and PASS in the linuxserver image), add the host name to its rpc-host-whitelist (HOST_WHITELIST), or use its IP address, then test again.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_of_an_address_under_which_transmission_has_no_rpc_is_a_404_saying_which_address_to_use()
    {
        //Arrange
        var settings = SignedIn with { BaseUrl = $"{BaseUrl}/transmission/x" };

        //Act
        var result = await Adapter.TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "Transmission's RPC wasn't found at this address (404 Not Found). Use the address of Transmission's web interface, such as http://192.168.1.10:9091.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_that_transmission_moves_to_its_web_interface_says_where_and_follows_nothing()
    {
        //Arrange
        // A reverse proxy's path Transmission doesn't know: everything outside its rpc-url moves to the web interface.
        var settings = SignedIn with { BaseUrl = $"{BaseUrl}/torrents" };

        //Act
        var result = await Adapter.TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://transmission.test:9091/transmission/web/. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Message);
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Theory]
    [InlineData("http://transmission.test:9091")]
    [InlineData("http://transmission.test:9091/transmission")]
    [InlineData("http://transmission.test:9091/transmission/web")]
    [InlineData("http://transmission.test:9091/transmission/rpc")]
    public async Task TestConnection_of_the_web_interfaces_address_as_typed_or_copied_reaches_the_rpc(string typed)
    {
        //Arrange

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn with { BaseUrl = typed }, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.All(_harness.TransmissionHttp.Requests, request => Assert.Equal(RpcAddress, request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task TestConnection_keeps_a_reverse_proxys_path_before_the_rpc()
    {
        //Arrange
        var proxied = new FakeTransmissionServer("http://proxy.test/tr");
        proxied.Arm(_harness.TransmissionHttp);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn with { BaseUrl = proxied.BaseUrl }, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.All(_harness.TransmissionHttp.Requests, request => Assert.Equal("http://proxy.test/tr/transmission/rpc", request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task TestConnection_with_the_temporary_folder_on_is_refused_with_how_to_turn_it_off()
    {
        //Arrange
        // D6 — the linuxserver image ships it on.
        _transmission.IncompleteDirEnabled = true;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "Transmission keeps unfinished downloads in its temporary folder “/downloads/incomplete”, where the app can't follow them. In Transmission's web interface open Edit preferences, Torrents, untick “Use temporary folder”, then test again.",
            result.Message);
    }

    [Theory]
    [MemberData(nameof(NotEnvelopes))]
    public async Task TestConnection_of_a_200_that_is_not_transmissions_envelope_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TransmissionHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTransmissionError, result.Message);
    }

    [Theory]
    [MemberData(nameof(BrokenSessions))]
    public async Task TestConnection_of_a_session_get_without_what_transmission_sends_is_an_unexpected_response(string broken)
    {
        //Arrange
        var arguments = SessionArguments;
        switch (broken)
        {
            case "no version":
                arguments.Remove("version");
                break;
            case "a blank version":
                arguments["version"] = "   ";
                break;
            case "a version that isn't text":
                arguments["version"] = 4.1;
                break;
            case "no rpc version at all":
                arguments.Remove("rpc-version");
                arguments.Remove("rpc-version-semver");
                break;
            case "no download-dir":
                arguments.Remove("download-dir");
                break;
            case "no incomplete-dir-enabled":
                arguments.Remove("incomplete-dir-enabled");
                break;
            default:
                arguments["incomplete-dir-enabled"] = "false";
                break;
        }

        AnswerSessionGet(arguments);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTransmissionError, result.Message);
    }

    [Fact]
    public async Task TestConnection_whose_session_get_transmission_refuses_quotes_its_result()
    {
        //Arrange
        _transmission.RpcIntercept = _ => new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "no method\nname" };

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Transmission refused the request: no method name.", result.Message);
    }

    [Fact]
    public async Task TestConnection_answered_409_twice_is_refused_and_sends_no_third_request()
    {
        //Arrange
        _transmission.Intercept = (_, _) =>
        {
            _transmission.RotateSession();
            return null;
        };

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(SessionIdRefusedError, result.Message);
        Assert.Equal(2, _harness.TransmissionHttp.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc\u0001def")]
    [InlineData("abc def")]
    [InlineData("идентифікатор")]
    public async Task TestConnection_answered_409_without_a_session_id_it_can_send_back_is_refused_and_sends_it_again_never(string? sessionId)
    {
        //Arrange
        _harness.TransmissionHttp.Handler = (_, _) =>
        {
            var conflict = VendorResponses.Text("<h1>409: Conflict</h1>", HttpStatusCode.Conflict, "text/html");
            if (sessionId is not null)
                conflict.Headers.TryAddWithoutValidation(FakeTransmissionServer.SessionIdHeader, sessionId);
            return conflict;
        };

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(SessionIdRefusedError, result.Message);
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_answered_409_with_a_session_id_too_long_to_be_one_is_refused()
    {
        //Arrange
        _harness.TransmissionHttp.Handler = (_, _) =>
        {
            var conflict = new HttpResponseMessage(HttpStatusCode.Conflict);
            conflict.Headers.TryAddWithoutValidation(FakeTransmissionServer.SessionIdHeader, new string('a', 257));
            return conflict;
        };

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(SessionIdRefusedError, result.Message);
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task TestConnection_of_the_recorded_4_1_3_handshake_takes_the_id_from_the_header()
    {
        //Arrange
        // The recorded page quotes the id too; the header is what counts.
        _harness.TransmissionHttp.Handler = (request, _) => request.Headers.Contains(FakeTransmissionServer.SessionIdHeader)
            ? VendorResponses.Sample(TransmissionSamples.SessionGetFile, "application/json; charset=UTF-8")
            : VendorResponses.Recorded(TransmissionSamples.HandshakeResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Transmission 4.1.3.", result.Message);
        Assert.Equal([TransmissionSamples.RedactedSessionId], _harness.TransmissionHttp.Requests[1].HeaderValues(FakeTransmissionServer.SessionIdHeader));
    }

    [Fact]
    public async Task TestConnection_of_the_recorded_4_0_6_answers_connects()
    {
        //Arrange
        _harness.TransmissionHttp.Handler = (request, _) => request.Headers.Contains(FakeTransmissionServer.SessionIdHeader)
            ? VendorResponses.Sample(TransmissionSamples.SessionGet406File, "application/json; charset=UTF-8")
            : VendorResponses.Recorded(TransmissionSamples.Handshake406ResponseFile);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Connected to Transmission 4.0.6.", result.Message);
    }

    [Fact]
    public async Task TestConnection_to_an_unreachable_address_names_transmission_and_no_exception_text()
    {
        //Arrange
        _transmission.Unreachable = true;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(
            "Couldn't reach Transmission at http://transmission.test:9091: nothing accepted the connection. Check the address and port, and that Transmission is running.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_answered_with_a_5xx_is_transmissions_own_failure()
    {
        //Arrange
        _harness.TransmissionHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("Transmission answered 503 Service Unavailable. Its own log says why.", result.Message);
    }

    [Fact]
    public async Task TestConnection_masks_the_password_the_basic_credentials_and_the_session_id_in_its_message()
    {
        //Arrange
        // A redirect whose path repeats all three, once the handshake handed out the id.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Password}"));
        _transmission.Intercept = (request, _) => request.Headers.Contains(FakeTransmissionServer.SessionIdHeader)
            ? VendorResponses.Redirect($"{BaseUrl}/x/{_transmission.SessionId}/{Password}/{Uri.EscapeDataString(basic)}/")
            : null;

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://transmission.test:9091/x/***/***/***/. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Message);
    }

    [Fact]
    public async Task TestConnection_masks_the_password_in_transmissions_own_result()
    {
        //Arrange
        _transmission.RpcIntercept = _ => new JsonObject { ["arguments"] = new JsonObject(), ["result"] = $"bad password {Password}" };

        //Act
        var result = await Adapter.TestConnectionAsync(SignedIn, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Transmission refused the request: bad password ***.", result.Message);
    }

    /// <summary>Has the server answer <c>session-get</c> with <paramref name="arguments"/> and <c>success</c>.</summary>
    private void AnswerSessionGet(JsonObject arguments) =>
        _transmission.RpcIntercept = call => call.Method == "session-get"
            ? new JsonObject { ["arguments"] = arguments.DeepClone(), ["result"] = "success" }
            : null;
}
