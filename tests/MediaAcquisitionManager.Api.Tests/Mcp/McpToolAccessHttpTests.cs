using System.Text.Json;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — the access gate on tools/call (ADR 0040, Vasyl's decision c): a Read only token calling one of the three writes
// anyway — a client that kept an old list, a model that guessed the name — gets the HTTP API's read-only sentence as an
// isError result, with nothing written or sent, a warning naming "tools/call <tool>" each time and one audit row a minute;
// arguments that don't fit a tool get a sentence that says what to fix, not the SDK's "An error occurred", while the same
// exceptions thrown by the tool itself stay the SDK's error, logged at Error; a name no tool has gets the SDK's own
// "Unknown tool".
public sealed class McpToolAccessHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from PersonalAccessTokenErrors: what the assistant reads and tells its user.
    private const string ReadOnlyError =
        "This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README.";

    private const string ArgumentsHint = " Check them against the tool's input schema and call it again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private UserEntity _member = null!;

    public McpToolAccessHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.Create("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("send_download")]
    [InlineData("cancel_job")]
    [InlineData("dismiss_recommendation")]
    public async Task Call_of_a_write_with_a_read_only_token_is_an_error_with_the_read_only_sentence_and_changes_nothing(string tool)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadOnly, name: "Library report");
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(tool, ArgumentsOf(tool), cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(ReadOnlyError, result.ReadText());
        Assert.Null(result.StructuredContent);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.RecommendationDismissals.CountAsync(cancellationToken));
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRefused, (Guid?)_member.Id, "member", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)token.Token.Id, (string?)"Library report", now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains($"tools/call {tool}: ReadOnly", warning.Message, StringComparison.Ordinal);
        // Nothing reached a download client.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task Refused_calls_of_one_token_log_a_warning_each_and_make_one_audit_row_a_minute()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        await client.CallToolAsync("send_download", ArgumentsOf("send_download"), cancellationToken: cancellationToken);
        await client.CallToolAsync("cancel_job", ArgumentsOf("cancel_job"), cancellationToken: cancellationToken);
        await client.CallToolAsync("cancel_job", ArgumentsOf("cancel_job"), cancellationToken: cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await client.CallToolAsync("dismiss_recommendation", ArgumentsOf("dismiss_recommendation"), cancellationToken: cancellationToken);

        //Assert
        var warnings = _factory.Logs.Entries
            .Where(entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName)
            .Select(entry => entry.Message)
            .ToList();
        Assert.Collection(
            warnings,
            message => Assert.Contains("tools/call send_download", message, StringComparison.Ordinal),
            message => Assert.Contains("tools/call cancel_job", message, StringComparison.Ordinal),
            message => Assert.Contains("tools/call cancel_job", message, StringComparison.Ordinal),
            message => Assert.Contains("tools/call dismiss_recommendation", message, StringComparison.Ordinal));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(2, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRefused, cancellationToken)));
    }

    [Fact]
    public async Task Call_of_a_read_with_a_read_only_token_passes_the_gate_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync("get_status", cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
    }

    [Theory]
    [InlineData("an id that isn't a uuid", "get_job", "jobId", "not-a-job-id",
        "The arguments don't fit get_job: The JSON value could not be converted to System.Guid. Path: $ | LineNumber: 0 | BytePositionInLine: 14.")]
    [InlineData("a name the enum doesn't have", "list_jobs", "view", "Waiting",
        "The arguments don't fit list_jobs: The JSON value could not be converted to System.Nullable`1[MediaAcquisitionManager.Core.Enums.DownloadJobView]. Path: $ | LineNumber: 0 | BytePositionInLine: 9.")]
    [InlineData("a required argument left out", "get_job", null, null,
        "The arguments don't fit get_job: The arguments dictionary is missing a value for the required parameter 'jobId'. (Parameter 'arguments')")]
    public async Task Call_with_arguments_that_dont_fit_is_an_error_saying_what_to_fix(string scenario, string tool, string? argument, string? value, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var arguments = argument is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { [argument] = value };

        //Act
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError, scenario);
        Assert.Equal(expected + ArgumentsHint, result.ReadText());
        // The caller's mistake, not the app's: nothing logged as an error.
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData("ArgumentException")]
    [InlineData("JsonException")]
    public async Task Call_whose_tool_throws_an_argument_or_json_exception_of_its_own_is_the_sdks_error_logged_as_an_error(string thrown)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadOnly);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        // A bug past the arguments, inside the tool's service: what a ToDictionary over a repeated key, or an answer the
        // serializer can't write, throws.
        Exception bug = thrown == "ArgumentException"
            ? new ArgumentException("An item with the same key has already been added. Key: Movie")
            : new JsonException("A possible object cycle was detected.");
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"DownloadJobs\"", StringComparison.Ordinal),
            _ => throw bug);

        //Act
        var result = await client.CallToolAsync("list_jobs", cancellationToken: cancellationToken);

        //Assert
        // Not "The arguments don't fit": the arguments were right, and a model told otherwise would only change them.
        Assert.True(result.IsError);
        Assert.Equal("An error occurred invoking 'list_jobs'.", result.ReadRawText());
        // The SDK's own Error entry, with the exception: a server bug must show in the log.
        var logged = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(McpServer).FullName && entry.Level >= LogLevel.Error);
        Assert.Equal(((LogLevel?)LogLevel.Error, "\"list_jobs\" threw an unhandled exception."), ((LogLevel?)logged.Level, logged.Message));
        Assert.Same(bug, logged.Exception);
    }

    [Fact]
    public async Task Call_of_a_tool_no_one_has_is_the_sdks_unknown_tool_error_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var call = async () => await client.CallToolAsync("replace_copy", cancellationToken: cancellationToken);

        //Assert
        var refused = await Assert.ThrowsAsync<McpProtocolException>(call);
        Assert.Equal(McpErrorCode.InvalidParams, refused.ErrorCode);
        Assert.Contains("Unknown tool: 'replace_copy'", refused.Message, StringComparison.Ordinal);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Arguments that fit each write: the gate refuses before they are bound, so nothing they name has to exist.
    private static Dictionary<string, object?> ArgumentsOf(string tool) => tool switch
    {
        "send_download" => new()
        {
            ["releaseId"] = Guid.NewGuid(),
            ["downloadClientId"] = Guid.NewGuid(),
            ["destinationId"] = Guid.NewGuid(),
            ["requestId"] = Guid.NewGuid(),
        },
        "cancel_job" => new() { ["jobId"] = Guid.NewGuid() },
        "dismiss_recommendation" => new() { ["kind"] = "Movie", ["catalogId"] = "603" },
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
    };
}
