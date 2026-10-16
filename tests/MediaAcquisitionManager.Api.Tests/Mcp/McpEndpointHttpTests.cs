using System.Net;
using System.Reflection;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — the MCP endpoint as an HTTP endpoint (ADR 0040, Vasyl's decisions a, b): POST /mcp for personal access tokens
// alone — no token or a refused one is the API's one 401 byte for byte, a refused one taking a sign-in attempt of its
// address as on the API, a session's token is 403 with a sentence that points to the README —, at most sixty requests a
// minute per token, GET/HEAD/DELETE 405 and OAuth discovery 404 instead of the SPA shell; and what it says on connecting:
// mam, its title and the build's version, and instructions that differ with the token's access, on either protocol.
// Claude Code's protocol and the SDK's latest both connect, and no log line carries a token.
public sealed class McpEndpointHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = McpHttpExtensions.McpUrl;

    // The plainest [Authorize] GET of the HTTP API, whose 401 /mcp must answer with the same bytes.
    private const string ApiReferenceUrl = "/api/downloads";

    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

    private const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    private const string Ping = """{"jsonrpc":"2.0","id":3,"method":"ping"}""";

    // Spelled out rather than read from product code: what a script's author and an assistant's user read.
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";

    private const string SessionNotAcceptedError =
        "The MCP endpoint takes a personal access token, not a web app sign-in. Create a token on your Profile page and connect with it; see “Connect an AI assistant (MCP)” in the README.";

    private const string TooManyRequestsError = "Too many requests with this access token: at most 60 a minute. Wait a minute and try again.";

    private const string TooManyAttemptsError = "Too many attempts. Wait a minute and try again.";

    private const string ReadOnlyInstructions =
        "This token is read only: it reads everything its user reads, but can't send downloads, cancel following or mark Not interested. For those the user creates a token with “Read and download” access on their Profile page.";

    /// <summary>A token's shown prefix, <c>mam_pat_</c> and six characters: shown in lists on purpose; the rest is the
    /// secret.</summary>
    private const int SecretPrefixLength = 14;

    private const string WebAppOnlyInstructions =
        "Replacing, quarantining, restoring or deleting copies, and every setting, are done in the web app; no tool does them.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _member = null!;

    public McpEndpointHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("no token")]
    [InlineData("a wrong token")]
    [InlineData("a revoked token")]
    [InlineData("an expired token")]
    public async Task Post_without_a_usable_token_is_the_same_401_the_api_gives_byte_for_byte(string refusal)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string? presented = refusal switch
        {
            "no token" => null,
            // A real token's prefix, so only the hash of the whole value can tell it apart.
            "a wrong token" => WrongSecretOf((await _tokens.Create(_member.User)).Secret),
            "a revoked token" => (await _tokens.CreateRevoked(_member.User)).Secret,
            "an expired token" => (await _tokens.CreateExpired(_member.User)).Secret,
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
        };
        var apiRefused = await _factory.GetHttpRequest(ApiReferenceUrl, presented);

        //Act
        var response = await _factory.PostMcpMessageAsync(presented, Initialize);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, apiRefused.StatusCode);
        Assert.Equal(SignInRequiredBody, await apiRefused.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(apiRefused.Content.Headers.ContentType?.ToString(), response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            await apiRefused.Content.ReadAsByteArrayAsync(cancellationToken),
            await response.Content.ReadAsByteArrayAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.PersonalAccessTokens.AnyAsync(token => token.LastUsedUtc != null, cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // ADR 0040 on /mcp too: the sign-in limit of the address is the one brake on guessing tokens there, as the mcp limit
    // goes by a token's id, which a wrong token has none of. Each refused token takes an attempt; with the ten used up,
    // every token from that address is 429 and isn't even looked up, a good one included, on /mcp and the API alike.
    [Fact]
    public async Task Post_with_refused_tokens_uses_up_the_sign_in_attempts_of_their_address_then_every_token_from_it_is_429()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var good = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        string[] refused =
        [
            WrongSecretOf(good.Secret),
            (await _tokens.CreateRevoked(_member.User)).Secret,
            (await _tokens.CreateExpired(_member.User)).Secret,
        ];
        _factory.PinClientAddress();
        var failed = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < SignInRateLimitPolicy.PermitLimit; attempt++)
            failed.Add((await _factory.PostMcpMessageAsync(refused[attempt % refused.Length], Initialize)).StatusCode);

        //Act
        var goodToken = await _factory.PostMcpMessageAsync(good.Secret, Initialize);
        var onTheApi = await _factory.GetHttpRequest(ApiReferenceUrl, good.Secret);
        DateTime? lastUsedWhileLimited = DateTime.MinValue;
        await _factory.WithDbAsync(async db => lastUsedWhileLimited =
            (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(token => token.Id == good.Token.Id, cancellationToken)).LastUsedUtc);
        _factory.PinClientAddress();
        var fromAnotherAddress = await _factory.PostMcpMessageAsync(good.Secret, Initialize);

        //Assert
        Assert.All(failed, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, goodToken.StatusCode);
        Assert.Equal("application/json", goodToken.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TooManyAttemptsError, await goodToken.ReadErrorAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), goodToken.Headers.RetryAfter?.Delta);
        // The attempts /mcp took are the address's own, which the HTTP API counts too.
        Assert.Equal(HttpStatusCode.TooManyRequests, onTheApi.StatusCode);
        Assert.Equal(TooManyAttemptsError, await onTheApi.ReadErrorAsync(cancellationToken));
        // Not looked up while limited: its row was never touched.
        Assert.Null(lastUsedWhileLimited);
        Assert.Equal(HttpStatusCode.OK, fromAnotherAddress.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Post_with_a_sessions_access_token_is_403_with_the_sentence_and_no_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostMcpMessageAsync(_member.AccessToken, Initialize);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SessionNotAcceptedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("GET", true)]
    [InlineData("HEAD", false)]
    [InlineData("DELETE", false)]
    [InlineData("DELETE", true)]
    public async Task Method_other_than_post_is_405_allowing_post_with_an_empty_body(string method, bool withToken)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(new HttpMethod(method), BaseUrl);
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        if (withToken)
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (await _tokens.Create(_member.User)).Secret);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        // Not the SPA shell's 200: a client opening its GET stream after initialize would read that HTML as events.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["POST"], response.Content.Headers.Allow);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    [InlineData("/.well-known/oauth-authorization-server")]
    public async Task Get_of_oauth_discovery_is_a_bare_404_not_the_spa_shell(string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(url);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    public async Task Post_with_a_token_reaches_the_mcp_handler_with_or_without_the_trailing_slash(string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);

        //Act
        var response = await _factory.PostMcpMessageAsync(token.Secret, Ping, url);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("""{"result":{},"id":3,"jsonrpc":"2.0"}""", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_beyond_sixty_a_minute_with_one_token_is_429_with_the_mcp_sentence_while_another_token_gets_through()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var busy = await _tokens.Create(_member.User, name: "Looping assistant");
        var other = await _tokens.Create(_member.User, name: "Other assistant");
        var allowed = new List<HttpStatusCode>();
        for (var request = 0; request < 60; request++)
            allowed.Add((await _factory.PostMcpMessageAsync(busy.Secret, ListTools)).StatusCode);

        //Act
        var limited = await _factory.PostMcpMessageAsync(busy.Secret, ListTools);
        var otherToken = await _factory.PostMcpMessageAsync(other.Secret, ListTools);

        //Assert
        Assert.All(allowed, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TooManyRequestsError, await limited.ReadErrorAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        // Per token: the same user's other assistant isn't held up by the one in a loop.
        Assert.Equal(HttpStatusCode.OK, otherToken.StatusCode);
    }

    [Fact]
    public async Task Initialize_introduces_mam_with_its_title_and_the_builds_version()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        //Act
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(("mam", (string?)"Media Acquisition Manager", version), (client.ServerInfo.Name, client.ServerInfo.Title, client.ServerInfo.Version));
        Assert.Equal(McpHttpExtensions.ClaudeCodeProtocolVersion, client.NegotiatedProtocolVersion);
    }

    // Claude Code's initialize and the SDK's latest server/discover each carry the instructions, made for the token
    // that asks.
    [Theory]
    [InlineData(McpHttpExtensions.ClaudeCodeProtocolVersion, "2025-11-25")]
    [InlineData(null, "2026-07-28")]
    public async Task Connecting_on_either_protocol_gives_each_access_instructions_that_name_what_it_may_do(string? asked, string negotiated)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var readOnly = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly);
        var readAndDownload = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        await using var readOnlyClient = await _factory.ConnectMcpAsync(readOnly.Secret, asked, cancellationToken);
        await using var readAndDownloadClient = await _factory.ConnectMcpAsync(readAndDownload.Secret, asked, cancellationToken);

        //Assert
        Assert.Equal((negotiated, negotiated), (readOnlyClient.NegotiatedProtocolVersion, readAndDownloadClient.NegotiatedProtocolVersion));
        var readOnlyText = readOnlyClient.ServerInstructions!;
        var readAndDownloadText = readAndDownloadClient.ServerInstructions!;
        Assert.NotEqual(readOnlyText, readAndDownloadText);
        Assert.Contains(ReadOnlyInstructions, readOnlyText, StringComparison.Ordinal);
        Assert.DoesNotContain("call send_download", readOnlyText, StringComparison.Ordinal);
        Assert.DoesNotContain(ReadOnlyInstructions, readAndDownloadText, StringComparison.Ordinal);
        Assert.Contains("Only after the user has confirmed the exact release, client and destination, call send_download", readAndDownloadText, StringComparison.Ordinal);
        Assert.Contains("cancel_job only stops following a job", readAndDownloadText, StringComparison.Ordinal);
        Assert.Contains("dismiss_recommendation marks a recommendation Not interested", readAndDownloadText, StringComparison.Ordinal);
        Assert.All([readOnlyText, readAndDownloadText], text =>
        {
            Assert.Contains("search_library first", text, StringComparison.Ordinal);
            Assert.Contains(WebAppOnlyInstructions, text, StringComparison.Ordinal);
            Assert.Contains("Reply in the user's language.", text, StringComparison.Ordinal);
            // Well inside Claude Code's 4,096 characters of server instructions.
            Assert.InRange(text.Length, 1, 2_000);
        });
    }

    [Theory]
    [InlineData(McpHttpExtensions.ClaudeCodeProtocolVersion, "2025-11-25")]
    [InlineData(null, "2026-07-28")]
    public async Task Client_on_claude_codes_protocol_or_the_sdks_latest_connects_and_lists_the_tools(string? asked, string negotiated)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, asked, cancellationToken);

        //Act
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(negotiated, client.NegotiatedProtocolVersion);
        Assert.Equal("mam", client.ServerInfo.Name);
        Assert.Equal(9, tools.Count);
    }

    [Fact]
    public async Task Token_secret_appears_in_no_log_entry_whatever_the_endpoint_answers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        var wrong = WrongSecretOf(token.Secret);

        //Act
        await using (var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken))
        {
            await client.ListToolsAsync(cancellationToken: cancellationToken);
            await client.CallToolAsync("get_status", cancellationToken: cancellationToken);
            // A refused write: the one call that logs a Warning naming the token.
            await client.CallToolAsync("cancel_job", new Dictionary<string, object?> { ["jobId"] = Guid.NewGuid() }, cancellationToken: cancellationToken);
        }

        await _factory.PostMcpMessageAsync(wrong, Initialize);

        //Assert
        var entries = _factory.Logs.Entries;
        // Guards the guard: the run logged, the token's refusal among it.
        Assert.Contains(entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("tools/call cancel_job", StringComparison.Ordinal));
        var secrets = new[] { token.Secret[SecretPrefixLength..], wrong[SecretPrefixLength..] };
        Assert.All(entries, entry => Assert.All(secrets, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string WrongSecretOf(string secret) => secret[..^1] + (secret[^1] == 'A' ? 'B' : 'A');
}
