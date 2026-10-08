using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
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

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-14 — the Jobs page's list: the caller's own jobs only — the installation owner's too (MAM-14's decision 1)
// — newest first, a tie of one instant broken by id, every field of each job with its progress and why it isn't
// moving on; filtered by status by the server, by name in any letter case, a value that isn't one answering 400
// with the sentence; paged by the server, past the last page an empty page with the real count; 400 for a page
// or a page size out of range; camelCase with statuses by name and instants in UTC. A read writes nothing and
// asks no client.
public sealed class DownloadsListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    // Spelled out rather than read from the product code: these are what the Jobs page shows.
    private const string PageError = "Page must be 1 or more.";
    private const string PageSizeError = "Page size must be between 1 and 100.";
    private const string MissingReason =
        "“qBittorrent” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private AuthorizedUser _otherMember = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _otherMember = await users.CreateAuthorized("other");
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        // Nothing answers it: a list never asks the client, and an armed one would hide a request.
        _client = await integrations.Create(
            "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, userName: FakeQBittorrentServer.DefaultUserName, password: "secret");
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_answers_the_callers_jobs_newest_first_with_every_field()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var oldest = await PlantAsync(_member, "Alien (1979)", DownloadJobStatus.Downloaded, progress: 1);
        await PlantAsync(_otherMember, "Another's (2001)", DownloadJobStatus.Downloading);
        var middle = await PlantAsync(_member, "Blade Runner (1982)", DownloadJobStatus.Downloading, progress: 0.42, blockReason: MissingReason);
        await PlantAsync(_owner, "Owner's (2002)", DownloadJobStatus.Submitted);
        var newest = await PlantAsync(_member, "Contact (1997)", DownloadJobStatus.Submitted);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((3, 1, 25), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal([newest.Id, middle.Id, oldest.Id], list.Items.Select(job => job.Id));
        Assert.Equivalent(
            new DownloadJobDto(
                middle.Id,
                middle.ReleaseId,
                "Blade Runner (1982)",
                "Toloka",
                "qBittorrent",
                "Movies",
                "Movies",
                DownloadJobStatus.Downloading,
                0.42,
                Hash(middle),
                Error: null,
                MissingReason,
                OverwriteCheck: null,
                middle.CreatedUtc,
                middle.StatusChangedUtc),
            list.Items[1],
            strict: true);
        // Each the same as reading it alone.
        foreach (var job in list.Items)
            Assert.Equivalent(job, await ReadJobAsync(job.Id, _member, cancellationToken), strict: true);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task List_of_the_installation_owner_holds_only_their_own_jobs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAsync(_member, "Alien (1979)", DownloadJobStatus.Downloading);
        var owners = await PlantAsync(_owner, "Owner's (2002)", DownloadJobStatus.Paused);
        // An older build's job, or one whose sender's account was removed: nobody's.
        await Jobs().Create((await _releases.CreateFound(_member.User, _toloka)).Id, _client.Id, _client.Name, _movies.Id);

        //Act
        var forOwner = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var forOtherMember = await _factory.GetHttpRequest(BaseUrl, _otherMember.AccessToken);

        //Assert
        // MAM-14's decision 1: a job stays its sender's, the owner's reach included (MAM-13's rule).
        var ownersList = await ReadListAsync(forOwner, cancellationToken);
        Assert.Equal((1, owners.Id), (ownersList.TotalCount, Assert.Single(ownersList.Items).Id));
        var othersList = await ReadListAsync(forOtherMember, cancellationToken);
        Assert.Equal(0, othersList.TotalCount);
        Assert.Empty(othersList.Items);
    }

    [Theory]
    [InlineData("downloading")]
    [InlineData("DOWNLOADING")]
    [InlineData(" Downloading ")]
    public async Task List_filters_by_status_by_name_in_any_letter_case(string status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await PlantAsync(_member, "Alien (1979)", DownloadJobStatus.Downloading);
        await PlantAsync(_member, "Blade Runner (1982)", DownloadJobStatus.Paused);
        var second = await PlantAsync(_member, "Contact (1997)", DownloadJobStatus.Downloading);
        await PlantAsync(_member, "Dune (2021)", DownloadJobStatus.Cancelled);
        await PlantAsync(_otherMember, "Another's (2001)", DownloadJobStatus.Downloading);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?status={Uri.EscapeDataString(status)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(2, list.TotalCount);
        Assert.Equal([second.Id, first.Id], list.Items.Select(job => job.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_with_a_blank_status_lists_every_status(string status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAsync(_member, "Alien (1979)", DownloadJobStatus.Failed);
        await PlantAsync(_member, "Blade Runner (1982)", DownloadJobStatus.NeedsLinking);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?status={Uri.EscapeDataString(status)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await ReadListAsync(response, cancellationToken)).TotalCount);
    }

    [Theory]
    [InlineData("Stalled", "Stalled")]
    // By name only: a number, or two names, would be an enum's value to Enum.TryParse.
    [InlineData("3", "3")]
    [InlineData("Created, Failed", "Created, Failed")]
    [InlineData("downloading-ish-and-then-a-lot-more-of-it", "downloading-ish-and-then-a-lot-m…")]
    public async Task List_with_a_status_that_is_not_one_is_400_with_why(string status, string echoed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAsync(_member, "Alien (1979)", DownloadJobStatus.Downloading);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?status={Uri.EscapeDataString(status)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"“{echoed}” is not a download status. Use Created, Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled.",
            await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(1, new[] { "E", "D" })]
    [InlineData(2, new[] { "C", "B" })]
    [InlineData(3, new[] { "A" })]
    // Past the last page: nothing, with the real count, not an error.
    [InlineData(4, new string[0])]
    [InlineData(int.MaxValue, new string[0])]
    public async Task List_pages_the_newest_first_list(int page, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var title in new[] { "A", "B", "C", "D", "E" })
            await PlantAsync(_member, title, DownloadJobStatus.Submitted);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page={page}&pageSize=2", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((5, page, 2), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal(expected, list.Items.Select(job => job.ReleaseTitle));
    }

    [Theory]
    [InlineData(0, 25, PageError)]
    [InlineData(-1, 25, PageError)]
    [InlineData(1, 0, PageSizeError)]
    [InlineData(1, 101, PageSizeError)]
    public async Task List_with_a_page_or_page_size_out_of_range_is_400_with_why(int page, int pageSize, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page={page}&pageSize={pageSize}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_with_a_page_that_is_not_a_number_is_400()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=abc", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_breaks_a_tie_of_one_instant_by_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobs = new List<DownloadJobEntity>();
        for (var n = 0; n < 4; n++)
            jobs.Add(await PlantAsync(_member, $"Same instant {n}", DownloadJobStatus.Submitted, advance: false));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?pageSize=2", _member.AccessToken);
        var next = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=2", _member.AccessToken);

        //Assert
        // A total order, so two pages never show one job twice or skip one: the id, highest first, as SQLite
        // compares its text.
        var expected = jobs.Select(job => job.Id).OrderByDescending(id => id.ToString(), StringComparer.Ordinal).ToList();
        var shown = (await ReadListAsync(response, cancellationToken)).Items.Concat((await ReadListAsync(next, cancellationToken)).Items);
        Assert.Equal(expected, shown.Select(job => job.Id));
    }

    [Fact]
    public async Task List_answers_camel_case_with_statuses_by_name_and_utc_instants()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAsync(_member, "Alien (1979)", DownloadJobStatus.NeedsLinking);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        Assert.Equal(["items", "totalCount", "page", "pageSize"], root.EnumerateObject().Select(property => property.Name));
        var item = root.GetProperty("items")[0];
        Assert.Equal(
            [
                "id", "releaseId", "releaseTitle", "sourceName", "downloadClientName", "destinationName", "clientDestinationKey", "status",
                "progress", "clientJobId", "error", "blockReason", "overwriteCheck", "createdUtc", "statusChangedUtc",
            ],
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal("NeedsLinking", item.GetProperty("status").GetString());
        Assert.EndsWith("Z", item.GetProperty("createdUtc").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", item.GetProperty("statusChangedUtc").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_without_a_token_is_401()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A job of <paramref name="user"/>'s that sent a release titled <paramref name="title"/> to qBittorrent's
    /// Movies, as a pass left it; a second later than the one before unless <paramref name="advance"/> is
    /// <c>false</c>. Its client id is a hash of its own (<see cref="Hash"/>).
    /// </summary>
    private async Task<DownloadJobEntity> PlantAsync(
        AuthorizedUser user, string title, DownloadJobStatus status, double? progress = null, string? blockReason = null, bool advance = true)
    {
        if (advance)
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var release = await _releases.CreateFound(user.User, _toloka, title);
        var hash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(40, '0');
        return await Jobs().Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            status,
            user.User.Id,
            infoHashV1: hash,
            clientJobId: hash,
            progress: progress,
            blockReason: blockReason,
            blockReasonOrigin: blockReason is null ? null : DownloadJobErrorOrigin.App);
    }

    /// <summary>The client id <see cref="PlantAsync"/> gave <paramref name="job"/>.</summary>
    private static string Hash(DownloadJobEntity job) => job.ClientJobId!;

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private static async Task<DownloadJobListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<DownloadJobDto> ReadJobAsync(Guid jobId, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>What a read may not change: every job as it stands, the audit rows, and the requests qBittorrent
    /// has had.</summary>
    private async Task<(string Jobs, int AuditEvents, int ClientRequests)> SnapshotAsync(CancellationToken cancellationToken)
    {
        var jobs = "";
        var auditEvents = 0;
        await _factory.WithDbAsync(async db =>
        {
            jobs = string.Join(
                "\n",
                (await db.DownloadJobs.AsNoTracking().OrderBy(job => job.CreatedUtc).ThenBy(job => job.Id).ToListAsync(cancellationToken))
                .Select(job => $"{job.Id} {job.Status} {job.Progress} {job.ClientJobId} {job.BlockReason} {job.StatusChangedUtc:O}"));
            auditEvents = await db.AuditEvents.CountAsync(cancellationToken);
        });
        return (jobs, auditEvents, _factory.QBittorrentHttp.Requests.Count);
    }

    private async Task AssertUnchangedAsync((string Jobs, int AuditEvents, int ClientRequests) before, CancellationToken cancellationToken) =>
        Assert.Equal(before, await SnapshotAsync(cancellationToken));
}
