using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-14 — cancelling only stops following (ADR 0014): 200 with the job Cancelled, its reason gone, its row and
// one audit row; not a single request to qBittorrent, which keeps the torrent as it was, and no later pass asks
// about the job — every request after the send is the sign-in or a GET. Any followed job and one needing linking
// can be cancelled — a Created one only once its send counts as interrupted, five minutes on; before that it is
// 409 with why, as is one that already ended, changing nothing; another user's job — the installation owner's
// reach included — and one that never was are a bare 404. A pass racing a cancel never undoes it nor stores the
// files it read, and a pass that moved the job on before the cancel's write doesn't stop it. Cancelled, its
// release can be sent again — qBittorrent's own "already has this torrent" still stopping a second copy while it
// does.
public sealed class DownloadsCancelHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string ReleaseTitle = "Interstellar (2014) BDRip 1080p";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>, which the feed's download links
    /// answer: qBittorrent's id for the torrent a send adds.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    // Spelled out rather than read from the product code: these are what the Jobs page shows.
    private const string AlreadyDownloadedError = "This download is already finished, so there's nothing to stop following.";
    private const string AlreadyFailedError = "This download has already failed, so there's nothing to stop following.";
    private const string AlreadyCancelledError = "This download is already cancelled, so there's nothing to stop following.";
    private const string StillBeingSentError = "This download is still being sent, so it can't be cancelled yet. Try again in a few minutes.";
    private const string AlreadyHasError = "“qBittorrent” already has this torrent, so nothing was sent.";
    private const string MissingReason =
        "“qBittorrent” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsCancelHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await integrations.Create(
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
    public async Task Cancel_is_200_with_the_job_cancelled_its_row_and_its_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Followed, and showing why it isn't moving on.
        var job = await PlantAsync(_member, DownloadJobStatus.Downloading, progress: 0.42, blockReason: MissingReason);
        var before = await ReadAsync(_member, job.Id, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(3));
        var cancelledUtc = _factory.Time.GetUtcNow().UtcDateTime;
        _factory.Logs.Clear();

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken);

        //Assert
        // The job as it was, Cancelled now and with nothing left to block it; its progress kept as history.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equivalent(
            before with { Status = DownloadJobStatus.Cancelled, BlockReason = null, StatusChangedUtc = cancelledUtc },
            cancelled,
            strict: true);
        Assert.Equivalent(cancelled, await ReadAsync(_member, job.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, cancellationToken);
            Assert.Equal(
                (DownloadJobStatus.Cancelled, (double?)0.42, (string?)null, (DownloadJobErrorOrigin?)null, cancelledUtc, SampleInfoHash),
                (row.Status, row.Progress, row.BlockReason, row.BlockReasonOrigin, row.StatusChangedUtc, row.ClientJobId));
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.DownloadCancelled, (Guid?)_member.User.Id, "member", (AuditTargetKind?)AuditTargetKind.DownloadJob, (Guid?)job.Id, ReleaseTitle, cancelledUtc),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
        var logged = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(DownloadService).FullName);
        Assert.Equal((LogLevel.Information, $"member stopped following download job {job.Id}."), (logged.Level, logged.Message));
    }

    [Fact]
    public async Task Cancel_sends_nothing_to_qBittorrent_and_no_later_pass_asks_about_the_job()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await SendAsync(_member, cancellationToken);
        var afterSend = _factory.QBittorrentHttp.Requests.Count;
        _qbittorrent.UpdateTorrent(SampleInfoHash, torrent => torrent with { State = "downloading", Progress = 0.3 });
        await PassAsync(cancellationToken);
        var torrentsBefore = _qbittorrent.Torrents;
        var beforeCancel = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{sent.Id}/cancel", _member.AccessToken);
        var duringCancel = _factory.QBittorrentHttp.Requests.Count - beforeCancel;
        await PassAsync(cancellationToken);
        await PassAsync(cancellationToken);

        //Assert
        // The cancel asked nothing of qBittorrent, nor did any pass after it; qBittorrent keeps the torrent and its
        // files as they were. Every request since the send is the sign-in or a read: there is no member of the
        // download client contract that could remove, stop or pause anything (DownloadClientNeverRemovesTests).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, duringCancel);
        Assert.Equal(beforeCancel, _factory.QBittorrentHttp.Requests.Count);
        Assert.Equal(Assert.Single(torrentsBefore), Assert.Single(_qbittorrent.Torrents));
        Assert.All(
            _factory.QBittorrentHttp.Requests.Skip(afterSend),
            request => Assert.True(
                request.Method == HttpMethod.Get || (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/v2/auth/login"),
                $"{request.Method} {request.Uri.PathAndQuery}"));
        Assert.DoesNotContain(
            _factory.QBittorrentHttp.Requests,
            request => new[] { "delete", "remove", "stop", "pause" }.Any(verb => request.Uri.AbsolutePath.Contains(verb, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(DownloadJobStatus.Cancelled, (await RowAsync(sent.Id, cancellationToken)).Status);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Created)]
    [InlineData(DownloadJobStatus.Submitted)]
    [InlineData(DownloadJobStatus.Downloading)]
    [InlineData(DownloadJobStatus.Paused)]
    // With no linking screen yet, cancelling is the one way to dismiss it, so its release can be sent again.
    [InlineData(DownloadJobStatus.NeedsLinking)]
    public async Task Cancel_of_a_job_still_followed_or_needing_linking_is_200(DownloadJobStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, status);
        // Old enough that a Created job is a send a stop interrupted, as the poller counts it: followed, so
        // cancellable. The other statuses are cancellable at any age.
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DownloadJobStatus.Cancelled, (await RowAsync(job.Id, cancellationToken)).Status);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(AuditAction.DownloadCancelled, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Downloaded, AlreadyDownloadedError)]
    [InlineData(DownloadJobStatus.Failed, AlreadyFailedError)]
    [InlineData(DownloadJobStatus.Cancelled, AlreadyCancelledError)]
    public async Task Cancel_of_a_job_that_already_ended_is_409_with_why_and_changes_nothing(DownloadJobStatus status, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, status);
        var before = await RowAsync(job.Id, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equivalent(before, await RowAsync(job.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Cancel_of_a_created_job_still_being_sent_is_409_with_why_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, DownloadJobStatus.Created);
        // A second short of an interrupted send: as far as anyone can tell, its DownloadSender is still working,
        // and its outcome is written only on a job still Created.
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge - TimeSpan.FromSeconds(1));
        var before = await RowAsync(job.Id, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken);

        //Assert
        // Not yet, and nothing recorded or sent: the send settles it, or a cancel once it counts as interrupted.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(StillBeingSentError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equivalent(before, await RowAsync(job.Id, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Cancel_twice_is_409_the_second_time_with_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, DownloadJobStatus.Downloading);
        var first = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken);

        //Act
        var again = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken);

        //Assert
        // A double click: the first stopped following, the second has nothing to stop and records nothing.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(AlreadyCancelledError, await again.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Cancel_of_another_users_job_is_a_bare_404_even_for_the_installation_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, DownloadJobStatus.Downloading);
        var other = await new UserSeeder(_factory.Scope).CreateAuthorized("other");
        // An older build's job, or one whose sender's account was removed: nobody's to cancel.
        var nobodys = await Jobs().Create((await _releases.CreateFound(_member.User, _toloka)).Id, _client.Id, _client.Name, _movies.Id);

        //Act
        var forOwner = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _owner.AccessToken);
        var forOther = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", other.AccessToken);
        var ofNobodys = await _factory.PostHttpRequest($"{BaseUrl}/{nobodys.Id}/cancel", _owner.AccessToken);

        //Assert
        // MAM-14's decision 1: a job stays its sender's.
        await AssertBare404Async(forOwner, cancellationToken);
        await AssertBare404Async(forOther, cancellationToken);
        await AssertBare404Async(ofNobodys, cancellationToken);
        Assert.Equal(DownloadJobStatus.Downloading, (await RowAsync(job.Id, cancellationToken)).Status);
        Assert.Equal(DownloadJobStatus.Submitted, (await RowAsync(nobodys.Id, cancellationToken)).Status);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Cancel_of_a_job_that_never_was_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/cancel", _member.AccessToken);

        //Assert
        await AssertBare404Async(response, cancellationToken);
    }

    [Fact]
    public async Task Cancel_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, DownloadJobStatus.Downloading);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(DownloadJobStatus.Downloading, (await RowAsync(job.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Poll_racing_a_cancel_never_undoes_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await SendAsync(_member, cancellationToken);
        _qbittorrent.UpdateTorrent(SampleInfoHash, torrent => torrent with { State = "downloading", Progress = 0.3, Files = [new("Interstellar.mkv", 1_000, 0.3)] });
        HttpResponseMessage? cancel = null;
        // The pass has read the job Submitted and its files, and is about to write both; the cancel lands first.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true, async _ => cancel = await _factory.PostHttpRequest($"{BaseUrl}/{sent.Id}/cancel", _member.AccessToken));
        var before = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // The pass's write was made against Submitted, which the job no longer had: it changed nothing, and the
        // files it had read went with it.
        Assert.Equal(HttpStatusCode.OK, cancel?.StatusCode);
        var row = await RowAsync(sent.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Cancelled, (double?)null), (row.Status, row.Progress));
        Assert.Contains(_factory.QBittorrentHttp.Requests.Skip(before), request => request.Uri.AbsolutePath == "/api/v2/torrents/files");
        Assert.Equal(0, await FileCountAsync(sent.Id, cancellationToken));
    }

    [Theory]
    // Moved on: the pass's write of the job carries the files.
    [InlineData(0.4)]
    // Nothing moved, but no files stored yet: the pass claims the job for the files alone.
    [InlineData(0.3)]
    public async Task Poll_racing_a_cancel_while_it_reads_the_files_writes_neither_the_job_nor_its_files(double clientProgress)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, DownloadJobStatus.Downloading, progress: 0.3);
        _qbittorrent.AddTorrent(
            FakeQBittorrentTorrent.Of(new TorrentInfoHashes(SampleInfoHash, null), ReleaseTitle, "Movies", "downloading", clientProgress) with
            {
                Files = [new("Interstellar.mkv", 1_000, clientProgress)],
            });
        HttpResponseMessage? cancel = null;
        // The cancel lands while qBittorrent is answering the pass's files read, which can take up to 15 s.
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/torrents/files" && cancel is null)
                cancel = _factory.PostHttpRequest($"{BaseUrl}/{job.Id}/cancel", _member.AccessToken).GetAwaiter().GetResult();
            return null;
        };

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Cancelled first, so the pass wrote nothing it read: not the job's progress, not the files qBittorrent
        // listed after the cancel had been made.
        Assert.Equal(HttpStatusCode.OK, cancel?.StatusCode);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Cancelled, (double?)0.3), (row.Status, row.Progress));
        Assert.Equal(0, await FileCountAsync(job.Id, cancellationToken));
    }

    [Fact]
    public async Task Cancel_of_a_job_a_pass_moved_on_since_it_was_read_still_cancels_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await SendAsync(_member, cancellationToken);
        _qbittorrent.UpdateTorrent(SampleInfoHash, torrent => torrent with { State = "downloading", Progress = 0.3 });
        // The cancel has read the job Submitted; before its write, a pass moves it to Downloading.
        _factory.TransactionHook.BeforeNextTransaction(_ => true, PassAsync);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{sent.Id}/cancel", _member.AccessToken);

        //Assert
        // Still cancellable, so still cancelled: the cancel's write asks only that.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RowAsync(sent.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Cancelled, (double?)0.3), (row.Status, row.Progress));
        await _factory.WithDbAsync(async db => Assert.Equal(
            [AuditAction.DownloadRequested, AuditAction.DownloadCancelled],
            await db.AuditEvents.AsNoTracking().OrderBy(audit => audit.CreatedUtc).Select(audit => audit.Action).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Cancel_lets_the_release_be_sent_again_once_qBittorrent_no_longer_has_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var sent = await SendAsync(_member, release, cancellationToken);
        var refusedWhileFollowed = await PostSendAsync(_member, release);
        var cancelled = await _factory.PostHttpRequest($"{BaseUrl}/{sent.Id}/cancel", _member.AccessToken);
        // Cancelled, but qBittorrent still has it: its own check stops a second copy (MAM-13's decision 4).
        var stillInQBittorrent = await PostSendAsync(_member, release);
        // Removed in qBittorrent.
        _qbittorrent.RemoveTorrent(SampleInfoHash);

        //Act
        var response = await PostSendAsync(_member, release);

        //Assert
        // MAM-14's decision 3: cancelling is how a release is sent again; while the job was followed it counted as
        // sent.
        Assert.Equal(HttpStatusCode.Conflict, refusedWhileFollowed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.Created, stillInQBittorrent.StatusCode);
        var failed = (await stillInQBittorrent.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, AlreadyHasError), (failed.Status, failed.Error));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Submitted, SampleInfoHash), (job.Status, job.ClientJobId));
        Assert.Equal(2, _qbittorrent.AddRequests.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One pass of the app's tracker, which must not have thrown: a pass that threw is logged and
    /// would otherwise read as one that changed nothing.</summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.PollDownloadJobsAsync(cancellationToken);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    private async Task<DownloadJobDto> SendAsync(AuthorizedUser user, CancellationToken cancellationToken) =>
        await SendAsync(user, await _releases.CreateFound(user.User, _toloka, ReleaseTitle), cancellationToken);

    /// <summary>Sends <paramref name="release"/> as <paramref name="user"/> to qBittorrent's Movies, the way the
    /// Download dialog does; throws unless a job was made.</summary>
    private async Task<DownloadJobDto> SendAsync(AuthorizedUser user, ReleaseEntity release, CancellationToken cancellationToken)
    {
        var response = await PostSendAsync(user, release);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The send answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    // A new request each time: a click of Download, not a retry.
    private Task<HttpResponseMessage> PostSendAsync(AuthorizedUser user, ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

    /// <summary>A job of <paramref name="user"/>'s left <paramref name="status"/>, with qBittorrent's id for it — no
    /// torrent in qBittorrent, which a cancel never asks.</summary>
    private async Task<DownloadJobEntity> PlantAsync(
        AuthorizedUser user, DownloadJobStatus status, double? progress = null, string? blockReason = null)
    {
        var release = await _releases.CreateFound(user.User, _toloka, ReleaseTitle);
        return await Jobs().Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            status,
            user.User.Id,
            infoHashV1: SampleInfoHash,
            clientJobId: SampleInfoHash,
            progress: progress,
            blockReason: blockReason,
            blockReasonOrigin: blockReason is null ? null : DownloadJobErrorOrigin.App);
    }

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private async Task<DownloadJobEntity> RowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    private async Task<int> FileCountAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var count = 0;
        await _factory.WithDbAsync(async db => count = await db.DownloadJobFiles.CountAsync(file => file.DownloadJobId == jobId, cancellationToken));
        return count;
    }

    private async Task<DownloadJobDto> ReadAsync(AuthorizedUser user, Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>A 404 with nothing in it: the same answer a job that never was gets (house rule 6).</summary>
    private static async Task AssertBare404Async(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
