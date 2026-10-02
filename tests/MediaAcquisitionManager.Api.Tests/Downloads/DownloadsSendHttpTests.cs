using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-13 — sending a release to a download client when it works: the 201 with the job the client took, the
// job row and its audit row written before anything was sent, and what went over the wire — the feed's
// download fetched once with its key put back (or nothing fetched for a magnet), then qBittorrent asked for
// the category and whether it already has the torrent, then ONE torrents/add carrying the release, the
// destination's category exactly as stored and autoTMM=true, and never a save path (decision 2). How the
// job is named by the client: 5.2's id, a lookup by info hash before 5.2 (once more when the torrent is
// listed late), no id when it is never listed, NeedsLinking when there is no hash to look for. Who may send
// it: every signed-in user (decision 1). Failures are DownloadsSendFailuresHttpTests; repeats and decision 3
// are DownloadsIdempotencyHttpTests.
public sealed class DownloadsSendHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    /// <summary>The v1 info hash of <c>TorznabSamples.TorrentFile()</c>, what the feed hands over unless a
    /// test changes it — and the id qBittorrent names its torrent by.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    /// <summary>The title <c>ReleaseSeeder.CreateFound</c> gives a release.</summary>
    private const string SampleTitle = "Interstellar (2014) BDRip 1080p";

    private const string MagnetHash = "c9e15763f722f23e98a29decdfae341b98d53056";

    /// <summary>A magnet whose tracker carries a passkey, the way a private tracker's do.</summary>
    private const string Magnet =
        "magnet:?xt=urn:btih:" + MagnetHash + "&dn=Interstellar.2014.1080p&tr=http%3A%2F%2Ftracker.test%2Fannounce%2Fplanted-passkey-0001";

    /// <summary>The info dictionary of a hybrid torrent — v1 pieces and meta version 2 (BEP 52) — and its
    /// two hashes, as the core's reader tests compute them.</summary>
    private const string HybridInfo =
        "d9:file treed5:x.mkvd0:d6:lengthi1e11:pieces root32:rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrreee6:lengthi1e12:meta versioni2e4:name5:x.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaae";

    private const string HybridV1Hash = "aaeeecd05bc4058a5dd980de3cd8c7c1794034bc";
    private const string HybridV2Hash = "b29a07ddcde6736c607836fc557abceaaaf64a860607c374fbc2d9de02ffc274";

    // qBittorrent's requests as the fake records them.
    private const string SignIn = "POST /api/v2/auth/login";
    private const string Version = "GET /api/v2/app/version";
    private const string Categories = "GET /api/v2/torrents/categories";
    private const string Add = "POST /api/v2/torrents/add";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsSendHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey);
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_is_201_with_the_job_the_client_took_its_row_and_its_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var requestId = Guid.NewGuid();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(requestId, release.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        // qBittorrent 5.2 names the torrent it added: its v1 info hash.
        Assert.Equivalent(
            new DownloadJobDto(
                job.Id, release.Id, SampleTitle, "Toloka", "qBittorrent", "Movies", "Movies", DownloadJobStatus.Submitted, null, SampleInfoHash, null, null, now, now),
            job,
            strict: true);
        Assert.Equal($"/api/downloads/{job.Id}", response.Headers.Location?.AbsolutePath);
        var read = await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equivalent(job, await ReadJobAsync(read, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(job.Id, row.Id);
            Assert.Equal(requestId, row.RequestId);
            Assert.Equal(_owner.User.Id, row.RequestedByUserId);
            Assert.Equal("owner", row.RequestedByUserName);
            Assert.Equal(release.Id, row.ReleaseId);
            Assert.Equal(_client.Id, row.DownloadClientConnectionId);
            Assert.Equal("qBittorrent", row.DownloadClientName);
            Assert.Equal(_movies.Id, row.DestinationMappingId);
            Assert.Equal("Movies", row.DestinationName);
            Assert.Equal("Movies", row.ClientDestinationKey);
            Assert.Equal(DownloadJobStatus.Submitted, row.Status);
            Assert.Equal(SampleInfoHash, row.ClientJobId);
            Assert.Equal(SampleInfoHash, row.InfoHashV1);
            Assert.Null(row.InfoHashV2);
            Assert.Null(row.Error);
            Assert.Equal(now, row.CreatedUtc);
            Assert.Equal(now, row.StatusChangedUtc);

            // One row per request, whatever the sending made of it: who sent which release.
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DownloadRequested, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.DownloadJob, audit.TargetKind);
            Assert.Equal(job.Id, audit.TargetId);
            Assert.Equal(SampleTitle, audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Create_sends_the_torrent_once_into_the_category_with_automatic_management_and_no_save_path()
    {
        //Arrange
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Signed in once; the category confirmed; asked whether it already has the torrent (decision 4);
        // then the one add, each call on a session a version read has just confirmed — and nothing after the
        // add, which named the torrent.
        Assert.Equal(CallsThroughTheAdd(Lookup(SampleInfoHash)), QBittorrentCalls());
        var add = Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal(["torrents", "category", "autoTMM"], add.FieldNames);
        Assert.Equal(ReleasePayloadKind.TorrentFile, add.Kind);
        Assert.Equal(TorznabSamples.TorrentFile(), add.TorrentFile);
        Assert.Equal("release.torrent", add.TorrentFileName);
        Assert.Equal("application/x-bittorrent", add.TorrentContentType);
        Assert.Equal("Movies", add.Category);
        Assert.Equal("true", add.AutoTmm);
        Assert.Null(add.Urls);
        // No save path in any request, nor any other field that could place it: the category's folder decides.
        Assert.All(_factory.QBittorrentHttp.Requests, request =>
        {
            foreach (var field in new[] { "savepath", "save_path", "downloadPath", "useDownloadPath", "rename", "paused", "stopped", "tags" })
            {
                Assert.DoesNotContain(field, request.Uri.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(field, request.Body ?? "", StringComparison.OrdinalIgnoreCase);
            }
        });
        // Where it landed: the category's own folder, under automatic torrent management.
        var torrent = Assert.Single(_qbittorrent.Torrents);
        Assert.Equal(SampleInfoHash, torrent.Hash);
        Assert.Equal("Movies", torrent.Category);
        Assert.True(torrent.AutoTmm);
        Assert.Equal("/downloads/movies", torrent.SavePath);
    }

    [Fact]
    public async Task Create_fetches_the_release_from_its_feed_once_with_the_api_key_put_back()
    {
        //Arrange
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The stored link carries the key's placeholder (ADR 0004); the fetch puts the feed's key back.
        var fetch = Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Equal(HttpMethod.Get, fetch.Method);
        Assert.Equal($"http://prowlarr.test:9696/1/download?apikey={ApiKey}&link={ReleaseSeeder.PlantedLinkToken}", fetch.Uri.AbsoluteUri);
        Assert.Null(fetch.Body);
    }

    [Fact]
    public async Task Create_of_a_magnet_release_sends_the_link_as_urls_and_fetches_nothing_from_the_feed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka, locator: Magnet, payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(MagnetHash, job.ClientJobId);
        // The magnet is the payload itself: the feed is never asked.
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Equal(CallsThroughTheAdd(Lookup(MagnetHash)), QBittorrentCalls());
        var add = Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal(["urls", "category", "autoTMM"], add.FieldNames);
        Assert.Equal(ReleasePayloadKind.Magnet, add.Kind);
        Assert.Equal(Magnet, add.Urls);
        Assert.Null(add.TorrentFile);
        Assert.Equal("Movies", add.Category);
        Assert.Equal("true", add.AutoTmm);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(MagnetHash, row.InfoHashV1);
            Assert.Null(row.InfoHashV2);
        });
    }

    [Fact]
    public async Task Create_against_qbittorrent_before_5_2_finds_the_job_by_its_info_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // 4.4.1 to 5.1 answer the add "Ok." and name no torrent.
        _qbittorrent.Version = "v5.1.4";
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(SampleInfoHash, job.ClientJobId);
        Assert.Null(job.Error);
        // Found by the hash worked out from the .torrent, with one lookup after the add.
        Assert.Equal([.. CallsThroughTheAdd(Lookup(SampleInfoHash)), Version, Lookup(SampleInfoHash)], QBittorrentCalls());
        Assert.Single(_qbittorrent.AddRequests);
        await AssertJobRowAsync(DownloadJobStatus.Submitted, SampleInfoHash, SampleInfoHash, cancellationToken);
    }

    [Fact]
    public async Task Create_against_qbittorrent_before_5_2_finds_a_torrent_listed_late_on_the_second_look()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.Version = "v5.1.4";
        // Listed only once libtorrent has loaded it, a moment after the add was answered.
        _qbittorrent.AddedTorrentsUnlistedForLookups = 1;
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(SampleInfoHash, job.ClientJobId);
        Assert.Equal(
            [.. CallsThroughTheAdd(Lookup(SampleInfoHash)), Version, Lookup(SampleInfoHash), Version, Lookup(SampleInfoHash)],
            QBittorrentCalls());
        Assert.Single(_qbittorrent.AddRequests);
        await AssertJobRowAsync(DownloadJobStatus.Submitted, SampleInfoHash, SampleInfoHash, cancellationToken);
    }

    [Fact]
    public async Task Create_against_qbittorrent_before_5_2_that_never_lists_the_torrent_records_it_submitted_without_an_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.Version = "v5.1.4";
        _qbittorrent.AddedTorrentsUnlistedForLookups = 3;
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // qBittorrent said it took it: taken, with its info hash kept for the poller to find it by (MAM-14).
        // Looked for twice after the add, never more, and never sent again.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Null(job.ClientJobId);
        Assert.Null(job.Error);
        Assert.Equal(
            [.. CallsThroughTheAdd(Lookup(SampleInfoHash)), Version, Lookup(SampleInfoHash), Version, Lookup(SampleInfoHash)],
            QBittorrentCalls());
        Assert.Single(_qbittorrent.AddRequests);
        await AssertJobRowAsync(DownloadJobStatus.Submitted, null, SampleInfoHash, cancellationToken);
    }

    [Fact]
    public async Task Create_against_qbittorrent_before_5_2_whose_list_fails_after_the_add_records_it_submitted_without_an_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.Version = "v5.1.4";
        // The lookup before the add answers; the one after it fails.
        _qbittorrent.Intercept = request =>
            request.RequestUri!.AbsolutePath.EndsWith("/torrents/info", StringComparison.Ordinal) && _qbittorrent.AddRequests.Count > 0
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : null;
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // The client said it took it, and a list it couldn't give doesn't take that back.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Null(job.ClientJobId);
        Assert.Null(job.Error);
        Assert.Equal([.. CallsThroughTheAdd(Lookup(SampleInfoHash)), Version, Lookup(SampleInfoHash)], QBittorrentCalls());
        Assert.Single(_qbittorrent.Torrents);
        await AssertJobRowAsync(DownloadJobStatus.Submitted, null, SampleInfoHash, cancellationToken);
    }

    [Fact]
    public async Task Create_whose_add_qbittorrent_accepted_as_pending_finds_the_job_by_its_info_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // 5.2 answers 202 for an add it hasn't finished: no id, and the torrent listed once it is loaded.
        _qbittorrent.Intercept = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/torrents/add", StringComparison.Ordinal))
                return null;
            _qbittorrent.AddTorrent(TorrentInfoHashReader.FromTorrentFile(TorznabSamples.TorrentFile()), "Interstellar", "Movies");
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        };
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(SampleInfoHash, job.ClientJobId);
        Assert.Equal([.. CallsThroughTheAdd(Lookup(SampleInfoHash)), Version, Lookup(SampleInfoHash)], QBittorrentCalls());
        Assert.Single(_factory.QBittorrentHttp.Requests, request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/v2/torrents/add");
        await AssertJobRowAsync(DownloadJobStatus.Submitted, SampleInfoHash, SampleInfoHash, cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_magnet_naming_two_torrents_that_the_client_took_needs_linking()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two different v1 topics: the app can't tell which torrent the link is, so it has no hash to look
        // the job up by — qBittorrent takes the link all the same.
        const string magnet =
            "magnet:?xt=urn:btih:" + MagnetHash + "&xt=urn:btih:" + SampleInfoHash + "&dn=Interstellar";
        _qbittorrent.Version = "v5.1.4";
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/torrents/add", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.", Encoding.UTF8, "text/plain") }
            : null;
        var release = await _releases.CreateFound(_owner.User, _toloka, locator: magnet, payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // Taken, but which of the client's downloads it became is picked by hand; nothing was looked up.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.NeedsLinking, job.Status);
        Assert.Null(job.ClientJobId);
        Assert.Null(job.Error);
        Assert.Equal([SignIn, Version, Categories, Version, Add], QBittorrentCalls());
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(DownloadJobStatus.NeedsLinking, row.Status);
            Assert.Null(row.InfoHashV1);
            Assert.Null(row.InfoHashV2);
            Assert.Null(row.ClientJobId);
        });
    }

    [Fact]
    public async Task Create_of_a_hybrid_torrent_records_both_info_hashes_and_the_id_qbittorrent_names_it_by()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _feed.TorrentFile = Encoding.ASCII.GetBytes("d8:announce19:http://tracker.test4:info" + HybridInfo + "e");
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // qBittorrent on libtorrent 2 names a hybrid by its v2 hash cut to 40 characters; the lookup asks for
        // the torrent both ways, and the job keeps both hashes, which the poller follows it by (MAM-14).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(HybridV2Hash[..40], job.ClientJobId);
        Assert.Equal(CallsThroughTheAdd(Lookup(HybridV1Hash, HybridV2Hash[..40])), QBittorrentCalls());
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(HybridV1Hash, row.InfoHashV1);
            Assert.Equal(HybridV2Hash, row.InfoHashV2);
            Assert.Equal(HybridV2Hash[..40], row.ClientJobId);
        });
    }

    [Fact]
    public async Task Create_sends_a_category_with_surrounding_spaces_exactly_as_the_client_spells_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddCategory("  Kids  ", "/downloads/kids");
        var kids = await _destinations.Create(_client, "Kids", "  Kids  ");
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await SendAsync(_owner, release, kids);

        //Assert
        // Trimmed, it would name a category qBittorrent hasn't got, which it would create in its default
        // folder.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal("Kids", job.DestinationName);
        Assert.Equal("  Kids  ", job.ClientDestinationKey);
        Assert.Equal("  Kids  ", Assert.Single(_qbittorrent.AddRequests).Category);
        var torrent = Assert.Single(_qbittorrent.Torrents);
        Assert.Equal("  Kids  ", torrent.Category);
        Assert.Equal("/downloads/kids", torrent.SavePath);
        await _factory.WithDbAsync(async db =>
            Assert.Equal("  Kids  ", (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).ClientDestinationKey));
    }

    [Fact]
    public async Task Create_by_a_member_is_201_with_their_job()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        // Every signed-in user may send a release, as every one may search (decision 1).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Single(_qbittorrent.AddRequests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(job.Id, row.Id);
            Assert.Equal(_member.User.Id, row.RequestedByUserId);
            Assert.Equal("member", row.RequestedByUserName);
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DownloadRequested, audit.Action);
            Assert.Equal(_member.User.Id, audit.ActorUserId);
            Assert.Equal("member", audit.ActorUserName);
            Assert.Equal(job.Id, audit.TargetId);
        });
    }

    [Fact]
    public async Task Create_records_its_audit_row_under_the_release_title_cut_to_200_characters()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var title = "Interstellar (2014) " + string.Concat(Enumerable.Repeat("BDRip 1080p Ukr Eng ", 20));
        var release = await _releases.CreateFound(_owner.User, _toloka, title: title);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // The audit row's column holds 200 characters, the last of them the ellipsis; the job keeps the title.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(title.Length > 200);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(title, job.ReleaseTitle);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(title[..199] + "…", audit.TargetName);
            Assert.Equal(200, audit.TargetName!.Length);
        });
    }

    [Fact]
    public async Task Create_logs_who_sent_the_release_and_how_its_job_ended()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        _factory.Logs.Clear();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message == $"owner sent release {release.Id} to integration {_client.Id} (qbittorrent) as download job {job.Id}.");
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message == $"Download job {job.Id} to integration {_client.Id} (qbittorrent) ended Submitted.");
    }

    [Fact]
    public async Task Create_of_a_release_a_search_found_sends_it_to_the_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var releases = await _factory.SearchReleasesAsync(_member.AccessToken, "Interstellar", cancellationToken);
        // One the feed links for download, not a magnet: the page sends what it lists.
        var found = releases.First(release => release.PayloadKind is null);
        var searchRequests = _factory.TorznabHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SendDownloadRequest(Guid.NewGuid(), found.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(found.Id, job.ReleaseId);
        Assert.Equal(found.Title, job.ReleaseTitle);
        Assert.Equal("Toloka", job.SourceName);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(SampleInfoHash, job.ClientJobId);
        // The listing's own link, fetched once from the feed with the feed's key.
        var fetch = Assert.Single(_factory.TorznabHttp.Requests.Skip(searchRequests));
        Assert.Equal("/1/download", fetch.Uri.AbsolutePath);
        Assert.Contains($"apikey={ApiKey}", fetch.Uri.Query, StringComparison.Ordinal);
        Assert.Equal(CallsThroughTheAdd(Lookup(SampleInfoHash)), QBittorrentCalls());
        Assert.Equal(TorznabSamples.TorrentFile(), Assert.Single(_qbittorrent.AddRequests).TorrentFile);
    }

    [Fact]
    public async Task Create_without_a_token_is_401_and_writes_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sends <paramref name="release"/> as <paramref name="user"/> to the qBittorrent, into
    /// <paramref name="destination"/> or Movies, under a request id of its own.</summary>
    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release, DestinationMappingEntity? destination = null) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, (destination ?? _movies).Id));

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>Every request qBittorrent received, as method and path with its query, in order.</summary>
    private List<string> QBittorrentCalls() => [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The job lookup for <paramref name="torrentIds"/>, joined by <c>|</c> and escaped as the
    /// adapter sends them.</summary>
    private static string Lookup(params string[] torrentIds) =>
        $"GET /api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join('|', torrentIds))}";

    /// <summary>A send's calls up to the one add: the sign-in, the category's confirmation, whether the
    /// client already has the torrent (<paramref name="lookup"/>), then the add — each call after a version
    /// read that confirms the session.</summary>
    private static List<string> CallsThroughTheAdd(string lookup) => [SignIn, Version, Categories, Version, lookup, Version, Add];

    private Task AssertJobRowAsync(DownloadJobStatus status, string? clientJobId, string infoHashV1, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(status, row.Status);
            Assert.Equal(clientJobId, row.ClientJobId);
            Assert.Equal(infoHashV1, row.InfoHashV1);
            Assert.Null(row.Error);
        });
}
