using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-7 — the real adapters behind the API, with only the network faked (each named client's primary
// handler) and the answers the services really send (VendorSamples, MAM-6): the request each makes with
// the stored credentials, and the sentence the dialog gets back. MAM-33 adds TMDb, MAM-30 Transmission. The adapters'
// every case is in Infrastructure.Tests; these pin the wiring from a saved row, or a draft, to the wire.
public sealed class IntegrationsRealAdaptersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string JellyfinAddress = "http://jellyfin.test:8096";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string TorznabAddress = "http://prowlarr.test:9696/1/api";
    private const string TorznabApiKey = "prowlarr-api-key-0001";
    private const string QBittorrentAddress = "http://qbittorrent.test:8080";
    private const string QBittorrentUserName = "admin";
    private const string QBittorrentPassword = "correct-horse-battery";
    private const string SessionId = "Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g";
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string TmdbApiKey = FakeTmdbServer.Token;
    private const string TransmissionAddress = FakeTransmissionServer.DefaultBaseUrl;
    private const string TransmissionRpcAddress = TransmissionAddress + FakeTransmissionServer.RpcPath;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsRealAdaptersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Each real type with the product name its sentences use.</summary>
    public static TheoryData<string, string> Types => new()
    {
        { "jellyfin", "Jellyfin" },
        { "torznab", "Torznab" },
        { "qbittorrent", "qBittorrent" },
        { "transmission", "Transmission" },
        { "tmdb", "TMDb" },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Test_of_jellyfin_reads_system_info_with_the_key_in_the_media_browser_header()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("jellyfin");
        _factory.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json; charset=utf-8");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Jellyfin 12.1.0 (VahaC media)."),
            await ReadTestResponseAsync(response, cancellationToken));
        var request = Assert.Single(_factory.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{JellyfinAddress}/System/Info", request.Uri.AbsoluteUri);
        // The scheme Jellyfin 12 reads by default; never api_key= in a URI a proxy logs, and never the
        // legacy header.
        Assert.Equal([$"MediaBrowser Token=\"{JellyfinApiKey}\""], request.HeaderValues("Authorization"));
        Assert.Empty(request.HeaderValues("X-Emby-Token"));
        Assert.DoesNotContain(JellyfinApiKey, request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_of_jellyfin_with_a_refused_key_is_authentication_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("jellyfin");
        _factory.JellyfinHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.AuthenticationFailed,
                "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here."),
            await ReadTestResponseAsync(response, cancellationToken));
        Assert.Single(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task Test_of_torznab_reads_the_caps_of_the_feed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("torznab");
        _factory.TorznabHttp.Handler = (_, _) => VendorResponses.Sample("Torznab/prowlarr-toloka-caps.xml", "application/rss+xml; charset=utf-8");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Prowlarr over Torznab; movie search by title, TV search by title, season and episode."),
            await ReadTestResponseAsync(response, cancellationToken));
        var request = Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        // Prowlarr's per-indexer address ends in /api, so it is used as is (ADR 0004).
        Assert.Equal($"{TorznabAddress}?t=caps&apikey={TorznabApiKey}", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Test_of_torznab_with_an_error_that_echoes_the_key_answers_it_masked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("torznab");
        _factory.TorznabHttp.Handler = (_, _) => VendorResponses.Text(
            $"""<?xml version="1.0" encoding="UTF-8"?><error code="100" description="Invalid API key {TorznabApiKey}" />""",
            HttpStatusCode.Unauthorized,
            "application/xml");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(TorznabApiKey, body, StringComparison.Ordinal);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (error 100: Invalid API key ***). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations."),
            await ReadTestResponseAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Test_of_qbittorrent_signs_in_and_reads_the_version_with_the_session_cookie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("qbittorrent");
        _factory.QBittorrentHttp.Handler = (request, _) => request.RequestUri!.AbsolutePath switch
        {
            SignInPath => VendorResponses.WithSetCookie(
                new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; HttpOnly; SameSite=Strict; path=/"),
            VersionPath => VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to qBittorrent v5.2.3."),
            await ReadTestResponseAsync(response, cancellationToken));
        Assert.Collection(
            _factory.QBittorrentHttp.Requests,
            signIn =>
            {
                Assert.Equal(HttpMethod.Post, signIn.Method);
                Assert.Equal(QBittorrentAddress + SignInPath, signIn.Uri.AbsoluteUri);
                // The stored password, decrypted, in a form body — never in the URL.
                Assert.Equal($"username={QBittorrentUserName}&password={QBittorrentPassword}", signIn.Body);
                Assert.Equal(["application/x-www-form-urlencoded"], signIn.HeaderValues("Content-Type"));
                Assert.Empty(signIn.HeaderValues("Referer"));
                Assert.Empty(signIn.HeaderValues("Origin"));
            },
            version =>
            {
                Assert.Equal(HttpMethod.Get, version.Method);
                Assert.Equal(QBittorrentAddress + VersionPath, version.Uri.AbsoluteUri);
                Assert.Equal([$"QBT_SID_8080={SessionId}"], version.HeaderValues("Cookie"));
            });
    }

    [Fact]
    public async Task Test_of_qbittorrent_without_credentials_reads_the_version_without_signing_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("qbittorrent", "Downloads", QBittorrentAddress);
        _factory.QBittorrentHttp.Handler = (_, _) => VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.Succeeded,
                "Connected to qBittorrent v5.2.3 without signing in: it lets this app's address skip authentication."),
            await ReadTestResponseAsync(response, cancellationToken));
        var request = Assert.Single(_factory.QBittorrentHttp.Requests);
        Assert.Equal(QBittorrentAddress + VersionPath, request.Uri.AbsoluteUri);
        Assert.Empty(request.HeaderValues("Cookie"));
    }

    [Fact]
    public async Task Test_of_qbittorrent_from_a_banned_address_is_refused_after_one_sign_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("qbittorrent");
        _factory.QBittorrentHttp.Handler = (_, _) => VendorResponses.Text("Your IP address has been banned.", HttpStatusCode.Forbidden);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.Refused,
                "qBittorrent has banned this app's address after too many failed sign-ins. Wait for the ban to end (qBittorrent's Web UI options set how long), then try again."),
            await ReadTestResponseAsync(response, cancellationToken));
        // Exactly one attempt: a repeated sign-in counts towards the ban.
        var request = Assert.Single(_factory.QBittorrentHttp.Requests);
        Assert.Equal(QBittorrentAddress + SignInPath, request.Uri.AbsoluteUri);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Test_of_a_service_that_answers_503_is_a_service_error_after_one_request(string adapterType, string displayName)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync(adapterType);
        // Left unarmed: every recording handler answers 503 until a test says otherwise.

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.ServiceError, $"{displayName} answered 503 Service Unavailable. Its own log says why."),
            await ReadTestResponseAsync(response, cancellationToken));
        // No retry (house rule 8): a test is one attempt.
        Assert.Single(HttpOf(adapterType).Requests);
    }

    [Theory]
    [InlineData("jellyfin")]
    [InlineData("torznab")]
    [InlineData("qbittorrent")]
    [InlineData("transmission")]
    [InlineData("tmdb")]
    public async Task Test_answered_with_a_redirect_is_an_unexpected_response_after_one_request(string adapterType)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync(adapterType);
        HttpOf(adapterType).Handler = (_, _) => VendorResponses.Redirect("https://sso.test/sign-in?return=%2F&token=leaked-token-0001");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The target's query is left out: it can hold a credential.
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.UnexpectedResponse,
                "The address answered with a redirect to https://sso.test/sign-in. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else."),
            await ReadTestResponseAsync(response, cancellationToken));
        // Not followed: the credentials went to the saved address only.
        var request = Assert.Single(HttpOf(adapterType).Requests);
        Assert.StartsWith(integration.BaseUrl, request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_of_transmission_shakes_hands_then_reads_session_get_with_the_stored_credentials()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("transmission");
        var transmission = new FakeTransmissionServer();
        transmission.Arm(_factory.TransmissionHttp);
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{FakeTransmissionServer.DefaultUserName}:{FakeTransmissionServer.DefaultPassword}"));

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Transmission 4.1.3."),
            await ReadTestResponseAsync(response, cancellationToken));
        // The 409 hands out the session id, and the same session-get goes once more with it — with the stored password,
        // decrypted, in the Basic header only.
        Assert.Collection(
            _factory.TransmissionHttp.Requests,
            handshake =>
            {
                Assert.Equal((HttpMethod.Post, TransmissionRpcAddress, "session-get"), (handshake.Method, handshake.Uri.AbsoluteUri, FakeTransmissionServer.Describe(handshake)));
                Assert.Equal([$"Basic {basic}"], handshake.HeaderValues("Authorization"));
                Assert.Empty(handshake.HeaderValues(FakeTransmissionServer.SessionIdHeader));
            },
            read =>
            {
                Assert.Equal((HttpMethod.Post, TransmissionRpcAddress, "session-get"), (read.Method, read.Uri.AbsoluteUri, FakeTransmissionServer.Describe(read)));
                Assert.Equal([transmission.SessionId], read.HeaderValues(FakeTransmissionServer.SessionIdHeader));
                Assert.DoesNotContain(FakeTransmissionServer.DefaultPassword, read.Body, StringComparison.Ordinal);
            });
        Assert.Empty(transmission.Writes);
    }

    [Fact]
    public async Task Test_of_transmission_older_than_4_0_says_it_needs_4_0_or_newer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("transmission");
        new FakeTransmissionServer { Version = FakeTransmissionVersion.V3_00 }.Arm(_factory.TransmissionHttp);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.UnsupportedVersion,
                "This is Transmission 3.00; the app needs Transmission 4.0 or newer. Update Transmission, then test again."),
            await ReadTestResponseAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Test_of_transmission_with_a_refused_password_is_authentication_failed_after_one_request()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("transmission");
        new FakeTransmissionServer { Password = "another-password" }.Arm(_factory.TransmissionHttp);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.AuthenticationFailed, "Transmission refused the user name or password."),
            await ReadTestResponseAsync(response, cancellationToken));
        // One request: with its anti-brute-force setting on, each refusal counts towards locking Transmission's RPC.
        Assert.Single(_factory.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task Test_of_transmission_with_its_temporary_folder_on_says_how_to_turn_it_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("transmission");
        new FakeTransmissionServer { IncompleteDirEnabled = true }.Arm(_factory.TransmissionHttp);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.Refused,
                "Transmission keeps unfinished downloads in its temporary folder “/downloads/incomplete”, where the app can't follow them. In Transmission's web interface open Edit preferences, Torrents, untick “Use temporary folder”, then test again."),
            await ReadTestResponseAsync(response, cancellationToken));
    }

    [Fact]
    public async Task TestDraft_of_transmission_at_its_web_interfaces_address_reaches_the_rpc_and_saves_nothing()
    {
        //Arrange
        // The address copied from the browser's address bar, and the draft's own credentials.
        var cancellationToken = TestContext.Current.CancellationToken;
        var transmission = new FakeTransmissionServer { UserName = "owner", Password = "draft-transmission-password" };
        transmission.Arm(_factory.TransmissionHttp);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(
            null,
            "transmission",
            $"{TransmissionAddress}/transmission/web/",
            UserName: "owner",
            Password: new SecretValueUpsert(SecretUpsertMode.Set, "draft-transmission-password")));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Transmission 4.1.3."),
            await ReadTestResponseAsync(response, cancellationToken));
        Assert.All(_factory.TransmissionHttp.Requests, request => Assert.Equal(TransmissionRpcAddress, request.Uri.AbsoluteUri));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Test_of_tmdb_reads_authentication_with_the_token_in_the_bearer_header()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await SeedAsync("tmdb");
        FakeTmdbServer.Arm(_factory.TmdbHttp);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to TMDb."),
            await ReadTestResponseAsync(response, cancellationToken));
        var request = Assert.Single(_factory.TmdbHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/authentication", request.Uri.AbsoluteUri);
        // The stored token, decrypted, in the header only: never v3's api_key= in a URI a proxy logs.
        Assert.Equal([$"Bearer {TmdbApiKey}"], request.HeaderValues("Authorization"));
        Assert.DoesNotContain(TmdbApiKey, request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestDraft_of_tmdb_with_a_wrong_token_is_authentication_failed_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        FakeTmdbServer.Arm(_factory.TmdbHttp);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(
            null,
            "tmdb",
            $"{FakeTmdbServer.BaseUrl}/3/",
            ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "draft-tmdb-token-0001")));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.AuthenticationFailed,
                "TMDb refused the key (401 Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API, not the shorter API key."),
            await ReadTestResponseAsync(response, cancellationToken));
        // The address ends in the API version already, so it is used as is.
        var request = Assert.Single(_factory.TmdbHttp.Requests);
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/authentication", request.Uri.AbsoluteUri);
        Assert.Equal(["Bearer draft-tmdb-token-0001"], request.HeaderValues("Authorization"));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task TestDraft_of_torznab_sends_the_draft_key_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.TorznabHttp.Handler = (_, _) => VendorResponses.Sample("Torznab/prowlarr-toloka-caps.xml", "application/xml");

        //Act
        // Jackett's copied feed address: its last segment isn't api, so /api is appended.
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(
            null,
            "torznab",
            "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/",
            ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "draft-torznab-key-01")));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Prowlarr over Torznab; movie search by title, TV search by title, season and episode."),
            await ReadTestResponseAsync(response, cancellationToken));
        var request = Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Equal(
            "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/api?t=caps&apikey=draft-torznab-key-01",
            request.Uri.AbsoluteUri);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // An integration of the type, with the credentials the type takes.
    private Task<IntegrationConnectionEntity> SeedAsync(string adapterType) => adapterType switch
    {
        "jellyfin" => _integrations.Create("jellyfin", "Living room", JellyfinAddress, apiKey: JellyfinApiKey),
        "torznab" => _integrations.Create("torznab", "Toloka", TorznabAddress, apiKey: TorznabApiKey),
        "qbittorrent" => _integrations.Create(
            "qbittorrent", "Downloads", QBittorrentAddress, userName: QBittorrentUserName, password: QBittorrentPassword),
        "transmission" => _integrations.Create(
            "transmission", "Transmission", TransmissionAddress,
            userName: FakeTransmissionServer.DefaultUserName, password: FakeTransmissionServer.DefaultPassword),
        "tmdb" => _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: TmdbApiKey),
        _ => throw new ArgumentOutOfRangeException(nameof(adapterType), adapterType, "Not a real adapter type."),
    };

    private RecordingHttpHandler HttpOf(string adapterType) => adapterType switch
    {
        "jellyfin" => _factory.JellyfinHttp,
        "torznab" => _factory.TorznabHttp,
        "qbittorrent" => _factory.QBittorrentHttp,
        "transmission" => _factory.TransmissionHttp,
        "tmdb" => _factory.TmdbHttp,
        _ => throw new ArgumentOutOfRangeException(nameof(adapterType), adapterType, "Not a real adapter type."),
    };

    private static async Task<ConnectionTestResponse?> ReadTestResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<ConnectionTestResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
}
