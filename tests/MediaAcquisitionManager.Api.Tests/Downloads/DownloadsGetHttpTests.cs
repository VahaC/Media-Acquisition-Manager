using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-13 — reading one job back, as the Download dialog does after a send: the caller's own job, every
// field, camelCase with its status by name and its instants in UTC; the names it was sent under even after
// the client, the destination or the source went, and after its search is a day old and pruned. Another
// user's job — the installation owner's reach included — one nobody is recorded as sending, and one that
// never was answer the same bare 404, so nothing confirms a job exists — (MAM-27) one carrying a replacement that
// stopped for review too. A read writes nothing and asks no client and no feed.
public sealed class DownloadsGetHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string ReleaseTitle = "Interstellar (2014) BDRip 1080p";

    // The v1 info hash of TorznabSamples.TorrentFile(), which the feed's download links answer: qBittorrent's
    // id of the torrent the send adds.
    private const string SampleTorrentHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    // Spelled out rather than read from the product code: this is what the Download dialog shows.
    private const string AlreadyHasError = "“qBittorrent” already has this torrent, so nothing was sent.";

    // MAM-27 — a replacement's ticked copies and why its check stopped.
    private const string DroppedCopyPath = "/media/movies/Interstellar (2014)/Interstellar (2014) 720p.mkv";
    private const string KeptCopyPath = "/media/movies/Interstellar (2014)/Interstellar (2014) 1080p.mkv";
    private const string StoppedReason =
        "“/media/movies/Interstellar.2014.BDRip.1080p.mkv” runs 1:06:40, less than three quarters of the old copy's 2:49:03.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private AuthorizedUser _otherMember = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsGetHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.CreateAuthorized("member");
        _otherMember = await _users.CreateAuthorized("other");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Get_of_the_callers_job_is_200_with_every_field()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new DownloadJobDto(
                sent.Id,
                release.Id,
                ReleaseTitle,
                "Toloka",
                "qBittorrent",
                "Movies",
                "Movies",
                DownloadJobStatus.Submitted,
                Progress: null,
                SampleTorrentHash,
                Error: null,
                BlockReason: null,
                // MAM-38 — sent with no Media folder allowed, so its folder couldn't be looked into.
                OverwriteCheck: DownloadOverwriteCheck.NotCheckedFolderOutsideMediaFolders,
                now,
                now),
            body,
            strict: true);
        // The same job the send answered with, read from its row.
        Assert.Equivalent(sent, body, strict: true);
        await _factory.WithDbAsync(async db =>
        {
            var job = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(body!.Id, job.Id);
            Assert.Equal(_member.User.Id, job.RequestedByUserId);
            Assert.Equal(DownloadJobStatus.Submitted, job.Status);
            Assert.Equal(SampleTorrentHash, job.ClientJobId);
        });
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_answers_camel_case_with_the_status_by_name_and_utc_instants()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken);

        //Assert
        // Exactly these properties — nothing that locates the payload: the release's link stays on the
        // server (ADR 0004).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(
            [
                "id", "releaseId", "releaseTitle", "sourceName", "downloadClientName", "destinationName", "clientDestinationKey", "status",
                "progress", "clientJobId", "error", "blockReason", "overwriteCheck", "createdUtc", "statusChangedUtc",
                // MAM-24 — the replacement it carries; null for a Keep both send.
                "replacement",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Submitted", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        // MAM-14 — not read from the client yet, and nothing blocks it.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("progress").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("blockReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("replacement").ValueKind);
        Assert.EndsWith("Z", root.GetProperty("createdUtc").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", root.GetProperty("statusChangedUtc").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ReleaseSeeder.PlantedLinkToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("prowlarr.test", text, StringComparison.OrdinalIgnoreCase);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_of_a_failed_job_carries_why_it_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        // Added in qBittorrent itself before the send (decision 4).
        _qbittorrent.AddTorrent(TorrentInfoHashReader.FromTorrentFile(TorznabSamples.TorrentFile()));
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(DownloadJobStatus.Failed, body!.Status);
        Assert.Equal(AlreadyHasError, body.Error);
        Assert.Null(body.ClientJobId);
        Assert.Equivalent(sent, body, strict: true);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(AlreadyHasError, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).Error));
        Assert.Empty(_qbittorrent.AddRequests);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_of_a_job_whose_client_destination_and_source_were_removed_still_names_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        // The owner removes both integrations; the client's destinations go with it.
        Assert.Equal(HttpStatusCode.NoContent, (await _factory.DeleteHttpRequest($"/api/settings/integrations/{_client.Id}", _owner.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _factory.DeleteHttpRequest($"/api/settings/integrations/{_toloka.Id}", _owner.AccessToken)).StatusCode);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken);

        //Assert
        // The names as they were when the job was made: a job reads the same after any of them is gone.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(sent, body, strict: true);
        await _factory.WithDbAsync(async db =>
        {
            var job = await db.DownloadJobs.AsNoTracking().SingleAsync(row => row.Id == sent.Id, cancellationToken);
            Assert.Null(job.DownloadClientConnectionId);
            Assert.Null(job.DestinationMappingId);
            Assert.Null((await db.Releases.AsNoTracking().SingleAsync(row => row.Id == release.Id, cancellationToken)).SourceConnectionId);
        });
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_of_a_job_whose_search_was_pruned_still_answers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(25));
        var member = await _users.CreateSession(_member.User);
        // The next search removes the old one; the release the job sent stays as its evidence.
        await _factory.SearchReleasesAsync(member.AccessToken, "Interstellar", cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(sent, body, strict: true);
        await _factory.WithDbAsync(async db =>
            Assert.Null((await db.Releases.AsNoTracking().SingleAsync(row => row.Id == release.Id, cancellationToken)).SearchSourceId));
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_of_another_users_job_is_a_bare_404_even_for_the_installation_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var forOtherMember = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _otherMember.AccessToken);
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _owner.AccessToken);

        //Assert
        await AssertBare404Async(forOtherMember, cancellationToken);
        await AssertBare404Async(forOwner, cancellationToken);
        // Still there for its sender.
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken)).StatusCode);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_of_another_users_job_with_a_stopped_replacement_is_a_bare_404_even_for_the_installation_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        // MAM-27 — a replacement the check stopped, one of its copies dropped, the other one a drop alone settles. Only the
        // owner schedules, so a member's job never carries one: planted, it proves the line it would bring opens no door
        // the job's own scoping keeps shut.
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var replacements = new ReplacementJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        var replacement = await replacements.Create(
            sent.Id, _member.User.Id, ReplacementJobState.NeedsReview, StoppedReason, inspectionStartedUtc: now.AddHours(-2));
        await replacements.AddFile(replacement.Id, DroppedCopyPath, TestMedia.File(inode: 1), position: 0, problem: StoppedReason, droppedUtc: now);
        await replacements.AddFile(replacement.Id, KeptCopyPath, TestMedia.File(inode: 2), position: 1, problem: StoppedReason, onlyDropSettles: true);
        var before = await SnapshotAsync(cancellationToken);
        var replacementRowsBefore = await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);

        //Act
        var forOtherMember = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _otherMember.AccessToken);
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _owner.AccessToken);
        var forOwnersList = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        await AssertBare404Async(forOtherMember, cancellationToken);
        await AssertBare404Async(forOwner, cancellationToken);
        Assert.Empty((await forOwnersList.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items);
        // Still there for its sender, with its replacement.
        var forSender = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken);
        Assert.Equal(HttpStatusCode.OK, forSender.StatusCode);
        var read = (await forSender.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((replacement.Id, 1, 1), (read.Replacement!.Id, read.Replacement.CopyCount, read.Replacement.DroppedCopyCount));
        await AssertUnchangedAsync(before, cancellationToken);
        Assert.Equal(replacementRowsBefore, await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken));
    }

    [Fact]
    public async Task Get_of_a_job_nobody_is_recorded_as_sending_is_a_bare_404_for_everyone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        // An older build's job, or one whose sender's account was removed: its sender is null.
        var jobs = new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        var job = await jobs.Create(release.Id, _client.Id, _client.Name, _movies.Id);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var forMember = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}", _member.AccessToken);
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}", _owner.AccessToken);

        //Assert
        await AssertBare404Async(forMember, cancellationToken);
        await AssertBare404Async(forOwner, cancellationToken);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_of_a_job_that_never_was_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        await SendAsync(_member, release.Id, cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _member.AccessToken);

        //Assert
        await AssertBare404Async(response, cancellationToken);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_with_an_id_that_is_not_one_is_404_naming_no_endpoint()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/abc", _member.AccessToken);

        //Assert
        // The route takes only an id, so this is the API's answer for no endpoint at all.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("No API endpoint matches GET /api/downloads/abc.", await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Get_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release.Id, cancellationToken);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUnchangedAsync(before, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sends <paramref name="releaseId"/> as <paramref name="user"/> to the test's qBittorrent and its
    /// Movies destination, the way the Download dialog does; throws unless a job was made.</summary>
    private async Task<DownloadJobDto> SendAsync(AuthorizedUser user, Guid releaseId, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), releaseId, _client.Id, _movies.Id));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The send answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>What a read may not change: every job as it stands, the audit rows, and the requests the
    /// feed and qBittorrent have had.</summary>
    private async Task<(string Jobs, int AuditEvents, int FeedRequests, int ClientRequests)> SnapshotAsync(CancellationToken cancellationToken)
    {
        var jobs = "";
        var auditEvents = 0;
        await _factory.WithDbAsync(async db =>
        {
            jobs = string.Join(
                "\n",
                (await db.DownloadJobs.AsNoTracking().OrderBy(job => job.CreatedUtc).ThenBy(job => job.Id).ToListAsync(cancellationToken))
                .Select(job => $"{job.Id} {job.Status} {job.ClientJobId} {job.Error} {job.StatusChangedUtc:O}"));
            auditEvents = await db.AuditEvents.CountAsync(cancellationToken);
        });
        return (jobs, auditEvents, _factory.TorznabHttp.Requests.Count, _factory.QBittorrentHttp.Requests.Count);
    }

    private async Task AssertUnchangedAsync((string Jobs, int AuditEvents, int FeedRequests, int ClientRequests) before, CancellationToken cancellationToken) =>
        Assert.Equal(before, await SnapshotAsync(cancellationToken));

    /// <summary>A 404 with nothing in it: the same answer a job that never was gets (house rule 6).</summary>
    private static async Task AssertBare404Async(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
