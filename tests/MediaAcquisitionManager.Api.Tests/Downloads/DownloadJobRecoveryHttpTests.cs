using System.Globalization;
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
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-14 — a restart loses nothing (ADR 0014): the tracker keeps nothing in memory, so the rows a previous run
// left — Submitted, Downloading, Paused — are followed by the first pass after it, and ended ones are never asked
// about; a job qBittorrent took without an id gets it once listed. ADR 0013's gaps, closed: a send a stop left
// Created is the send's own for five minutes and then the poller's — followed when qBittorrent has it, needing
// linking when it lists two, Failed when it lists none or the job never learnt what it sent, and Created with
// the reason while qBittorrent can't be asked — its lookup, or its destination's listing alone; and a hybrid
// torrent qBittorrent renamed after its magnet is found again through its destination's listing, its new id and
// v2 hash kept from then on.
public sealed class DownloadJobRecoveryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>, which the feed's download links
    /// answer: qBittorrent's id for the torrent a send adds.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    /// <summary>A hybrid torrent's two hashes, its magnet naming only the first.</summary>
    private const string HybridV1 = "aaeeecd05bc4058a5dd980de3cd8c7c1794034bc";
    private const string HybridV2 = "b29a07ddcde6736c607836fc557abceaaaf64a860607c374fbc2d9de02ffc274";

    private const string SignIn = "POST /api/v2/auth/login";
    private const string Version = "GET /api/v2/app/version";
    private const string MoviesListing = "GET /api/v2/torrents/info?category=Movies";

    // Spelled out rather than read from the product code: these are what the Jobs page shows.
    private const string InterruptedNotListedError =
        "The app stopped while sending this download, and “qBittorrent” doesn't list it, so it isn't followed. Send the release again if you still want it.";
    private const string InterruptedBeforeHashesError =
        "The app stopped while sending this download, before it could tell whether “qBittorrent” took it, so it isn't followed. Check “qBittorrent” before sending the release again.";
    private const string UnreachableError =
        "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
    private const string ServiceError = "qBittorrent answered 500 Internal Server Error. Its own log says why.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadJobRecoveryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        // The owner sends: a client's own sentence is the owner's to read (DownloadJobMissingHttpTests pins a
        // member's view).
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
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
    public async Task Poll_after_a_restart_follows_the_jobs_a_previous_run_left_and_asks_about_no_ended_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // What the previous run left: three jobs it followed — the torrents moved on in qBittorrent meanwhile —
        // and one of each status that ended.
        var submitted = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.4);
        var downloading = await PlantAsync(DownloadJobStatus.Downloading, Hash(2), "stalledUP", 1, storedProgress: 0.6);
        var paused = await PlantAsync(DownloadJobStatus.Paused, Hash(3), "stoppedDL", 0.2, storedProgress: 0.2);
        await PlantAsync(DownloadJobStatus.Downloaded, Hash(4), "stalledUP", 1, storedProgress: 1);
        await PlantAsync(DownloadJobStatus.Failed, Hash(5), "stalledUP", 1);
        await PlantAsync(DownloadJobStatus.NeedsLinking, Hash(6), "stalledUP", 1);
        await PlantAsync(DownloadJobStatus.Cancelled, Hash(7), "downloading", 0.5);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // A fresh session, one lookup of the followed three only, and the files of each — none stored yet.
        Assert.Equal(
            [SignIn, Version, Lookup(Hash(1), Hash(2), Hash(3)), Version, Files(Hash(1)), Version, Files(Hash(2)), Version, Files(Hash(3))],
            QBittorrentCalls());
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.4), await StatusAsync(submitted.Id, cancellationToken));
        Assert.Equal((DownloadJobStatus.Downloaded, (double?)1), await StatusAsync(downloading.Id, cancellationToken));
        Assert.Equal((DownloadJobStatus.Paused, (double?)0.2), await StatusAsync(paused.Id, cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(
            [DownloadJobStatus.Downloaded, DownloadJobStatus.Failed, DownloadJobStatus.NeedsLinking, DownloadJobStatus.Cancelled],
            await db.DownloadJobs.AsNoTracking().OrderBy(job => job.CreatedUtc).Skip(3).Select(job => job.Status).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Poll_finds_and_keeps_the_id_of_a_job_qBittorrent_took_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A qBittorrent before 5.2 answers the add without an id and lists the torrent only a moment later: the
        // send's two looks miss it, and so does the first pass — its lookup and its look among the Movies
        // torrents — inside the listing grace.
        _qbittorrent.Version = "v5.1.4";
        _qbittorrent.AddedTorrentsUnlistedForLookups = 4;
        var sent = await SendAsync(await _releases.CreateFound(_owner.User, _toloka), cancellationToken);
        await PassAsync(cancellationToken);
        var unlisted = await RowAsync(sent.Id, cancellationToken);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Listed now: the job keeps qBittorrent's id from here on, and takes what qBittorrent says.
        Assert.Equal((DownloadJobStatus.Submitted, (string?)null, (string?)null), (sent.Status, sent.ClientJobId, unlisted.ClientJobId));
        Assert.Null(unlisted.BlockReason);
        var row = await RowAsync(sent.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Downloading, SampleInfoHash, (double?)0, (string?)null),
            (row.Status, row.ClientJobId, row.Progress, TestTexts.En(row.BlockReason)));
        var read = await ReadAsync(sent.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, SampleInfoHash), (read.Status, read.ClientJobId));
    }

    [Fact]
    public async Task Poll_leaves_a_created_job_younger_than_five_minutes_to_its_send()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Created, Hash(1), "downloading", 0.3, clientJobId: null);
        // A send still under way, as far as anyone can tell.
        AgeTo(job, DownloadJobPoller.InterruptedSendAge - TimeSpan.FromSeconds(1));
        var before = await RowAsync(job.Id, cancellationToken);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Not read, not asked about: the poller never races a DownloadSender.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Equivalent(before, await RowAsync(job.Id, cancellationToken), strict: true);
    }

    [Theory]
    [InlineData("queuedDL", 0, DownloadJobStatus.Submitted)]
    [InlineData("downloading", 0.2, DownloadJobStatus.Downloading)]
    [InlineData("stalledUP", 1, DownloadJobStatus.Downloaded)]
    [InlineData("moving", 1, DownloadJobStatus.Submitted)]
    public async Task Poll_of_an_interrupted_send_qBittorrent_took_follows_it_from_then_on(
        string state, double progress, DownloadJobStatus expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A stop came after the hashes were recorded and before the outcome was: Created, no id.
        var job = await PlantAsync(DownloadJobStatus.Created, Hash(1), state, progress, clientJobId: null);
        AgeTo(job, DownloadJobPoller.InterruptedSendAge);
        var passUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Five minutes old is old enough. qBittorrent has it, so it was taken — at least Submitted — with the id
        // qBittorrent lists, and followed from here.
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (expected, Hash(1), passUtc, (string?)null, (string?)null),
            (row.Status, row.ClientJobId, row.StatusChangedUtc, TestTexts.En(row.Error), TestTexts.En(row.BlockReason)));
    }

    [Fact]
    public async Task Poll_of_an_interrupted_send_qBittorrent_lists_twice_needs_linking()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The job knows both hashes of a hybrid; qBittorrent has a v1 torrent named by the one and a v2 torrent
        // holding the other — two downloads, either of which it could be.
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var job = await Jobs().Create(
            release.Id, _client.Id, _client.Name, _movies.Id, DownloadJobStatus.Created, _owner.User.Id, infoHashV1: HybridV1, infoHashV2: HybridV2);
        _qbittorrent.AddTorrent(new TorrentInfoHashes(HybridV1, null), "v1 copy", "Movies");
        _qbittorrent.AddTorrent(new TorrentInfoHashes(null, HybridV2), "v2 copy", "Movies");
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Picked by hand, as a send that can't tell is (MAM-13): no id guessed, nothing to show as blocking it.
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.NeedsLinking, (string?)null, (string?)null, (string?)null),
            (row.Status, row.ClientJobId, TestTexts.En(row.Error), TestTexts.En(row.BlockReason)));
    }

    [Fact]
    public async Task Poll_of_an_interrupted_send_qBittorrent_does_not_list_fails_with_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Created, Hash(1), "downloading", 0.2, clientJobId: null);
        _qbittorrent.RemoveTorrent(Hash(1));
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Looked for by its hash, then — sent with its v1 hash alone — among its destination's torrents, in case
        // qBittorrent renamed it; in neither, it ends Failed with the app's sentence, which its sender reads.
        Assert.Equal([SignIn, Version, Lookup(Hash(1)), Version, MoviesListing], QBittorrentCalls());
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Failed, InterruptedNotListedError, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App, (string?)null),
            (row.Status, TestTexts.En(row.Error), row.ErrorOrigin, TestTexts.En(row.BlockReason)));
        Assert.Equal(InterruptedNotListedError, (await ReadAsync(job.Id, cancellationToken)).Error);
    }

    [Fact]
    public async Task Poll_of_an_interrupted_send_that_never_learnt_what_it_sent_fails_without_asking_qBittorrent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Stopped before the payload was even fetched: no hash to look for.
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var job = await Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, DownloadJobStatus.Created, _owner.User.Id);
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge);
        var passUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Failed, InterruptedBeforeHashesError, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App, passUtc),
            (row.Status, TestTexts.En(row.Error), row.ErrorOrigin, row.StatusChangedUtc));
    }

    [Fact]
    public async Task Poll_of_an_interrupted_send_whose_client_can_not_be_asked_stays_created_with_why_until_it_can()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Created, Hash(1), "downloading", 0.2, clientJobId: null);
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge);
        _qbittorrent.Unreachable = true;
        await PassAsync(cancellationToken);
        var waiting = await RowAsync(job.Id, cancellationToken);
        _qbittorrent.Unreachable = false;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Nothing decided while qBittorrent couldn't say: Created, with qBittorrent's own sentence as why — then
        // followed, the reason gone, once it answers.
        Assert.Equal(
            (DownloadJobStatus.Created, UnreachableError, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.DownloadClient),
            (waiting.Status, TestTexts.En(waiting.BlockReason), waiting.BlockReasonOrigin));
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, Hash(1), (string?)null), (row.Status, row.ClientJobId, TestTexts.En(row.BlockReason)));
    }

    [Theory]
    [InlineData("failing", ServiceError)]
    [InlineData("unreachable", UnreachableError)]
    public async Task Poll_of_an_interrupted_send_whose_destination_listing_fails_stays_created_with_why_until_it_answers(
        string failure, string expectedReason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Sent with its v1 hash alone, and not listed by it: only its destination's listing could still find it.
        var job = await PlantAsync(DownloadJobStatus.Created, Hash(1), "downloading", 0.2, clientJobId: null);
        _qbittorrent.RemoveTorrent(Hash(1));
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge);
        // The lookup answers; the listing alone fails.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/info"
            && request.RequestUri.Query.StartsWith("?category=", StringComparison.Ordinal)
                ? failure == "failing"
                    ? VendorResponses.Text("Internal Server Error", HttpStatusCode.InternalServerError)
                    : throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it.")
                : null;
        await PassAsync(cancellationToken);
        var waiting = await RowAsync(job.Id, cancellationToken);
        var askedWhileFailing = QBittorrentCalls();
        _qbittorrent.Intercept = null;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // A listing that failed said nothing about the job: Created, with qBittorrent's own sentence — never Failed
        // as not listed. Once the listing answers, the pass decides as usual: in neither, it fails with why.
        Assert.Equal([SignIn, Version, Lookup(Hash(1)), Version, MoviesListing], askedWhileFailing);
        Assert.Equal(
            (DownloadJobStatus.Created, expectedReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.DownloadClient, (string?)null),
            (waiting.Status, TestTexts.En(waiting.BlockReason), waiting.BlockReasonOrigin, TestTexts.En(waiting.Error)));
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Failed, InterruptedNotListedError, (string?)null),
            (row.Status, TestTexts.En(row.Error), TestTexts.En(row.BlockReason)));
    }

    [Fact]
    public async Task Poll_finds_a_hybrid_torrent_qBittorrent_renamed_after_its_magnet_through_its_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A magnet naming only the v1 hash: qBittorrent takes it under that id ...
        var release = await _releases.CreateFound(
            _owner.User, _toloka, "Interstellar (2014) WEBRip", $"magnet:?xt=urn:btih:{HybridV1}&dn=Interstellar", payloadKind: ReleasePayloadKind.Magnet);
        var sent = await SendAsync(release, cancellationToken);
        // ... and, built on libtorrent 2, renames it to its v2 hash cut to 40 characters once the metadata arrives
        // (ADR 0013's gap): a lookup by the v1 hash no longer finds it.
        _qbittorrent.RenameAsHybrid(HybridV1, HybridV2);
        _qbittorrent.UpdateTorrent(HybridV2[..40], torrent => torrent with { State = "downloading", Progress = 0.1 });
        var before = _factory.QBittorrentHttp.Requests.Count;
        await PassAsync(cancellationToken);
        var renamedCalls = QBittorrentCalls()[before..];
        var found = await RowAsync(sent.Id, cancellationToken);
        _qbittorrent.UpdateTorrent(HybridV2[..40], torrent => torrent with { Progress = 0.3 });
        var afterFirst = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Found among the Movies torrents by the v1 hash qBittorrent still lists for it; the job keeps qBittorrent's
        // new id and the v2 hash from then on, so the next pass finds it by its ids alone.
        Assert.Equal((DownloadJobStatus.Submitted, HybridV1), (sent.Status, sent.ClientJobId));
        Assert.Equal([Version, Lookup(HybridV1), Version, MoviesListing, Version, Files(HybridV2[..40])], renamedCalls);
        Assert.Equal(
            (DownloadJobStatus.Downloading, HybridV2[..40], HybridV1, HybridV2, (double?)0.1),
            (found.Status, found.ClientJobId, found.InfoHashV1, found.InfoHashV2, found.Progress));
        Assert.DoesNotContain(MoviesListing, QBittorrentCalls()[afterFirst..]);
        var row = await RowAsync(sent.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, HybridV2[..40], (double?)0.3), (row.Status, row.ClientJobId, row.Progress));
        Assert.Equal(HybridV2[..40], (await ReadAsync(sent.Id, cancellationToken)).ClientJobId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One pass of the app's tracker, which must not have thrown: a pass that threw is logged and
    /// would otherwise read as one that changed nothing.</summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.PollDownloadJobsAsync(cancellationToken);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>Sends <paramref name="release"/> as the owner to qBittorrent's Movies, the way the Download dialog
    /// does.</summary>
    private async Task<DownloadJobDto> SendAsync(ReleaseEntity release, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The send answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>
    /// A job of the owner's a previous run left <paramref name="status"/>, sent with the v1 info hash
    /// <paramref name="hash"/> (qBittorrent's id for it, unless <paramref name="clientJobId"/> says the job never
    /// learnt it), and the torrent in qBittorrent in <paramref name="state"/> at <paramref name="progress"/>. A second
    /// later than the job before it, so the oldest-first order is the order they were planted in.
    /// </summary>
    private async Task<DownloadJobEntity> PlantAsync(
        DownloadJobStatus status, string hash, string state, double progress, double? storedProgress = null, string? clientJobId = "")
    {
        var release = await _releases.CreateFound(_owner.User, _toloka);
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), $"Torrent {hash[^4..]}", "Movies", state, progress));
        var job = await Jobs().Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            status,
            _owner.User.Id,
            infoHashV1: hash,
            clientJobId: clientJobId == "" ? hash : clientJobId,
            progress: storedProgress);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        return job;
    }

    /// <summary>Moves the clock on until <paramref name="job"/> is exactly <paramref name="age"/> old.</summary>
    private void AgeTo(DownloadJobEntity job, TimeSpan age) =>
        _factory.Time.Advance(job.CreatedUtc + age - _factory.Time.GetUtcNow().UtcDateTime);

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private async Task<DownloadJobEntity> RowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    private async Task<(DownloadJobStatus Status, double? Progress)> StatusAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var row = await RowAsync(jobId, cancellationToken);
        return (row.Status, row.Progress);
    }

    /// <summary>The job as the owner reads it over HTTP.</summary>
    private async Task<DownloadJobDto> ReadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>A v1 info hash of its own for each <paramref name="n"/>.</summary>
    private static string Hash(int n) => n.ToString("x40", CultureInfo.InvariantCulture);

    /// <summary>Every request qBittorrent received, as method and path with its query, in order.</summary>
    private List<string> QBittorrentCalls() => [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The job lookup for <paramref name="torrentIds"/>, joined by <c>|</c> and escaped as the adapter
    /// sends them.</summary>
    private static string Lookup(params string[] torrentIds) =>
        $"GET /api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join('|', torrentIds))}";

    private static string Files(string torrentId) => $"GET /api/v2/torrents/files?hash={torrentId}";
}
