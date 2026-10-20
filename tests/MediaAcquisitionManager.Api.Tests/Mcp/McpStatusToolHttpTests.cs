using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — get_status (Vasyl's decision f): who the tools act as — the user, whether they are the owner, and the calling
// token's name, access and expiry —, MAM's version, the library sources as the Library page gives them to that user, the
// latest recommendations run as runs/latest gives it, and the release sources whose last recommendations call failed as
// the Recommendations page names them: their sentences and sync errors for the owner only. Enums go by name and instants
// with a Z, as the HTTP API writes them.
public sealed class McpStatusToolHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string Tool = "get_status";
    private const string LibrarySourcesUrl = "/api/library/sources";
    private const string LatestRunUrl = "/api/recommendations/runs/latest";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string JackettApiKey = "planted-jackett-key-0045";

    /// <summary>A sync's error as the owner reads it: it can name the server's address.</summary>
    private const string SyncError = "Jellyfin at http://127.0.0.1:9 refused the API key.";

    /// <summary>The Search page's sentence for Jackett's error 100, spelled out.</summary>
    private const string RefusedKeyError =
        "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public McpStatusToolHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetStatus_with_the_owners_token_names_the_owner_the_token_and_the_builds_version()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var expires = _factory.Time.GetUtcNow().UtcDateTime.AddDays(90);
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, expires, "Owner's Claude");
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var status = result.ReadStructured<StatusToolResult>();
        Assert.Equal(new McpCallerDto("owner", true, "Owner's Claude", PersonalAccessTokenAccess.ReadAndDownload, expires), status.Caller);
        Assert.Equal(version, status.Version);
        Assert.Equal(
            $"You act as owner (the installation owner) with the Read and download token “Owner's Claude”, on MAM {version}; 0 library sources, 0 release sources failing.",
            result.ReadText());
        // A read: it records nothing.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task GetStatus_with_a_members_token_names_the_member_and_a_token_that_never_expires_by_name_and_with_nulls_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly, name: "Library report");
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        var status = result.ReadStructured<StatusToolResult>();
        Assert.Equal(new McpCallerDto("member", false, "Library report", PersonalAccessTokenAccess.ReadOnly, null), status.Caller);
        Assert.Null(status.LatestRecommendationRun);
        Assert.StartsWith("You act as member (a member) with the Read only token “Library report”", result.ReadText(), StringComparison.Ordinal);
        // The JSON as the output schema says it: the access by name, and every null written, not left out.
        var caller = result.StructuredContent!.Value.GetProperty("caller").GetRawText();
        Assert.Equal(
            """{"userName":"member","isInstallationOwner":false,"tokenName":"Library report","tokenAccess":"ReadOnly","tokenExpiresUtc":null}""",
            caller);
        Assert.Contains("\"latestRecommendationRun\":null", JsonTextOf(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, PersonalAccessTokenAccess.ReadAndDownload, "You act as owner (the installation owner) with the Read and download token “Script”, on MAM ")]
    [InlineData(true, PersonalAccessTokenAccess.ReadOnly, "You act as owner (the installation owner) with the Read only token “Script”, on MAM ")]
    [InlineData(false, PersonalAccessTokenAccess.ReadAndDownload, "You act as member (a member) with the Read and download token “Script”, on MAM ")]
    [InlineData(false, PersonalAccessTokenAccess.ReadOnly, "You act as member (a member) with the Read only token “Script”, on MAM ")]
    public async Task GetStatus_summary_names_the_callers_role_and_the_tokens_access(bool asOwner, PersonalAccessTokenAccess access, string expected)
    {
        //Arrange
        // MAM-47 (ADR 0042) — a whole sentence per role and access, so each of the four is spelled out here.
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(asOwner ? _owner.User : _member.User, access, name: "Script");
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.StartsWith(expected, result.ReadText(), StringComparison.Ordinal);
        Assert.EndsWith("; 0 library sources, 0 release sources failing.", result.ReadText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatus_writes_its_json_text_with_cyrillic_as_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, name: "Звіт бібліотеки");
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        // Not a \uXXXX escape per letter: a household's library is mostly Ukrainian, and the model reads this text.
        Assert.Contains("\"tokenName\":\"Звіт бібліотеки\"", JsonTextOf(result), StringComparison.Ordinal);
        Assert.Equal("Звіт бібліотеки", result.ReadStructured<StatusToolResult>().Caller.TokenName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStatus_gives_the_library_sources_as_the_library_page_gives_them_to_the_owner_and_to_a_member(bool asTheOwner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = asTheOwner ? _owner : _member;
        var livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await new LibrarySyncRunSeeder(_factory.Scope).Create(livingRoom, LibrarySyncStatus.Failed, _owner.User, startedUtc, Message.Legacy(SyncError));
        var token = await _tokens.Create(user.User);
        var page = await ReadAsync<LibrarySourceListResponse>(LibrarySourcesUrl, user, cancellationToken);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        var library = result.ReadStructured<StatusToolResult>().Library;
        Assert.Equivalent(page, library, strict: true);
        var source = Assert.Single(library.Items);
        Assert.Equal(
            ("Living room", LibrarySyncStatus.Failed, asTheOwner ? SyncError : null, asTheOwner ? "owner" : null),
            (source.Name, source.LatestRun!.Status, source.LatestRun.Error, source.LatestRun.RequestedByUserName));
        Assert.EndsWith("1 library source, 0 release sources failing.", result.ReadText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatus_gives_the_latest_recommendations_run_as_runs_latest_gives_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationRunSeeder(_factory.Scope).Create(
            RecommendationRunStatus.Succeeded, RecommendationRunTrigger.Requested, _owner.User, _factory.Time.GetUtcNow().UtcDateTime, 12, 340);
        var token = await _tokens.Create(_member.User);
        var latest = await ReadAsync<RecommendationRunDto>(LatestRunUrl, _member, cancellationToken);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        var run = result.ReadStructured<StatusToolResult>().LatestRecommendationRun;
        Assert.Equal(latest, run);
        Assert.Equal((RecommendationRunStatus.Succeeded, (int?)340, (string?)null), (run!.Status, run.CandidateCount, run.RequestedByUserName));
        // Instants leave with their Z, as the HTTP API writes them.
        Assert.Matches("\"createdUtc\":\"[^\"]+Z\"", JsonTextOf(result));
    }

    [Fact]
    public async Task GetStatus_names_the_release_sources_whose_last_recommendations_call_failed_with_their_sentence_for_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jackett, the only release source, refusing the key at the availability check of a recommended film.
        await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: JackettApiKey);
        var jackett = FakeTorznabServer.Jackett(JackettAddress, JackettApiKey);
        jackett.Intercept = _ => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);
        jackett.Arm(_factory.TorznabHttp);
        var film = await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Movie, "157336", "Інтерстеллар", new DateOnly(2014, 11, 5), originalTitle: "Interstellar");
        await new RecommendationCandidateSeeder(_factory.Scope).Create(film, RecommendationTab.Movies);
        var failedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var ownersToken = await _tokens.Create(_owner.User);
        var membersToken = await _tokens.Create(_member.User);
        await using var ownersClient = await _factory.ConnectMcpAsync(ownersToken.Secret, cancellationToken: cancellationToken);
        await using var membersClient = await _factory.ConnectMcpAsync(membersToken.Secret, cancellationToken: cancellationToken);

        //Act
        var owners = await ownersClient.CallToolAsync(Tool, cancellationToken: cancellationToken);
        var members = await membersClient.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            [new RecommendationSourceFailureDto("Toloka", IntegrationCallStatus.AuthenticationFailed, RefusedKeyError, failedUtc)],
            owners.ReadStructured<StatusToolResult>().FailedRecommendationSources);
        // A member sees the status, not the owner's sentence (the MAM-8 rule), as on the Recommendations page.
        Assert.Equal(
            [new RecommendationSourceFailureDto("Toloka", IntegrationCallStatus.AuthenticationFailed, null, failedUtc)],
            members.ReadStructured<StatusToolResult>().FailedRecommendationSources);
        Assert.EndsWith("0 library sources, 1 release source failing.", owners.ReadText(), StringComparison.Ordinal);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The same answer as the web app reads it, through the HTTP API with <paramref name="user"/>'s session.</summary>
    private async Task<T> ReadAsync<T>(string url, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The JSON text block a success carries beside its summary.</summary>
    private static string JsonTextOf(CallToolResult result) => result.Content.OfType<TextContentBlock>().Last().Text;
}
