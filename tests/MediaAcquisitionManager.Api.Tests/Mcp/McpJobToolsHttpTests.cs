using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — list_jobs, get_job and cancel_job: the web app's Jobs page through MCP, the caller's own jobs only. A list is
// what GET /api/downloads answers for the same view, status and page — newest first, with the counts of every job of the
// caller in NeedsYou and InProgress whatever the filters —, a member's holding only their own; get_job is the job and its
// first 25 files as the Jobs page reads them, another user's job answering as a missing one. A cancel stops following,
// with its audit row and nothing sent to the client (ADR 0014); a job that already ended or is still being sent can't be
// cancelled, and records no refusal; a job whose replacement is still followed is refused with the HTTP API's sentence for
// a token, nothing changed and the refusal recorded, as DownloadsController.Cancel does.
public sealed class McpJobToolsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ListTool = "list_jobs";
    private const string GetTool = "get_job";
    private const string CancelTool = "cancel_job";
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ReleaseTitle = "Interstellar (2014) BDRip 1080p";

    // Spelled out rather than read from product code: what the assistant reads and tells its user.
    private const string JobNotFoundError = "There is no download job with this id among yours; list_jobs lists them with their ids.";
    private const string AlreadyDownloadedError = "This download is already finished, so there's nothing to stop following.";
    private const string StillBeingSentError = "This download is still being sent, so it can't be cancelled yet. Try again in a few minutes.";
    private const string PageSizeError = "Page size must be between 1 and 100.";

    private const string CancelEndsReplacementError =
        "This job also replaces a copy in the library, and an access token never touches a replacement. Cancel it on the Jobs page of the web app; see “Scripts and the API” in the README.";

    /// <summary>The owner's jobs, oldest first, one or more in each tab of the Jobs page: titled as the release each sent,
    /// with the replacement it carries, if any.</summary>
    private static readonly (string Title, DownloadJobStatus Status, ReplacementJobState? Replacement)[] OwnersJobs =
    [
        ("Needs linking", DownloadJobStatus.NeedsLinking, null),
        ("Created", DownloadJobStatus.Created, null),
        ("Downloading", DownloadJobStatus.Downloading, null),
        ("Downloaded", DownloadJobStatus.Downloaded, null),
        ("Failed", DownloadJobStatus.Failed, null),
        ("Cancelled", DownloadJobStatus.Cancelled, null),
        ("Waiting for review", DownloadJobStatus.Downloaded, ReplacementJobState.NeedsReview),
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public McpJobToolsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: "planted-torznab-key-0045");
        // Nothing answers it: the job tools never ask the client, and a request to it would be recorded.
        _client = await integrations.Create(
            "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, userName: FakeQBittorrentServer.DefaultUserName, password: "secret");
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── list_jobs ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("NeedsYou", new[] { "Waiting for review", "Needs linking" })]
    [InlineData("InProgress", new[] { "Downloading", "Created" })]
    [InlineData("Finished", new[] { "Cancelled", "Failed", "Downloaded" })]
    [InlineData(null, new[] { "Waiting for review", "Cancelled", "Failed", "Downloaded", "Downloading", "Created", "Needs linking" })]
    public async Task ListJobs_of_a_view_answers_what_the_jobs_page_lists_with_the_counts_of_every_job(string? view, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantMembersJobsAsync();
        await PlantOwnersJobsAsync();
        var page = await ReadAsync<DownloadJobListResponse>($"{BaseUrl}?view={view}&pageSize=20", _owner, cancellationToken);
        var token = await _tokens.Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var arguments = new Dictionary<string, object?>();
        if (view is not null)
            arguments["view"] = view;

        //Act
        var result = await CallAsync(client, ListTool, arguments, cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var list = result.ReadStructured<DownloadJobListResponse>();
        Assert.Equivalent(page, list, strict: true);
        Assert.Equal(expected, list.Items.Select(job => job.ReleaseTitle));
        // The member's jobs are theirs: never the owner's to list or count.
        Assert.Equal((expected.Length, 1, 20, 2, 2), (list.TotalCount, list.Page, list.PageSize, list.NeedsYouCount, list.InProgressCount));
        Assert.Equal(
            $"{expected.Length} jobs match; this page lists {expected.Length}. Among all your jobs, NeedsYou holds 2 and InProgress 2.",
            result.ReadText());
        // A read: it records nothing.
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task ListJobs_with_a_status_and_a_page_lists_only_those_jobs_newest_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantOwnersJobsAsync();
        var token = await _tokens.Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(
            client, ListTool, new() { ["status"] = "Downloaded", ["page"] = 2, ["pageSize"] = 1 }, cancellationToken);

        //Assert
        var list = result.ReadStructured<DownloadJobListResponse>();
        Assert.Equal(["Downloaded"], list.Items.Select(job => job.ReleaseTitle));
        Assert.Equal((2, 2, 1, 2, 2), (list.TotalCount, list.Page, list.PageSize, list.NeedsYouCount, list.InProgressCount));
        Assert.Equal("2 jobs match; this page lists 1. Among all your jobs, NeedsYou holds 2 and InProgress 2.", result.ReadText());
    }

    [Fact]
    public async Task ListJobs_of_a_member_lists_and_counts_only_their_own_jobs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantOwnersJobsAsync();
        await PlantMembersJobsAsync();
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, ListTool, new(), cancellationToken);

        //Assert
        var list = result.ReadStructured<DownloadJobListResponse>();
        Assert.Equal(["Member's downloading", "Member's linking"], list.Items.Select(job => job.ReleaseTitle));
        Assert.Equal((2, 1, 1), (list.TotalCount, list.NeedsYouCount, list.InProgressCount));
        Assert.Equal("2 jobs match; this page lists 2. Among all your jobs, NeedsYou holds 1 and InProgress 1.", result.ReadText());
    }

    [Fact]
    public async Task ListJobs_with_a_page_size_the_list_does_not_take_is_an_error_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, ListTool, new() { ["pageSize"] = 500 }, cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(PageSizeError, result.ReadText());
        Assert.Null(result.StructuredContent);
    }

    // ── get_job ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJob_answers_the_job_and_its_first_25_files_as_the_jobs_page_reads_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(_member.User, DownloadJobStatus.Downloading, progress: 0.426);
        await Jobs().AddFiles(
            job.Id,
            [.. Enumerable.Range(1, 27).Select(n => new DownloadJobFileEntity { RelativePath = $"Pack/{n:00}.mkv", SizeBytes = 1_000 + n, Progress = 0.5, IsWanted = true })]);
        var shown = await ReadAsync<DownloadJobDto>($"{BaseUrl}/{job.Id}", _member, cancellationToken);
        var files = await ReadAsync<DownloadJobFileListResponse>($"{BaseUrl}/{job.Id}/files?page=1&pageSize=25", _member, cancellationToken);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, GetTool, new() { ["jobId"] = job.Id }, cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var answer = result.ReadStructured<JobToolResult>();
        Assert.Equal(shown, answer.Job);
        Assert.Equivalent(files, answer.Files, strict: true);
        Assert.Equal((27, 25, "Pack/01.mkv", "Pack/25.mkv"), (answer.Files.TotalCount, answer.Files.Items.Count, answer.Files.Items[0].Path, answer.Files.Items[^1].Path));
        // Rounded down: a download at 42.6 % isn't at 43 % yet.
        Assert.Equal(
            "“Interstellar (2014) BDRip 1080p” in “qBittorrent” is Downloading at 42%; the first 25 of its 27 files are listed.",
            result.ReadText());
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task GetJob_of_a_job_whose_files_are_not_listed_yet_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A magnet still fetching its metadata: the client has said nothing of its files or its progress yet.
        var job = await PlantJobAsync(_member.User, DownloadJobStatus.Submitted);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, GetTool, new() { ["jobId"] = job.Id }, cancellationToken);

        //Assert
        Assert.Empty(result.ReadStructured<JobToolResult>().Files.Items);
        Assert.Equal(
            "“Interstellar (2014) BDRip 1080p” in “qBittorrent” is Submitted; its download client hasn't listed its files yet.",
            result.ReadText());
    }

    [Fact]
    public async Task GetJob_of_another_users_job_is_the_error_a_missing_one_gets()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownersJob = await PlantJobAsync(_owner.User, DownloadJobStatus.Downloading);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var others = await CallAsync(client, GetTool, new() { ["jobId"] = ownersJob.Id }, cancellationToken);
        var missing = await CallAsync(client, GetTool, new() { ["jobId"] = Guid.NewGuid() }, cancellationToken);

        //Assert
        // Nothing confirms the owner's job exists, as the HTTP API's bare 404 doesn't.
        Assert.Equal((true, JobNotFoundError), (others.IsError, others.ReadText()));
        Assert.Equal((true, JobNotFoundError), (missing.IsError, missing.ReadText()));
        Assert.Null(others.StructuredContent);
    }

    // ── cancel_job ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelJob_of_the_callers_job_stops_following_it_with_its_audit_row_and_sends_nothing_to_the_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(_member.User, DownloadJobStatus.Downloading);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, CancelTool, new() { ["jobId"] = job.Id }, cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var cancelled = result.ReadStructured<DownloadJobDto>();
        Assert.Equal((job.Id, DownloadJobStatus.Cancelled), (cancelled.Id, cancelled.Status));
        Assert.Equal(await ReadAsync<DownloadJobDto>($"{BaseUrl}/{job.Id}", _member, cancellationToken), cancelled);
        Assert.Equal(
            "MAM no longer follows “Interstellar (2014) BDRip 1080p”: the job is Cancelled, and the torrent stays in “qBittorrent” as it is.",
            result.ReadText());
        Assert.Equal(DownloadJobStatus.Cancelled, (await JobRowAsync(job.Id, cancellationToken)).Status);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.DownloadCancelled, (Guid?)_member.User.Id, "member", (Guid?)job.Id, now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetId, audit.CreatedUtc));
        });
        // ADR 0014 — a cancel only stops following: the torrent and its files stay in the client.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Downloaded, AlreadyDownloadedError)]
    // Younger than an interrupted send: its send may still be under way.
    [InlineData(DownloadJobStatus.Created, StillBeingSentError)]
    public async Task CancelJob_of_a_job_no_cancel_can_take_is_an_error_with_its_sentence_and_changes_nothing(
        DownloadJobStatus status, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(_member.User, status);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var before = await JobRowAsync(job.Id, cancellationToken);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, CancelTool, new() { ["jobId"] = job.Id }, cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(expectedError, result.ReadText());
        Assert.Null(result.StructuredContent);
        Assert.Equivalent(before, await JobRowAsync(job.Id, cancellationToken), strict: true);
        // Not a token's refusal: a session's cancel gets the same answer.
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Null((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(row => row.Id == token.Token.Id, cancellationToken)).LastRefusedUtc);
        });
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
    }

    [Fact]
    public async Task CancelJob_of_a_job_whose_replacement_is_followed_is_refused_with_the_sentence_changes_nothing_and_is_recorded()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The owner's own job and the owner's own token: refused because a token never touches a replacement, and the
        // cancel would end this one too.
        var job = await PlantJobAsync(_owner.User, DownloadJobStatus.Downloading);
        var replacement = await new ReplacementJobDataFactory(Db(), _factory.Time).Create(job.Id, _owner.User.Id, ReplacementJobState.Scheduled);
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, name: "Owner's Claude");
        var jobBefore = await JobRowAsync(job.Id, cancellationToken);
        var replacementBefore = await ReplacementRowAsync(replacement.Id, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, CancelTool, new() { ["jobId"] = job.Id }, cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(CancelEndsReplacementError, result.ReadText());
        Assert.Null(result.StructuredContent);
        Assert.Equivalent(jobBefore, await JobRowAsync(job.Id, cancellationToken), strict: true);
        Assert.Equivalent(replacementBefore, await ReplacementRowAsync(replacement.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRefused, (Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)token.Token.Id, (string?)"Owner's Claude", now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("tools/call cancel_job: CancelEndsReplacement", warning.Message, StringComparison.Ordinal);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task CancelJob_of_another_users_job_is_the_error_a_missing_one_gets_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownersJob = await PlantJobAsync(_owner.User, DownloadJobStatus.Downloading);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var before = await JobRowAsync(ownersJob.Id, cancellationToken);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, CancelTool, new() { ["jobId"] = ownersJob.Id }, cancellationToken);

        //Assert
        Assert.Equal((true, JobNotFoundError), (result.IsError, result.ReadText()));
        Assert.Equivalent(before, await JobRowAsync(ownersJob.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Task<CallToolResult> CallAsync(
        McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken) =>
        client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).AsTask();

    /// <summary>Every one of <see cref="OwnersJobs"/> as the owner's, a second apart, oldest first.</summary>
    private async Task PlantOwnersJobsAsync()
    {
        foreach (var (title, status, replacement) in OwnersJobs)
        {
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            var release = await _releases.Create(_toloka, title);
            var job = await Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, status, _owner.User.Id);
            if (replacement is { } state)
                await new ReplacementJobDataFactory(Db(), _factory.Time).Create(job.Id, _owner.User.Id, state);
        }
    }

    /// <summary>The member's own jobs — one that needs linking, one downloading —, never the owner's to list or count.</summary>
    private async Task PlantMembersJobsAsync()
    {
        foreach (var (title, status) in new[] { ("Member's linking", DownloadJobStatus.NeedsLinking), ("Member's downloading", DownloadJobStatus.Downloading) })
        {
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
            var release = await _releases.Create(_toloka, title);
            await Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, status, _member.User.Id);
        }
    }

    /// <summary>A job of <paramref name="user"/>'s left <paramref name="status"/> by the poller — no torrent in qBittorrent,
    /// which these tools never ask.</summary>
    private async Task<DownloadJobEntity> PlantJobAsync(UserEntity user, DownloadJobStatus status, double? progress = null)
    {
        var release = await _releases.CreateFound(user, _toloka, ReleaseTitle);
        return await Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, status, user.Id, progress: progress);
    }

    private ApplicationDbContext Db() => _factory.Scope.GetRequiredService<ApplicationDbContext>();

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(Db(), _factory.Time);

    /// <summary>The same answer as the web app reads it, through the HTTP API with <paramref name="user"/>'s session.</summary>
    private async Task<T> ReadAsync<T>(string url, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<DownloadJobEntity> JobRowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    private async Task<ReplacementJobEntity> ReplacementRowAsync(Guid replacementId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.ReplacementJobs.AsNoTracking().SingleAsync(job => job.Id == replacementId, cancellationToken));
        return row!;
    }
}
