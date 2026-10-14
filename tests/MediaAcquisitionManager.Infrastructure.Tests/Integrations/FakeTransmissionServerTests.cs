using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — the fake Transmission every Transmission test stands on answers as the recorded 4.1.3 and 4.0.6 did
// (VendorSamples/Transmission, compared field by field with numbers as written — all but torrent-stop.json, a write the
// fake refuses on purpose), and as their sources say where nothing was recorded: the checks in rpc-server.cc's order (403 lock → 403 address → 401 → 301 → 421 → 409 → 404/405), the four
// methods it serves, every other method a recorded write it never acts on, and torrent-add's refusals before anything is
// added. The adapter's own tests (TransmissionAdapter*Tests) are where the app's reading of these answers is proved.
public sealed class FakeTransmissionServerTests : IDisposable
{
    private const string RpcAddress = FakeTransmissionServer.DefaultBaseUrl + FakeTransmissionServer.RpcPath;
    private const string SessionIdHeader = FakeTransmissionServer.SessionIdHeader;

    private readonly RecordingHttpHandler _handler = new();
    private readonly FakeTransmissionServer _server = new();
    private readonly HttpClient _client;

    public FakeTransmissionServerTests()
    {
        _server.Arm(_handler);
        _client = new HttpClient(_handler);
    }

    public void Dispose() => _client.Dispose();

    // ── the checks before the body is read ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4.1.3", TransmissionSamples.HandshakeResponseFile)]
    [InlineData("4.0.6", TransmissionSamples.Handshake406ResponseFile)]
    public async Task Request_without_a_session_id_answers_the_recorded_409_with_the_servers_own_id(string version, string recordedFile)
    {
        //Arrange
        _server.Version = VersionOf(version);
        using var recorded = VendorResponses.Recorded(recordedFile);
        var recordedPage = await recorded.Content.ReadAsStringAsync(Ct);

        //Act
        using var response = await PostAsync(Envelope("session-get", Fields("version"), 1), sessionId: null);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Matches("^[0-9A-Za-z]{48}$", _server.SessionId);
        Assert.Equal(recordedPage.Replace(TransmissionSamples.RedactedSessionId, _server.SessionId, StringComparison.Ordinal), await response.Content.ReadAsStringAsync(Ct));
        // The recorded Content-Length is the page's with a real 48-character id in it.
        Assert.Equal(HeadersOf(recorded, _server.SessionId), HeadersOf(response));
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task Request_with_a_wrong_password_answers_the_recorded_401_and_is_not_read()
    {
        //Arrange
        using var recorded = VendorResponses.Recorded(TransmissionSamples.WrongPasswordResponseFile);
        var recordedPage = await recorded.Content.ReadAsStringAsync(Ct);

        //Act
        using var response = await PostAsync(Envelope("session-get", Fields("version"), 1), _server.SessionId, Basic(FakeTransmissionServer.DefaultUserName, "wrong"));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(recordedPage, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HeadersOf(recorded), HeadersOf(response));
        Assert.Empty(_server.Calls);
    }

    [Theory]
    [InlineData("/")]
    [InlineData(FakeTransmissionServer.RpcPath)]
    public async Task Missing_credentials_answer_401_before_the_path_and_session_are_looked_at(string path)
    {
        //Arrange

        //Act
        using var response = await SendAsync(HttpMethod.Post, FakeTransmissionServer.DefaultBaseUrl + path, Envelope("session-get"), sessionId: null, authorization: null);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["Basic realm=\"Transmission\""], response.Headers.NonValidated["WWW-Authenticate"]);
        Assert.Empty(_server.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic d3Jvbmc6d3Jvbmc=")]
    [InlineData("Bearer anything")]
    public async Task Without_a_password_any_credentials_or_none_pass(string? authorization)
    {
        //Arrange
        _server.Password = null;

        //Act
        using var response = await PostAsync(Envelope("session-get", Fields("version"), 1), _server.SessionId, authorization);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("success", (string?)(await JsonOf(response))["result"]);
    }

    [Fact]
    public async Task A_password_may_hold_a_colon_as_the_user_name_ends_at_the_first()
    {
        //Arrange
        _server.Password = "pass:word";

        //Act
        using var response = await PostAsync(Envelope("session-get", Fields("version"), 1), _server.SessionId, Basic(FakeTransmissionServer.DefaultUserName, "pass:word"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anti_brute_force_locks_the_rpc_once_its_threshold_of_wrong_credentials_came_even_for_the_right_ones()
    {
        //Arrange
        _server.AntiBruteForceThreshold = 2;
        var wrong = Basic(FakeTransmissionServer.DefaultUserName, "wrong");

        //Act
        using var first = await PostAsync(Envelope("session-get"), _server.SessionId, wrong);
        using var second = await PostAsync(Envelope("session-get"), _server.SessionId, wrong);
        using var right = await PostAsync(Envelope("session-get"), _server.SessionId);

        //Assert
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], [first.StatusCode, second.StatusCode, right.StatusCode]);
        Assert.Equal("<h1>403: Forbidden</h1>", await right.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2, _server.LoginAttempts);
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task Right_credentials_set_the_login_attempts_back_to_0()
    {
        //Arrange
        _server.AntiBruteForceThreshold = 2;
        var wrong = Basic(FakeTransmissionServer.DefaultUserName, "wrong");

        //Act
        using var first = await PostAsync(Envelope("session-get"), _server.SessionId, wrong);
        using var right = await PostAsync(Envelope("session-get"), _server.SessionId);
        using var second = await PostAsync(Envelope("session-get"), _server.SessionId, wrong);

        //Assert
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.Unauthorized], [first.StatusCode, right.StatusCode, second.StatusCode]);
        Assert.Equal(1, _server.LoginAttempts);
    }

    [Theory]
    [InlineData("4.1.3", true)]
    [InlineData("4.0.6", false)]
    public async Task A_client_address_off_the_whitelist_answers_403_before_its_credentials_are_looked_at(string version, bool clickjackingHeaders)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.AddressAllowed = false;

        //Act
        using var response = await PostAsync(Envelope("session-get"), _server.SessionId, Basic(FakeTransmissionServer.DefaultUserName, "wrong"));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("<h1>403: Forbidden</h1>", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("text/html; charset=ISO-8859-1", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(clickjackingHeaders, response.Headers.Contains("X-Frame-Options"));
        // 4.0 sends its CORS header only once the address passed.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(0, _server.LoginAttempts);
    }

    [Fact]
    public async Task Options_answers_200_before_the_credentials_are_looked_at()
    {
        //Arrange

        //Act
        using var response = await SendAsync(HttpMethod.Options, RpcAddress, body: null, sessionId: null, authorization: null);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<h1>200: OK</h1>", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/transmission")]
    [InlineData("/transmission/web")]
    [InlineData("/rpc")]
    [InlineData("/Transmission/rpc")]
    public async Task A_path_outside_the_rpc_url_is_moved_to_the_web_interface_before_the_host_name_and_session_are_checked(string path)
    {
        //Arrange
        _server.Password = null;
        _server.HostWhitelist = [];

        //Act
        using var response = await SendAsync(HttpMethod.Post, FakeTransmissionServer.DefaultBaseUrl + path, Envelope("session-get"), sessionId: null, authorization: null);

        //Assert
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(["/transmission/web/"], response.Headers.NonValidated["Location"]);
        Assert.Equal("<h1>301: Moved Permanently</h1>", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task The_web_interface_answers_its_page_before_the_host_name_and_session_are_checked()
    {
        //Arrange
        _server.Password = null;
        _server.HostWhitelist = [];

        //Act
        using var response = await SendAsync(HttpMethod.Get, FakeTransmissionServer.DefaultBaseUrl + FakeTransmissionServer.WebPath, body: null, sessionId: null, authorization: null);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(FakeTransmissionServer.WebInterfacePage, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("4.1.3", "'rpc_host_whitelist' and 'rpc_host_whitelist_enabled'")]
    [InlineData("4.0.6", "'rpc-host-whitelist' and 'rpc-host-whitelist-enabled'")]
    public async Task A_host_name_off_the_whitelist_without_a_password_answers_421_worded_by_version(string version, string settings)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.Password = null;
        _server.HostWhitelist = ["*.example.org"];

        //Act
        using var response = await PostAsync(Envelope("session-get"), sessionId: null, authorization: null);

        //Assert
        // Never recorded (MAM-29 always had a password): the page as rpc-server.cc spells it (4.1.3 :600-613, 4.0.6 :487-499).
        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.Equal(
            "<h1>421: Misdirected Request</h1><p>Transmission received your request, but the hostname was unrecognized.</p>"
            + "<p>To fix this, choose one of the following options:<ul><li>Enable password authentication, then any hostname is allowed.</li>"
            + "<li>Add the hostname you want to use to the whitelist in settings.</li></ul></p>"
            + $"<p>If you're editing settings.json, see the {settings} entries.</p>"
            + "<p>This requirement has been added to help prevent <a href=\"https://en.wikipedia.org/wiki/DNS_rebinding\">DNS Rebinding</a> attacks.</p>",
            await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("text/html; charset=ISO-8859-1", response.Content.Headers.ContentType!.ToString());
        Assert.Empty(_server.Calls);
    }

    [Theory]
    [InlineData("http://192.168.1.10:9091")]
    [InlineData("http://localhost:9091")]
    [InlineData("http://[::1]:9091")]
    [InlineData("http://transmission.test:9091")]
    public async Task Without_a_password_an_ip_address_localhost_or_a_whitelisted_name_passes_the_host_check(string baseUrl)
    {
        //Arrange
        var server = new FakeTransmissionServer(baseUrl) { Password = null, HostWhitelist = ["*.test"] };
        server.Arm(_handler);

        //Act
        using var response = await SendAsync(HttpMethod.Post, baseUrl + FakeTransmissionServer.RpcPath, Envelope("session-get"), sessionId: null, authorization: null);

        //Assert
        // On to the session check: the host passed.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task With_a_password_any_host_name_passes_as_recorded()
    {
        //Arrange
        _server.HostWhitelist = [];
        using var recorded = VendorResponses.Recorded(TransmissionSamples.WrongHostAllowedResponseFile);
        var recordedBody = await recorded.Content.ReadAsStringAsync(Ct);

        //Act
        using var response = await SendAsync(
            HttpMethod.Post, RpcAddress, Envelope("session-get", Fields("version"), 4), _server.SessionId, RightCredentials, TransmissionSamples.NotWhitelistedHost);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(recordedBody, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HeadersOf(recorded), HeadersOf(response));
    }

    [Fact]
    public async Task A_missing_or_stale_session_id_answers_409_before_the_path_or_method_is_looked_at()
    {
        //Arrange
        var stale = _server.SessionId;
        _server.RotateSession();

        //Act
        using var get = await SendAsync(HttpMethod.Get, RpcAddress, body: null, sessionId: null, RightCredentials);
        using var elsewhere = await SendAsync(HttpMethod.Post, FakeTransmissionServer.DefaultBaseUrl + "/transmission/other", Envelope("session-get"), stale, RightCredentials);

        //Assert
        Assert.Equal([HttpStatusCode.Conflict, HttpStatusCode.Conflict], [get.StatusCode, elsewhere.StatusCode]);
        Assert.NotEqual(stale, _server.SessionId);
        Assert.Equal([_server.SessionId], elsewhere.Headers.NonValidated[SessionIdHeader]);
    }

    [Fact]
    public async Task Another_path_under_the_rpc_url_answers_404_naming_it()
    {
        //Arrange

        //Act
        using var response = await SendAsync(HttpMethod.Post, FakeTransmissionServer.DefaultBaseUrl + "/transmission/other?x=1", Envelope("session-get"), _server.SessionId, RightCredentials);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("<h1>404: Not Found</h1>/transmission/other?x=1", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_method_other_than_post_to_the_rpc_answers_405()
    {
        //Arrange

        //Act
        using var response = await SendAsync(HttpMethod.Get, RpcAddress, body: null, _server.SessionId, RightCredentials);

        //Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("<h1>405: Method Not Allowed</h1>", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task RotateSession_answers_the_kept_id_the_recorded_409_with_the_new_one_and_leaves_the_add_unread()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");
        var stale = _server.SessionId;
        _server.RotateSession();
        using var recorded = VendorResponses.Recorded(TransmissionSamples.TorrentAddStaleSessionResponseFile);
        var recordedPage = await recorded.Content.ReadAsStringAsync(Ct);

        //Act
        using var add = await PostAsync(Envelope("torrent-add", AddArguments(torrent), 20), stale);
        var get = await CallTextAsync("torrent-get", new JsonObject { ["ids"] = Strings(HashOf(torrent)), ["fields"] = Strings([.. TransmissionSamples.SmallFields]) }, 21);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        Assert.Equal(recordedPage.Replace(TransmissionSamples.RedactedSessionId, _server.SessionId, StringComparison.Ordinal), await add.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_server.AddRequests);
        Assert.Empty(_server.Torrents);
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentGetAfterStaleSessionFile)), Canonical(get));
    }

    [Fact]
    public async Task Unreachable_throws_a_connection_error_before_any_answer()
    {
        //Arrange
        _server.Unreachable = true;

        //Act
        var failure = await Record.ExceptionAsync(() => PostAsync(Envelope("session-get"), _server.SessionId));

        //Assert
        Assert.Equal(HttpRequestError.ConnectionError, Assert.IsType<HttpRequestException>(failure).HttpRequestError);
    }

    [Fact]
    public async Task Intercept_answers_before_every_check_and_sees_the_call_unrecorded()
    {
        //Arrange
        _server.Intercept = (_, call) => call?.Method == "torrent-add" ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null;

        //Act
        using var add = await PostAsync(Envelope("torrent-add", new JsonObject { ["filename"] = TransmissionSamples.Magnet }), sessionId: null, authorization: null);
        using var get = await PostAsync(Envelope("session-get"), sessionId: null);

        //Assert
        Assert.Equal([HttpStatusCode.BadGateway, HttpStatusCode.Conflict], [add.StatusCode, get.StatusCode]);
        Assert.Empty(_server.Calls);
        Assert.Empty(_server.AddRequests);
    }

    [Fact]
    public async Task Arm_with_several_servers_answers_each_at_its_own_address_a_proxys_base_path_included_and_503_elsewhere()
    {
        //Arrange
        var proxied = new FakeTransmissionServer("http://proxy.test/torrents") { Version = FakeTransmissionVersion.V4_0_6 };
        FakeTransmissionServer.Arm(_handler, _server, proxied);

        //Act
        var direct = JsonNode.Parse(await CallTextAsync("session-get", Fields("version")))!;
        using var throughProxy = await SendAsync(
            HttpMethod.Post, "http://proxy.test/torrents" + FakeTransmissionServer.RpcPath, Envelope("session-get", Fields("version")), proxied.SessionId, RightCredentials);
        using var nowhere = await SendAsync(HttpMethod.Post, "http://elsewhere.test:9091" + FakeTransmissionServer.RpcPath, Envelope("session-get"), _server.SessionId, RightCredentials);

        //Assert
        Assert.Equal("4.1.3 (838877323f)", (string?)direct["arguments"]!["version"]);
        Assert.Equal("4.0.6 (38c164933e)", (string?)(await JsonOf(throughProxy))["arguments"]!["version"]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, nowhere.StatusCode);
    }

    // ── the envelope ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(",\"tag\":7", "7")]
    [InlineData(",\"tag\":\"seven\"", null)]
    [InlineData(",\"tag\":7.5", null)]
    [InlineData("", null)]
    public async Task Every_answer_echoes_an_integer_tag_and_no_other(string tag, string? echoed)
    {
        //Arrange
        var body = "{\"method\":\"session-get\",\"arguments\":{\"fields\":[\"version\"]}" + tag + "}";

        //Act
        using var response = await PostAsync(body, _server.SessionId);

        //Assert
        Assert.Equal(echoed, (await JsonOf(response))["tag"]?.ToJsonString());
    }

    [Theory]
    [InlineData("4.1.3", "torrent-stop", "no method name")]
    [InlineData("4.0.6", "torrent-stop", "method name not recognized")]
    [InlineData("4.1.3", "torrent-remove", "no method name")]
    [InlineData("4.0.6", "torrent-set-location", "method name not recognized")]
    [InlineData("4.1.3", "session-set", "no method name")]
    public async Task A_method_the_fake_does_not_serve_is_a_recorded_write_answered_with_the_versions_sentence_and_changes_nothing(
        string version, string method, string sentence)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());
        var before = _server.Torrents;

        //Act
        var answer = await CallTextAsync(method, new JsonObject { ["ids"] = Strings(TransmissionSamples.BigBuckBunnyHash) }, 29);

        //Assert
        Assert.Equal($"{{\"arguments\":{{}},\"result\":\"{sentence}\",\"tag\":29}}", answer);
        Assert.Equal([method], _server.Writes.Select(call => call.Method));
        Assert.Equal(before, _server.Torrents);
    }

    [Fact]
    public async Task TorrentStop_is_refused_where_the_recorded_one_succeeded_so_a_send_of_it_fails_loudly()
    {
        //Arrange
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());
        var recorded = TransmissionSamples.Read(TransmissionSamples.TorrentStopFile);

        //Act
        var answer = await CallTextAsync("torrent-stop", new JsonObject { ["ids"] = Strings(TransmissionSamples.BigBuckBunnyHash) }, 29);

        //Assert
        // The one recorded answer the fake departs from, on purpose: real 4.1.3 stopped the torrent.
        Assert.Equal("success", (string?)JsonNode.Parse(recorded)!["result"]);
        Assert.Equal(Canonical(recorded.Replace("\"success\"", "\"no method name\"", StringComparison.Ordinal)), Canonical(answer));
        Assert.Equal(6, Assert.Single(_server.Torrents).Status);
    }

    [Theory]
    [InlineData("{\"arguments\":{},\"tag\":3}")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task A_body_that_names_no_method_answers_no_method_name(string body)
    {
        //Arrange

        //Act
        using var response = await PostAsync(body, _server.SessionId);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no method name", (string?)(await JsonOf(response))["result"]);
    }

    [Fact]
    public async Task RpcIntercept_answers_a_call_the_server_read_with_its_tag_added_and_changes_nothing()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");
        _server.RpcIntercept = call => call.Method == "torrent-add" ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "No space left on device" } : null;

        //Act
        var answer = await CallTextAsync("torrent-add", AddArguments(torrent), 5);

        //Assert
        Assert.Equal("{\"arguments\":{},\"result\":\"No space left on device\",\"tag\":5}", answer);
        Assert.Empty(_server.Torrents);
        Assert.Single(_server.AddRequests);
    }

    // ── session-get ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4.1.3", TransmissionSamples.SessionGetFile)]
    [InlineData("4.0.6", TransmissionSamples.SessionGet406File)]
    public async Task SessionGet_answers_the_recorded_version_and_settings(string version, string recordedFile)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.DownloadDir = TransmissionSamples.SampleDownloadDir;
        _server.IncompleteDir = TransmissionSamples.SampleIncompleteDir;

        //Act
        var answer = await CallTextAsync("session-get", Fields([.. TransmissionSamples.SessionFields]), 2);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(recordedFile)), Canonical(answer));
    }

    [Fact]
    public async Task SessionGet_without_fields_answers_every_field_the_fake_knows_the_session_id_among_them()
    {
        //Arrange
        _server.IncompleteDirEnabled = true;

        //Act
        var arguments = JsonNode.Parse(await CallTextAsync("session-get"))!["arguments"]!.AsObject();

        //Assert
        Assert.Equal(
            ["download-dir", "incomplete-dir", "incomplete-dir-enabled", "rename-partial-files", "rpc-version", "rpc-version-minimum",
                "rpc-version-semver", "session-id", "start-added-torrents", "version"],
            arguments.Select(argument => argument.Key));
        Assert.Equal(_server.SessionId, (string?)arguments["session-id"]);
        Assert.Equal((FakeTransmissionServer.DefaultDownloadDir, true), ((string?)arguments["download-dir"], (bool)arguments["incomplete-dir-enabled"]!));
    }

    [Fact]
    public async Task SessionGet_of_3_00_answers_rpc_version_16_and_no_semver()
    {
        //Arrange
        _server.Version = FakeTransmissionVersion.V3_00;

        //Act
        var arguments = JsonNode.Parse(await CallTextAsync("session-get", Fields("version", "rpc-version", "rpc-version-semver")))!["arguments"]!.AsObject();

        //Assert
        Assert.Equal(["rpc-version", "version"], arguments.Select(argument => argument.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("3.00 (bb6b5a062e)", 16), ((string?)arguments["version"], (int)arguments["rpc-version"]!));
    }

    // ── free-space ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreeSpace_of_a_folder_it_sees_answers_as_recorded()
    {
        //Arrange
        _server.AddFolder(TransmissionSamples.SampleDownloadDir, TransmissionSamples.SampleFreeBytes, TransmissionSamples.SampleTotalBytes);

        //Act
        var answer = await CallTextAsync("free-space", new JsonObject { ["path"] = TransmissionSamples.SampleDownloadDir }, 3);
        var withSlash = JsonNode.Parse(await CallTextAsync("free-space", new JsonObject { ["path"] = TransmissionSamples.SampleDownloadDir + "/" }))!;

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.FreeSpaceDownloadDirFile)), Canonical(answer));
        Assert.Equal(("success", "/scratch/"), ((string?)withSlash["result"], (string?)withSlash["arguments"]!["path"]));
    }

    [Theory]
    [InlineData("4.1.3", "totalSize")]
    [InlineData("4.0.6", "total_size")]
    public async Task FreeSpace_of_a_folder_it_does_not_see_answers_as_recorded_the_total_spelled_by_version(string version, string total)
    {
        //Arrange
        _server.Version = VersionOf(version);

        //Act
        var answer = await CallTextAsync("free-space", new JsonObject { ["path"] = TransmissionSamples.MissingFolder }, 6);

        //Assert
        var recorded = TransmissionSamples.Read(TransmissionSamples.FreeSpaceMissingFolderFile).Replace("\"totalSize\"", $"\"{total}\"", StringComparison.Ordinal);
        Assert.Equal(Canonical(recorded), Canonical(answer));
    }

    [Fact]
    public async Task RemoveFolder_makes_free_space_answer_as_for_a_missing_folder()
    {
        //Arrange
        var removed = _server.RemoveFolder(FakeTransmissionServer.DefaultDownloadDir);

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("free-space", new JsonObject { ["path"] = FakeTransmissionServer.DefaultDownloadDir }))!;

        //Assert
        Assert.True(removed);
        Assert.Equal(("No such file or directory", -1L), ((string?)answer["result"], (long)answer["arguments"]!["size-bytes"]!));
    }

    [Theory]
    [InlineData(null, "directory path argument is missing")]
    [InlineData("downloads", "directory path is not absolute")]
    [InlineData("", "directory path is not absolute")]
    public async Task FreeSpace_refuses_a_missing_or_relative_path(string? path, string sentence)
    {
        //Arrange
        var arguments = path is null ? new JsonObject() : new JsonObject { ["path"] = path };

        //Act
        var answer = await CallTextAsync("free-space", arguments);

        //Assert
        Assert.Equal($"{{\"arguments\":{{}},\"result\":\"{sentence}\"}}", answer);
    }

    // ── torrent-get ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4.1.3", TransmissionSamples.TorrentGetDownloadingFile, "downloading", 13)]
    [InlineData("4.1.3", TransmissionSamples.TorrentGetSeedingFile, "seeding", 27)]
    [InlineData("4.1.3", TransmissionSamples.TorrentGetStoppedFile, "stopped", 31)]
    [InlineData("4.0.6", TransmissionSamples.TorrentGetVerifying406File, "verifying", 7)]
    public async Task TorrentGet_answers_the_recorded_torrent_numbers_and_pieces_written_by_version(string version, string recordedFile, string state, int tag)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.AddTorrent(state switch
        {
            "downloading" => TransmissionSamples.BigBuckBunnyDownloading(),
            "seeding" => TransmissionSamples.BigBuckBunnySeeding(),
            "stopped" => TransmissionSamples.BigBuckBunnyStopped(),
            _ => TransmissionSamples.BigBuckBunnyVerifying(),
        });

        //Act
        var answer = await CallTextAsync("torrent-get", FullFieldsOf(TransmissionSamples.BigBuckBunnyHash), tag);

        //Assert
        // Raw numbers compared: 4.1.3 writes 1.0 where 4.0.6 writes 1, and only 4.1.3 lists begin_piece and end_piece.
        Assert.Equal(Canonical(TransmissionSamples.Read(recordedFile)), Canonical(answer));
    }

    [Fact]
    public async Task TorrentGet_answers_the_recorded_magnet_without_metadata()
    {
        //Arrange
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());
        _server.AddTorrent(TransmissionSamples.MagnetWithoutMetadata());

        //Act
        var answer = await CallTextAsync("torrent-get", FullFieldsOf(TransmissionSamples.MagnetHash), 18);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentGetMagnetNoMetadataFile)), Canonical(answer));
    }

    [Theory]
    [InlineData(TransmissionSamples.TorrentGetAllFile, null, 8)]
    [InlineData(TransmissionSamples.TorrentGetUnknownHashFile, "0123456789abcdef0123456789abcdef01234567", 3)]
    public async Task TorrentGet_of_no_torrent_answers_as_recorded(string recordedFile, string? id, int tag)
    {
        //Arrange
        var arguments = new JsonObject { ["fields"] = Strings([.. TransmissionSamples.SmallFields]) };
        if (id is not null)
            arguments["ids"] = Strings(id);

        //Act
        var answer = await CallTextAsync("torrent-get", arguments, tag);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(recordedFile)), Canonical(answer));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("[1]")]
    [InlineData("\"DD8255ECDC7CA55FB0BBF81323D87062DB1F6D1C\"")]
    [InlineData("[\"3wbfl3g4pssv7mf37ajshwdqmlnr63i4\"]")]
    [InlineData("[\"magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=x\"]")]
    public async Task TorrentGet_finds_a_torrent_by_its_id_its_hash_in_hex_or_base32_in_any_letter_case_or_a_magnet_naming_it(string ids)
    {
        //Arrange
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());

        //Act
        var torrents = await TorrentsAsync(JsonNode.Parse(ids), "hashString");

        //Assert
        Assert.Equal([TransmissionSamples.BigBuckBunnyHash], torrents.Select(torrent => (string?)torrent!["hashString"]));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("\"recently-active\"")]
    [InlineData("\"Big Buck Bunny\"")]
    [InlineData("[\"magnet:?xt=urn:btmh:1220aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"]")]
    public async Task TorrentGet_of_ids_naming_no_torrent_it_has_lists_none(string ids)
    {
        //Arrange
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());

        //Act
        var torrents = await TorrentsAsync(JsonNode.Parse(ids), "hashString");

        //Assert
        Assert.Empty(torrents);
    }

    [Theory]
    [InlineData("4.1.3", null, "{\"arguments\":{},\"result\":\"no fields specified\"}")]
    [InlineData("4.1.3", "\"id\"", "{\"arguments\":{},\"result\":\"no fields specified\"}")]
    [InlineData("4.0.6", null, "{\"arguments\":{\"torrents\":[]},\"result\":\"no fields specified\"}")]
    [InlineData("4.0.6", "\"id\"", "{\"arguments\":{\"torrents\":[]},\"result\":\"no fields specified\"}")]
    public async Task TorrentGet_without_a_fields_list_answers_no_fields_specified_on_both_versions(string version, string? fields, string expected)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());
        var arguments = fields is null ? new JsonObject() : new JsonObject { ["fields"] = JsonNode.Parse(fields) };

        //Act
        var answer = await CallTextAsync("torrent-get", arguments);

        //Assert
        // 4.0.6 refuses after it put the empty torrents list in its answer (rpcimpl.cc:905-930).
        Assert.Equal(expected, answer);
    }

    [Theory]
    [InlineData("4.1.3", "[]", "{\"arguments\":{},\"result\":\"no fields specified\"}")]
    [InlineData("4.1.3", "[\"noSuchField\",5]", "{\"arguments\":{},\"result\":\"no fields specified\"}")]
    [InlineData("4.0.6", "[]", "{\"arguments\":{\"torrents\":[{}]},\"result\":\"success\"}")]
    [InlineData("4.0.6", "[\"noSuchField\",5]", "{\"arguments\":{\"torrents\":[{}]},\"result\":\"success\"}")]
    public async Task TorrentGet_of_a_fields_list_naming_no_field_it_knows_is_refused_on_4_1_and_lists_empty_torrents_on_4_0(
        string version, string fields, string expected)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());

        //Act
        var answer = await CallTextAsync("torrent-get", new JsonObject { ["fields"] = JsonNode.Parse(fields) });

        //Assert
        Assert.Equal(expected, answer);
    }

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task TorrentGet_lists_the_fields_asked_for_in_their_order_leaving_out_one_it_does_not_know(string version)
    {
        //Arrange
        _server.Version = VersionOf(version);
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());

        //Act
        var torrents = await TorrentsAsync(null, "name", "noSuchField", "id");

        //Assert
        Assert.Equal(["name", "id"], torrents.Single()!.AsObject().Select(field => field.Key));
    }

    // ── torrent-add ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4.1.3", TransmissionSamples.TorrentAddAddedFile, 9)]
    [InlineData("4.0.6", TransmissionSamples.TorrentAddAdded406File, 5)]
    public async Task TorrentAdd_of_a_torrent_file_adds_it_under_its_v1_hash_as_recorded(string version, string recordedFile, int tag)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = TestTorrent.V1(TransmissionSamples.BigBuckBunnyName, [(["Big Buck Bunny.mp4"], TestTorrent.Bytes(40_000, 1)), (["poster.jpg"], TestTorrent.Bytes(3_000, 2))]);
        var hash = HashOf(torrent);

        //Act
        var answer = await CallTextAsync("torrent-add", AddArguments(torrent, TransmissionSamples.SampleDownloadDir, TransmissionSamples.Label), tag);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(recordedFile).Replace(TransmissionSamples.BigBuckBunnyHash, hash, StringComparison.Ordinal)), Canonical(answer));
        var added = Assert.Single(_server.Torrents);
        Assert.Equal(
            (1, hash, TransmissionSamples.BigBuckBunnyName, TransmissionSamples.SampleDownloadDir, 1.0, 0.0, 43_000L, 43_000L),
            (added.Id, added.HashString, added.Name, added.DownloadDir, added.MetadataPercentComplete, added.PercentDone, added.LeftUntilDone, added.TotalSize));
        Assert.Equal([TransmissionSamples.Label], added.Labels);
        Assert.Equal(
            [new FakeTransmissionFile("Big Buck Bunny/Big Buck Bunny.mp4", 40_000, 0), new FakeTransmissionFile("Big Buck Bunny/poster.jpg", 3_000, 0)],
            added.Files);
        Assert.Equal(TestTorrent.PieceLength, added.PieceLength);
    }

    [Fact]
    public async Task TorrentAdd_of_a_magnet_adds_it_without_metadata_as_recorded()
    {
        //Arrange
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        //Act
        var answer = await CallTextAsync(
            "torrent-add",
            new JsonObject
            {
                ["filename"] = TransmissionSamples.Magnet,
                ["download-dir"] = TransmissionSamples.SampleDownloadDir,
                ["labels"] = Strings(TransmissionSamples.Label),
                ["paused"] = false,
            },
            17);
        var addedDate = _server.Torrents[1].AddedDate;
        // The add's own date is today's; the recorded one goes in for the comparison.
        _server.UpdateTorrent(TransmissionSamples.MagnetHash, torrent => torrent with { AddedDate = TransmissionSamples.MagnetAddedDate });
        var listed = await CallTextAsync("torrent-get", FullFieldsOf(TransmissionSamples.MagnetHash), 18);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentAddMagnetAddedFile)), Canonical(answer));
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentGetMagnetNoMetadataFile)), Canonical(listed));
        Assert.InRange(addedDate, before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task TorrentAdd_of_a_torrent_already_there_answers_the_recorded_duplicate_and_changes_nothing()
    {
        //Arrange
        var held = _server.AddTorrent(TransmissionSamples.BigBuckBunnyStopped());

        //Act
        var answer = await CallTextAsync(
            "torrent-add",
            new JsonObject
            {
                ["filename"] = "magnet:?xt=urn:btih:DD8255ECDC7CA55FB0BBF81323D87062DB1F6D1C",
                ["download-dir"] = "/downloads/movies",
                ["labels"] = Strings("Movies"),
                ["paused"] = false,
            },
            9);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentAddDuplicateFile)), Canonical(answer));
        Assert.Equal([held], _server.Torrents);
    }

    [Fact]
    public async Task TorrentAdd_of_a_torrent_file_already_added_keeps_its_first_folder_labels_and_state()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");
        await CallTextAsync("torrent-add", AddArguments(torrent, "/downloads/films", "Films", paused: true));

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("torrent-add", AddArguments(torrent, "/downloads/movies", "Movies")))!;

        //Assert
        Assert.Equal((HashOf(torrent), 1), ((string?)answer["arguments"]!["torrent-duplicate"]!["hashString"], (int)answer["arguments"]!["torrent-duplicate"]!["id"]!));
        var kept = Assert.Single(_server.Torrents);
        Assert.Equal(("/downloads/films", 0), (kept.DownloadDir, kept.Status));
        Assert.Equal(["Films"], kept.Labels);
    }

    [Fact]
    public async Task TorrentAdd_of_bytes_that_are_no_torrent_answers_as_recorded()
    {
        //Arrange

        //Act
        var answer = await CallTextAsync(
            "torrent-add",
            new JsonObject
            {
                ["metainfo"] = Convert.ToBase64String("not a torrent"u8),
                ["download-dir"] = TransmissionSamples.SampleDownloadDir,
                ["labels"] = Strings(TransmissionSamples.Label),
                ["paused"] = false,
            },
            14);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentAddCorruptMetainfoFile)), Canonical(answer));
        Assert.Empty(_server.Torrents);
    }

    [Theory]
    [InlineData("v2-only torrent")]
    [InlineData("btmh-only magnet")]
    [InlineData("magnet without a topic")]
    [InlineData("text that is no hash")]
    [InlineData("metainfo that is no base64")]
    [InlineData("torrent of one empty file")]
    public async Task TorrentAdd_of_what_names_no_torrent_with_v1_metadata_answers_unrecognized_info_and_adds_nothing(string payload)
    {
        //Arrange
        var arguments = payload switch
        {
            "v2-only torrent" => new JsonObject { ["metainfo"] = Convert.ToBase64String(TestTorrent.V2("Sintel", [(["Sintel.mkv"], TestTorrent.Bytes(40_000, 3))])) },
            "btmh-only magnet" => new JsonObject { ["filename"] = "magnet:?xt=urn:btmh:1220" + new string('a', 64) + "&dn=Sintel" },
            "magnet without a topic" => new JsonObject { ["filename"] = "magnet:?dn=Sintel" },
            "text that is no hash" => new JsonObject { ["filename"] = "Sintel" },
            "metainfo that is no base64" => new JsonObject { ["metainfo"] = "not base64!" },
            _ => new JsonObject { ["metainfo"] = Convert.ToBase64String(TestTorrent.V1("empty.mkv", Array.Empty<byte>())) },
        };

        //Act
        var answer = await CallTextAsync("torrent-add", arguments, 14);

        //Assert
        Assert.Equal("{\"arguments\":{},\"result\":\"unrecognized info\",\"tag\":14}", answer);
        Assert.Empty(_server.Torrents);
        Assert.Single(_server.AddRequests);
    }

    [Fact]
    public async Task TorrentAdd_into_a_relative_folder_answers_as_recorded()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        var answer = await CallTextAsync("torrent-add", AddArguments(torrent, "relative/path", TransmissionSamples.Label), 16);

        //Assert
        Assert.Equal(Canonical(TransmissionSamples.Read(TransmissionSamples.TorrentAddRelativeDownloadDirFile)), Canonical(answer));
        Assert.Empty(_server.Torrents);
    }

    [Theory]
    [InlineData("", "download directory path is not absolute")]
    [InlineData("C:\\Downloads", "download directory path is not absolute")]
    public async Task TorrentAdd_into_an_empty_or_windows_folder_is_refused_by_a_linux_transmission(string folder, string sentence)
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        var answer = await CallTextAsync("torrent-add", AddArguments(torrent, folder, TransmissionSamples.Label));

        //Assert
        Assert.Equal($"{{\"arguments\":{{}},\"result\":\"{sentence}\"}}", answer);
        Assert.Empty(_server.Torrents);
    }

    [Theory]
    [InlineData("", "labels cannot be empty")]
    [InlineData(" \t ", "labels cannot be empty")]
    [InlineData("Movies, TV", "labels cannot contain comma (,) character")]
    [InlineData(" Movies,", "labels cannot contain comma (,) character")]
    public async Task TorrentAdd_with_a_blank_label_or_one_with_a_comma_is_refused_before_anything_is_added(string label, string sentence)
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        var answer = await CallTextAsync("torrent-add", AddArguments(torrent, FakeTransmissionServer.DefaultDownloadDir, label));

        //Assert
        Assert.Equal($"{{\"arguments\":{{}},\"result\":\"{sentence}\"}}", answer);
        Assert.Empty(_server.Torrents);
    }

    [Theory]
    [InlineData("relative", "Movies, TV", "download directory path is not absolute")]
    [InlineData("/downloads", "Movies, TV", "labels cannot contain comma (,) character")]
    [InlineData("/downloads", "Movies", "unrecognized info")]
    public async Task TorrentAdd_checks_the_folder_then_the_labels_then_the_torrent(string folder, string label, string sentence)
    {
        //Arrange
        var arguments = new JsonObject { ["metainfo"] = Convert.ToBase64String("not a torrent"u8), ["download-dir"] = folder, ["labels"] = Strings(label) };

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("torrent-add", arguments))!;

        //Assert
        Assert.Equal(sentence, (string?)answer["result"]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public async Task TorrentAdd_without_a_filename_or_metainfo_is_refused(string? filename, string? metainfo)
    {
        //Arrange
        var arguments = new JsonObject { ["download-dir"] = FakeTransmissionServer.DefaultDownloadDir };
        if (filename is not null)
            arguments["filename"] = filename;
        if (metainfo is not null)
            arguments["metainfo"] = metainfo;

        //Act
        var answer = await CallTextAsync("torrent-add", arguments);

        //Assert
        Assert.Equal("{\"arguments\":{},\"result\":\"no filename or metainfo specified\"}", answer);
    }

    [Fact]
    public async Task TorrentAdd_trims_the_labels_keeps_each_once_with_its_letter_case_and_ignores_what_is_no_text()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");
        var arguments = AddArguments(torrent);
        arguments["labels"] = new JsonArray(" Movies ", "Movies", 5, "movies", null);

        //Act
        await CallTextAsync("torrent-add", arguments);

        //Assert
        Assert.Equal(["Movies", "movies"], Assert.Single(_server.Torrents).Labels);
    }

    [Theory]
    [InlineData(false, true, 4)]
    [InlineData(true, true, 0)]
    [InlineData(null, true, 4)]
    [InlineData(null, false, 0)]
    [InlineData(false, false, 4)]
    public async Task TorrentAdd_starts_the_torrent_unless_paused_or_without_paused_the_session_does_not_start_added_ones(
        bool? paused, bool startAddedTorrents, int status)
    {
        //Arrange
        _server.StartAddedTorrents = startAddedTorrents;
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        await CallTextAsync("torrent-add", AddArguments(torrent, paused: paused));

        //Assert
        Assert.Equal(status, Assert.Single(_server.Torrents).Status);
    }

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task TorrentAdd_lists_a_started_torrent_downloading_at_once_skipping_the_verify_transmission_runs_first(string version)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        await CallTextAsync("torrent-add", AddArguments(torrent));
        var listed = (await TorrentsAsync(null, "status", "isStalled")).Single()!;

        //Assert
        // On purpose: real 4.0.6, read straight after its add, was still verifying (status 2, isStalled true); a test that
        // wants that state plants it.
        Assert.Equal("{\"status\":4,\"isStalled\":false}", listed.ToJsonString());
    }

    [Fact]
    public async Task TorrentAdd_without_a_download_dir_lands_in_the_sessions()
    {
        //Arrange
        _server.DownloadDir = "/data/complete";
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        await CallTextAsync("torrent-add", new JsonObject { ["metainfo"] = Convert.ToBase64String(torrent) });

        //Assert
        Assert.Equal("/data/complete", Assert.Single(_server.Torrents).DownloadDir);
    }

    [Fact]
    public async Task TorrentAdd_names_a_single_file_torrents_file_alone_and_a_multi_file_torrents_under_its_folder_even_one()
    {
        //Arrange
        var single = SingleFileTorrent("Sintel.mkv");
        var pack = TestTorrent.V1("Pack", [(["Season 1", "e01.mkv"], TestTorrent.Bytes(20_000, 4)), (["e02.mkv"], TestTorrent.Bytes(20_000, 5))]);
        var one = TestTorrent.V1("Folder", [(["only.mkv"], TestTorrent.Bytes(20_000, 6))]);

        //Act
        foreach (var torrent in new[] { single, pack, one })
            await CallTextAsync("torrent-add", AddArguments(torrent));

        //Assert
        Assert.Equal(
            [["Sintel.mkv"], ["Pack/Season 1/e01.mkv", "Pack/e02.mkv"], ["Folder/only.mkv"]],
            _server.Torrents.Select(torrent => torrent.Files.Select(file => file.Name).ToArray()));
    }

    [Theory]
    [InlineData("4.0.6", "Movie_ Part 2/_CON.mkv|Movie_ Part 2/sub/a_.srt|Movie_ Part 2/x")]
    [InlineData("4.1.3", "Movie: Part 2 /CON.mkv|Movie: Part 2 /sub/a?.srt|Movie: Part 2 /_../x.")]
    public async Task TorrentAdd_cleans_the_files_names_as_the_version_does_and_keeps_the_torrents_name(string version, string names)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = TestTorrent.V1(
            "Movie: Part 2 ",
            [(["CON.mkv"], TestTorrent.Bytes(20_000, 7)), (["sub", "a?.srt"], TestTorrent.Bytes(100, 8)), (["..", "x."], TestTorrent.Bytes(100, 9))]);

        //Act
        await CallTextAsync("torrent-add", AddArguments(torrent));

        //Assert
        var added = Assert.Single(_server.Torrents);
        Assert.Equal("Movie: Part 2 ", added.Name);
        Assert.Equal(names.Split('|'), added.Files.Select(file => file.Name));
    }

    [Theory]
    [InlineData("4.0.6", "Pack/a.mkv")]
    [InlineData("4.1.3", "Pack/a.mkv|Pack/empty.txt")]
    public async Task TorrentAdd_on_4_0_leaves_an_empty_file_out_which_4_1_lists(string version, string names)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = TestTorrent.V1("Pack", [(["a.mkv"], TestTorrent.Bytes(20_000, 10)), (["empty.txt"], [])]);

        //Act
        await CallTextAsync("torrent-add", AddArguments(torrent));

        //Assert
        Assert.Equal(names.Split('|'), Assert.Single(_server.Torrents).Files.Select(file => file.Name));
    }

    [Theory]
    [InlineData("4.1.3", "unrecognized info")]
    [InlineData("4.0.6", "success")]
    public async Task TorrentAdd_on_4_1_refuses_two_files_named_alike_which_4_0_takes(string version, string result)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = TestTorrent.V1("Pack", [(["a.mkv"], TestTorrent.Bytes(20_000, 11)), (["a.mkv"], TestTorrent.Bytes(20_000, 12))]);

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("torrent-add", AddArguments(torrent)))!;

        //Assert
        Assert.Equal(result, (string?)answer["result"]);
    }

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task TorrentAdd_of_a_hybrid_torrent_adds_it_under_its_v1_hash_with_its_v1_files(string version)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = HybridTorrent("Pack", ("a.mkv", TestTorrent.Bytes(TestTorrent.PieceLength, 15)), ("b.nfo", TestTorrent.Bytes(1_000, 16)));
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("torrent-add", AddArguments(torrent)))!;

        //Assert
        Assert.NotNull(hashes.V2);
        Assert.Equal(hashes.V1, (string?)answer["arguments"]!["torrent-added"]!["hashString"]);
        var added = Assert.Single(_server.Torrents);
        Assert.Equal(hashes.V1, added.HashString);
        Assert.Equal(
            [new FakeTransmissionFile("Pack/a.mkv", TestTorrent.PieceLength, 0), new FakeTransmissionFile("Pack/b.nfo", 1_000, 0)],
            added.Files);
    }

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task TorrentAdd_lists_a_torrents_pad_files_as_ordinary_files(string version)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = TestTorrent.V1("Pack", [(["a.mkv"], TestTorrent.Bytes(20_000, 17)), (["b.nfo"], TestTorrent.Bytes(100, 18))], padFiles: true);

        //Act
        await CallTextAsync("torrent-add", AddArguments(torrent));

        //Assert
        // BEP 47's attr is ignored on both versions: the pad file a.mkv's last piece needs (16 384 - 3 616) is a file.
        Assert.Equal(
            [
                new FakeTransmissionFile("Pack/a.mkv", 20_000, 0),
                new FakeTransmissionFile("Pack/.pad/12768", 12_768, 0),
                new FakeTransmissionFile("Pack/b.nfo", 100, 0),
            ],
            Assert.Single(_server.Torrents).Files);
    }

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task TorrentAdd_takes_name_utf_8_and_path_utf_8_over_name_and_path(string version)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var content = TestTorrent.Bytes(20_000, 19);
        var file = TestTorrent.Dictionary(
            ("length", (long)content.Length),
            ("path", new List<object> { "Season 1", "Seriya 1.mkv" }),
            ("path.utf-8", new List<object> { "Серія 1.mkv" }));
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object> { file }),
            ("name", "Film"),
            ("name.utf-8", "Фільм"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", Pieces(content))))));

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("torrent-add", AddArguments(torrent)))!;

        //Assert
        var added = Assert.Single(_server.Torrents);
        Assert.Equal(("Фільм", "Фільм"), ((string?)answer["arguments"]!["torrent-added"]!["name"], added.Name));
        // The plain path is dropped whole, not merged.
        Assert.Equal(["Фільм/Серія 1.mkv"], added.Files.Select(entry => entry.Name));
    }

    [Theory]
    [InlineData("4.0.6", "...", "a.mkv")]
    [InlineData("4.0.6", " . ", "a.mkv")]
    [InlineData("4.1.3", "...", ".../a.mkv")]
    [InlineData("4.1.3", ".", "_./a.mkv")]
    public async Task TorrentAdd_puts_the_files_under_no_folder_when_4_0_cleans_the_torrents_name_to_nothing_where_4_1_keeps_a_folder(
        string version, string name, string path)
    {
        //Arrange
        _server.Version = VersionOf(version);
        var torrent = TestTorrent.V1(name, [(["a.mkv"], TestTorrent.Bytes(20_000, 20))]);

        //Act
        await CallTextAsync("torrent-add", AddArguments(torrent));

        //Assert
        var added = Assert.Single(_server.Torrents);
        Assert.Equal((name, path), (added.Name, Assert.Single(added.Files).Name));
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:3WBFL3G4PSSV7MF37AJSHWDQMLNR63I4&dn=Big%20Buck+Bunny%3A%20%D0%A4%D1%96%D0%BB%D1%8C%D0%BC", "Big Buck+Bunny: Фільм")]
    [InlineData("magnet:?xt=urn:btih:3wbfl3g4pssv7mf37ajshwdqmlnr63i4", TransmissionSamples.BigBuckBunnyHash)]
    [InlineData(" magnet:?dn=Big+Buck+Bunny&xt=urn:btih:DD8255ECDC7CA55FB0BBF81323D87062DB1F6D1C\n", "Big+Buck+Bunny")]
    public async Task TorrentAdd_of_a_magnet_names_it_by_its_hex_v1_hash_and_its_dn_percent_decoded_a_plus_kept_or_by_the_hash_without_one(
        string magnet, string name)
    {
        //Arrange

        //Act
        var answer = JsonNode.Parse(await CallTextAsync("torrent-add", new JsonObject { ["filename"] = magnet }))!;

        //Assert
        var added = answer["arguments"]!["torrent-added"]!;
        Assert.Equal((TransmissionSamples.BigBuckBunnyHash, name), ((string?)added["hashString"], (string?)added["name"]));
        Assert.Equal(name, Assert.Single(_server.Torrents).Name);
    }

    [Fact]
    public async Task Adds_of_one_torrent_at_once_add_it_once_and_answer_the_others_as_duplicates()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");
        var body = Envelope("torrent-add", AddArguments(torrent));

        //Act
        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var response = await PostAsync(body, _server.SessionId);
            return (await JsonOf(response))["arguments"]!.AsObject().Single().Key;
        }));

        //Assert
        Assert.Equal(1, answers.Count(answer => answer == "torrent-added"));
        Assert.Equal(7, answers.Count(answer => answer == "torrent-duplicate"));
        Assert.Single(_server.Torrents);
        Assert.Equal(8, _server.AddRequests.Count);
    }

    [Fact]
    public async Task Each_add_is_recorded_with_what_it_sent_and_only_adds_are_writes()
    {
        //Arrange
        var torrent = SingleFileTorrent("Sintel.mkv");

        //Act
        await CallTextAsync("session-get", Fields("version"));
        await CallTextAsync("torrent-add", AddArguments(torrent, "/downloads/movies", "Movies"));
        await CallTextAsync("torrent-add", new JsonObject { ["filename"] = TransmissionSamples.Magnet, ["paused"] = true });
        await CallTextAsync("torrent-get", new JsonObject { ["fields"] = Strings("id") });

        //Assert
        Assert.Equal(["session-get", "torrent-add", "torrent-add", "torrent-get"], _server.Calls.Select(call => call.Method));
        Assert.Equal(["torrent-add", "torrent-add"], _server.Writes.Select(call => call.Method));
        var (file, magnet) = (_server.AddRequests[0], _server.AddRequests[1]);
        Assert.Equal(["metainfo", "download-dir", "labels", "paused"], file.ArgumentNames);
        Assert.Equal(
            ((ReleasePayloadKind?)ReleasePayloadKind.TorrentFile, (string?)null, "/downloads/movies", (bool?)false),
            (file.Kind, file.Filename, file.DownloadDir, file.Paused));
        Assert.Equal(torrent, file.Metainfo);
        Assert.Equal(["Movies"], file.Labels);
        Assert.Equal(
            ((ReleasePayloadKind?)ReleasePayloadKind.Magnet, TransmissionSamples.Magnet, (byte[]?)null, (string?)null, (IReadOnlyList<string>?)null, (bool?)true),
            (magnet.Kind, magnet.Filename, magnet.Metainfo, magnet.DownloadDir, magnet.Labels, magnet.Paused));
        var call = _server.Calls[1];
        Assert.Equal((_server.SessionId, RightCredentials), (call.SessionId, call.Authorization));
    }

    // ── what a test does to a torrent ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_makes_a_torrent_whole_seeding_and_done_and_not_finished()
    {
        //Arrange
        var torrent = TestTorrent.V1("Pack", [(["a.mkv"], TestTorrent.Bytes(20_000, 13)), (["b.nfo"], TestTorrent.Bytes(100, 14))]);
        await CallTextAsync("torrent-add", AddArguments(torrent));

        //Act
        var completed = _server.Complete(HashOf(torrent));
        var listed = (await TorrentsAsync(null, "status", "percentDone", "leftUntilDone", "isFinished", "files")).Single()!;

        //Assert
        Assert.Equal("{\"status\":6,\"percentDone\":1.0,\"leftUntilDone\":0,\"isFinished\":false,\"files\":["
            + "{\"begin_piece\":0,\"bytesCompleted\":20000,\"end_piece\":2,\"length\":20000,\"name\":\"Pack/a.mkv\"},"
            + "{\"begin_piece\":1,\"bytesCompleted\":100,\"end_piece\":2,\"length\":100,\"name\":\"Pack/b.nfo\"}]}", listed.ToJsonString());
        Assert.True(completed.DoneDate >= completed.AddedDate && completed.DoneDate > 0);
    }

    [Fact]
    public async Task Complete_of_a_torrent_without_its_metadata_throws()
    {
        //Arrange
        await CallTextAsync("torrent-add", new JsonObject { ["filename"] = TransmissionSamples.Magnet });

        //Act
        var failure = Record.Exception(() => _server.Complete(TransmissionSamples.MagnetHash));

        //Assert
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public async Task GetMetadata_gives_a_magnets_torrent_its_name_files_and_sizes_over_the_wanted_ones()
    {
        //Arrange
        await CallTextAsync("torrent-add", new JsonObject { ["filename"] = TransmissionSamples.Magnet });

        //Act
        _server.GetMetadata(
            TransmissionSamples.MagnetHash,
            [new FakeTransmissionFile("Pack/a.mkv", 100, 25), new FakeTransmissionFile("Pack/b.nfo", 50, 0, Wanted: false)],
            "Pack");
        var listed = (await TorrentsAsync(null, "name", "metadataPercentComplete", "totalSize", "sizeWhenDone", "leftUntilDone", "percentDone", "fileStats")).Single()!;

        //Assert
        Assert.Equal("{\"name\":\"Pack\",\"metadataPercentComplete\":1.0,\"totalSize\":150,\"sizeWhenDone\":100,\"leftUntilDone\":75,\"percentDone\":0.25,"
            + "\"fileStats\":[{\"bytesCompleted\":25,\"priority\":0,\"wanted\":true},{\"bytesCompleted\":0,\"priority\":0,\"wanted\":false}]}", listed.ToJsonString());
    }

    [Fact]
    public async Task UpdateTorrent_and_RemoveTorrent_change_what_torrent_get_lists()
    {
        //Arrange
        _server.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());

        //Act
        _server.UpdateTorrent(TransmissionSamples.BigBuckBunnyHash.ToUpperInvariant(), torrent => torrent with { Error = 3, ErrorString = "No data found!" });
        var updated = (await TorrentsAsync(null, "error", "errorString")).Single()!.ToJsonString();
        var removed = _server.RemoveTorrent(TransmissionSamples.BigBuckBunnyHash);
        var removedAgain = _server.RemoveTorrent(TransmissionSamples.BigBuckBunnyHash);
        var after = await TorrentsAsync(null, "id");
        var missing = Record.Exception(() => _server.UpdateTorrent(TransmissionSamples.BigBuckBunnyHash, torrent => torrent));

        //Assert
        Assert.Equal("{\"error\":3,\"errorString\":\"No data found!\"}", updated);
        Assert.Equal((true, false), (removed, removedAgain));
        Assert.Empty(after);
        Assert.IsType<InvalidOperationException>(missing);
    }

    [Fact]
    public void AddTorrent_gives_each_torrent_the_next_id_and_refuses_one_it_has()
    {
        //Arrange
        var first = new TorrentInfoHashes("1111111111111111111111111111111111111111", null);
        var second = new TorrentInfoHashes("2222222222222222222222222222222222222222", "2222222222222222222222222222222222222222222222222222222222222222");

        //Act
        var one = _server.AddTorrent(first, files: [new FakeTransmissionFile("a.mkv", 10, 4)]);
        var two = _server.AddTorrent(second, "Hybrid", "/downloads/tv", ["TV"]);
        var again = Record.Exception(() => _server.AddTorrent(first));

        //Assert
        Assert.Equal((1, 4, 0L), (one.Id, one.Status, one.DoneDate));
        Assert.Equal((2, "2222222222222222222222222222222222222222", "Hybrid", "/downloads/tv", 6), (two.Id, two.HashString, two.Name, two.DownloadDir, two.Status));
        Assert.True(two.DoneDate > 0);
        Assert.IsType<InvalidOperationException>(again);
    }

    [Fact]
    public void FakeTransmissionTorrent_Of_a_torrent_without_a_v1_hash_throws()
    {
        //Arrange
        var v2Only = new TorrentInfoHashes(null, new string('a', 64));

        //Act
        var failure = Record.Exception(() => FakeTransmissionTorrent.Of(v2Only, "Sintel"));

        //Assert
        Assert.IsType<ArgumentException>(failure);
    }

    [Fact]
    public async Task Describe_names_each_recorded_requests_rpc_method_or_else_its_method_and_path()
    {
        //Arrange

        //Act
        using var handshake = await PostAsync(Envelope("session-get"), sessionId: null);
        await CallTextAsync("torrent-get", new JsonObject { ["fields"] = Strings("id") });
        using var web = await SendAsync(HttpMethod.Get, FakeTransmissionServer.DefaultBaseUrl + FakeTransmissionServer.WebPath, body: null, sessionId: null, RightCredentials);

        //Assert
        Assert.Equal(["session-get", "torrent-get", "GET /transmission/web/"], _handler.Requests.Select(FakeTransmissionServer.Describe));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string RightCredentials => Basic(FakeTransmissionServer.DefaultUserName, FakeTransmissionServer.DefaultPassword);

    private static FakeTransmissionVersion VersionOf(string version) => version switch
    {
        "4.1.3" => FakeTransmissionVersion.V4_1_3,
        "4.0.6" => FakeTransmissionVersion.V4_0_6,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "No such version preset."),
    };

    private static string Basic(string userName, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));

    private static byte[] SingleFileTorrent(string name) => TestTorrent.V1(name, TestTorrent.Bytes(40_000, 42));

    private static string HashOf(byte[] torrent) => TorrentInfoHashReader.FromTorrentFile(torrent).V1!;

    /// <summary>BEP 3's <c>pieces</c> of <paramref name="data"/>: the SHA-1 of each piece, the last as short as it is.</summary>
    private static byte[] Pieces(byte[] data) => [.. data.Chunk(TestTorrent.PieceLength).SelectMany(piece => SHA1.HashData(piece))];

    /// <summary>
    /// A hybrid torrent (BEP 52) of <paramref name="files"/> under a folder <paramref name="name"/>: v1 <c>files</c> and
    /// <c>pieces</c> beside the v2 <c>file tree</c> and <c>meta version</c> 2 in one info dictionary. A test gives every
    /// file but the last whole pieces, so no pad file is due, and none over one 16 KiB block, so each file's
    /// <c>pieces root</c> is the SHA-256 of its bytes and no piece layer is due.
    /// </summary>
    private static byte[] HybridTorrent(string name, params (string Name, byte[] Content)[] files)
    {
        var tree = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (fileName, content) in files)
            tree[fileName] = TestTorrent.Dictionary(("", TestTorrent.Dictionary(("length", (long)content.Length), ("pieces root", SHA256.HashData(content)))));

        return TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", tree),
            ("files", files.Select(file => (object)TestTorrent.Dictionary(("length", (long)file.Content.Length), ("path", new List<object> { file.Name }))).ToList()),
            ("meta version", 2L),
            ("name", name),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", Pieces([.. files.SelectMany(file => file.Content)]))))));
    }

    private static JsonArray Strings(params string[] values) => new([.. values.Select(value => (JsonNode)JsonValue.Create(value))]);

    private static JsonObject Fields(params string[] names) => new() { ["fields"] = Strings(names) };

    private static JsonObject FullFieldsOf(string hash) =>
        new() { ["ids"] = Strings(hash), ["fields"] = Strings([.. TransmissionSamples.FullFields]) };

    /// <summary>A torrent-add as the app sends one: the .torrent, the folder, the label, and whether it is paused.</summary>
    private static JsonObject AddArguments(byte[] torrent, string? folder = FakeTransmissionServer.DefaultDownloadDir, string? label = "Movies", bool? paused = false)
    {
        var arguments = new JsonObject { ["metainfo"] = Convert.ToBase64String(torrent) };
        if (folder is not null)
            arguments["download-dir"] = folder;
        if (label is not null)
            arguments["labels"] = Strings(label);
        if (paused is not null)
            arguments["paused"] = paused;
        return arguments;
    }

    private static string Envelope(string method, JsonObject? arguments = null, long? tag = null)
    {
        var envelope = new JsonObject { ["method"] = method, ["arguments"] = arguments ?? new JsonObject() };
        if (tag is not null)
            envelope["tag"] = tag;
        return envelope.ToJsonString();
    }

    private Task<HttpResponseMessage> PostAsync(string body, string? sessionId, string? authorization = "") =>
        SendAsync(HttpMethod.Post, RpcAddress, body, sessionId, authorization == "" ? RightCredentials : authorization);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? body, string? sessionId, string? authorization, string? host = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (sessionId is not null)
            request.Headers.TryAddWithoutValidation(SessionIdHeader, sessionId);
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (host is not null)
            request.Headers.Host = host;
        return await _client.SendAsync(request, Ct);
    }

    /// <summary>One call with the server's session id and the right credentials: the answer's body as sent.</summary>
    private async Task<string> CallTextAsync(string method, JsonObject? arguments = null, long? tag = null)
    {
        using var response = await PostAsync(Envelope(method, arguments, tag), _server.SessionId);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<JsonArray> TorrentsAsync(JsonNode? ids, params string[] fields)
    {
        var arguments = new JsonObject { ["fields"] = Strings(fields) };
        if (ids is not null)
            arguments["ids"] = ids;
        return JsonNode.Parse(await CallTextAsync("torrent-get", arguments))!["arguments"]!["torrents"]!.AsArray();
    }

    private static async Task<JsonObject> JsonOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();

    /// <summary>
    /// Every header but the connection's own (<c>Date</c>, <c>Connection</c>), one line each, sorted: a recording's with
    /// <paramref name="sessionId"/> where it has <c>REDACTED</c>.
    /// </summary>
    private static List<string> HeadersOf(HttpResponseMessage response, string? sessionId = null)
    {
        var length = response.Content.Headers.ContentLength;
        return
        [
            .. response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated)
                .Where(header => header.Key is not ("Date" or "Connection" or "Content-Length"))
                .SelectMany(header => header.Value.Select(value => $"{header.Key}: {value}"))
                .Append($"Content-Length: {length}")
                .Select(line => sessionId is null ? line : line.Replace(TransmissionSamples.RedactedSessionId, sessionId, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// <paramref name="json"/> written compactly with every object's keys sorted and each number exactly as written, so
    /// an indented recording and a compact answer compare equal only when they hold the same — <c>1</c> is not <c>1.0</c>.
    /// </summary>
    private static string Canonical(string json)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(JsonNode.Parse(json), writer);
        return Encoding.UTF8.GetString(stream.ToArray());

        static void Write(JsonNode? node, Utf8JsonWriter writer)
        {
            switch (node)
            {
                case JsonObject members:
                    writer.WriteStartObject();
                    foreach (var (name, value) in members.OrderBy(member => member.Key, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(name);
                        Write(value, writer);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonArray items:
                    writer.WriteStartArray();
                    foreach (var item in items)
                        Write(item, writer);
                    writer.WriteEndArray();
                    break;
                case null:
                    writer.WriteNullValue();
                    break;
                default:
                    node.WriteTo(writer);
                    break;
            }
        }
    }
}
