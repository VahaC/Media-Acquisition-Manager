using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
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

// MAM-13 — a download is sent once (ADR 0013, house rule 8). The same request id from the same user answers
// the job it made — 200, nothing fetched or sent again, no second job or audit row — however it comes back:
// a repeated click, a retry, two copies at once (the unique index settles a race), after its job failed,
// after the release, the client or the destination changed or went. The same id asking for another
// download is 409, also when it loses the race to the save; a release, client or destination removed
// between the checks and the save is that row's 404, not a 500. Another user's same id is their own
// request. Decision 3: a release already sent to the client by a job that didn't fail — recognised by its
// source's id for it or by its info hash, whoever sent it — is 409 with nothing written or sent; a failed
// job, another client, or another source's listing with the same id doesn't count.
public sealed class DownloadsIdempotencyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    /// <summary>The v1 info hash of <c>TorznabSamples.TorrentFile()</c>, the .torrent the feed hands over.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    private const string ListedInfoHash = "c9e15763f722f23e98a29decdfae341b98d53056";
    private const string ListedV2InfoHash = "3d8c42fa5353329627f1a2afcba8bc04c6b5db714d3b07577c9624e6a4396bc2";
    private const string TolokaItemId = "https://toloka.to/t678901";

    // Spelled out rather than read from the product code: these are what the Download dialog shows.
    private const string RequestIdReusedError = "This request id was already used to send a different download. Reload the page and try again.";
    private const string AlreadySentError = "This release was already sent to “qBittorrent”, so it isn't sent twice.";
    private const string ReleaseMissingError = "This release isn't available any more: a search keeps what it found for a day. Search again.";
    private const string ClientGoneError = "The download client you picked no longer exists. Reload the page and pick one again.";
    private const string DestinationGoneError = "The destination you picked no longer exists. Reload the page and pick one again.";
    private const string CategoryGoneError =
        "“qBittorrent” has no category “Movies” any more, so nothing was sent: it would have created one in its default folder. Create the category again in “qBittorrent”, or the owner edits the destination.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IReleaseSeeder _releases = null!;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsIdempotencyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string> WaysToRecogniseTheRelease => new()
    {
        "another search's copy with the same source item id",
        "another user's copy with the same source item id",
        "the same listed info hash",
        "the v1 info hash the earlier job worked out",
        "the v2 info hash the earlier job worked out",
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.CreateAuthorized("member");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
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
    public async Task Create_twice_with_one_request_id_answers_the_same_job_and_sends_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id);

        //Act
        var first = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);
        var second = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // A repeated click: the job the first one made, as it stands — not a second job, and not refused as a
        // release already sent (the request id is looked up before anything else).
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var job = await ReadJobAsync(first, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equivalent(job, await ReadJobAsync(second, cancellationToken), strict: true);
        Assert.Null(second.Headers.Location);
        await AssertOneJobAndOneAuditRowAsync(job.Id, cancellationToken);
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Single(_factory.QBittorrentHttp.Requests, request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/v2/torrents/add");
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_twice_at_once_with_one_request_id_makes_one_job_and_sends_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id);
        HttpResponseMessage? competing = null;
        // Deterministic overlap: this copy found no job for its request id and passed every check, and is
        // about to save its job when the other copy is sent — and saved, and sent to the client — whole.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DownloadJobEntity>().Any(entry => entry.State == EntityState.Added),
            async _ => competing = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request));
        _factory.Logs.Clear();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // The unique index lets one job commit; the other copy answers that job, a 200 rather than a 500,
        // and its audit row rolled back with its insert.
        Assert.Equal(HttpStatusCode.Created, competing?.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(competing, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equivalent(job, await ReadJobAsync(response, cancellationToken), strict: true);
        await AssertOneJobAndOneAuditRowAsync(job.Id, cancellationToken);
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Single(_qbittorrent.AddRequests);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message == $"A concurrent copy of request {request.RequestId} made download job {job.Id} first; this one answered it.");
    }

    [Fact]
    public async Task Create_twice_in_parallel_with_one_request_id_makes_one_job_and_sends_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id);

        //Act
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request),
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request));

        //Assert
        // Whichever got there first made the job; the other answers it — while it is still being sent, or
        // after.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], responses.Select(response => response.StatusCode).Order());
        var jobs = await Task.WhenAll(responses.Select(response => ReadJobAsync(response, cancellationToken)));
        Assert.Equal(jobs[0].Id, jobs[1].Id);
        Assert.Equal(DownloadJobStatus.Submitted, jobs[Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.Created)].Status);
        await AssertOneJobAndOneAuditRowAsync(jobs[0].Id, cancellationToken);
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_whose_copy_wrote_its_job_after_the_lookup_answers_that_job_rather_than_already_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id);
        HttpResponseMessage? competing = null;
        // Deterministic overlap: the other copy of this very request is saved and sent whole after this one
        // found no job for it, just before this one asks whether the release was already sent — which the
        // copy's job now says it was.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"DownloadJobs\"", StringComparison.Ordinal)
                && command.CommandText.Contains("'Failed'", StringComparison.Ordinal),
            async _ => competing = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // Answered as the unique index answers a copy that gets as far as the save (ADR 0013), never 409.
        Assert.Equal(HttpStatusCode.Created, competing!.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var winner = await ReadJobAsync(competing, cancellationToken);
        var answered = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(winner.Id, answered.Id);
        await AssertOneJobAndOneAuditRowAsync(winner.Id, cancellationToken);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_racing_another_download_under_its_request_id_is_409_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestId = Guid.NewGuid();
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var other = await _releases.CreateFound(_owner.User, _toloka, title: "Dune (2021)");
        HttpResponseMessage? competing = null;
        // Deterministic overlap: a send of another release under the same request id is saved and sent whole
        // just before this one's save.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DownloadJobEntity>().Any(entry => entry.State == EntityState.Added),
            async _ => competing = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(requestId, other.Id, _client.Id, _movies.Id)));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(requestId, release.Id, _client.Id, _movies.Id));

        //Assert
        // The unique index decides which one the request id names; the loser asked for something else.
        Assert.Equal(HttpStatusCode.Created, competing?.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestIdReusedError, await response.ReadErrorAsync(cancellationToken));
        var job = await ReadJobAsync(competing, cancellationToken);
        Assert.Equal(other.Id, job.ReleaseId);
        await AssertOneJobAndOneAuditRowAsync(job.Id, cancellationToken);
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Theory]
    [InlineData("release", ReleaseMissingError)]
    [InlineData("client", ClientGoneError)]
    [InlineData("destination", DestinationGoneError)]
    public async Task Create_whose_release_client_or_destination_is_removed_just_before_its_save_is_404_and_writes_nothing(
        string removed, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        // Every check passed; then another request removed the row — the release by the prune, the client
        // with its destinations, or the destination — just before this save.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DownloadJobEntity>().Any(entry => entry.State == EntityState.Added),
            async token =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                _ = removed switch
                {
                    "release" => await db.Releases.Where(row => row.Id == release.Id).ExecuteDeleteAsync(token),
                    "client" => await db.IntegrationConnections.Where(row => row.Id == _client.Id).ExecuteDeleteAsync(token),
                    _ => await db.DestinationMappings.Where(row => row.Id == _movies.Id).ExecuteDeleteAsync(token),
                };
            });

        //Act
        var response = await SendAsync(release);

        //Assert
        // The foreign key settles it: the dialog says what went, rather than a 500, and the audit row rolled
        // back with the job.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        AssertNothingFetchedOrSent();
    }

    [Theory]
    [InlineData("another release")]
    [InlineData("another client")]
    [InlineData("another destination")]
    public async Task Create_with_a_request_id_used_for_another_download_is_409_and_writes_nothing(string change)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var requestId = Guid.NewGuid();
        var earlier = await Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, requestedByUserId: _owner.User.Id, requestId: requestId);
        SendDownloadRequest request;
        switch (change)
        {
            case "another release":
                request = new SendDownloadRequest(requestId, (await _releases.CreateFound(_owner.User, _toloka, title: "Dune (2021)")).Id, _client.Id, _movies.Id);
                break;
            case "another client":
                var seedbox = await CreateSeedboxAsync();
                request = new SendDownloadRequest(requestId, release.Id, seedbox.Id, (await _destinations.Create(seedbox, "Movies", "Movies")).Id);
                break;
            default:
                request = new SendDownloadRequest(requestId, release.Id, _client.Id, (await _destinations.Create(_client, "TV Shows", "TV Shows")).Id);
                break;
        }

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // A request id names one download: asked for another, it is a bug in the page, never a second send.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestIdReusedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(earlier.Id, row.Id);
            Assert.Equal(release.Id, row.ReleaseId);
            Assert.Equal(DownloadJobStatus.Submitted, row.Status);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        AssertNothingFetchedOrSent();
    }

    [Fact]
    public async Task Create_with_the_request_id_of_a_failed_job_answers_that_job_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id);
        _qbittorrent.Categories.Remove("Movies");
        var failed = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);
        var failedJob = await ReadJobAsync(failed, cancellationToken);
        // Put right since: a send now would go through, so only the request id stands in the way.
        _qbittorrent.AddCategory("Movies", "/downloads/movies");
        var fetched = _factory.TorznabHttp.Requests.Count;
        var asked = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // A retry of the same submission is still that submission: trying again is a new request (a new id).
        Assert.Equal(HttpStatusCode.Created, failed.StatusCode);
        Assert.Equal(DownloadJobStatus.Failed, failedJob.Status);
        Assert.Equal(CategoryGoneError, failedJob.Error);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equivalent(failedJob, await ReadJobAsync(response, cancellationToken), strict: true);
        await AssertOneJobAndOneAuditRowAsync(failedJob.Id, cancellationToken);
        Assert.Equal(fetched, _factory.TorznabHttp.Requests.Count);
        Assert.Equal(asked, _factory.QBittorrentHttp.Requests.Count);
        Assert.Empty(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_with_a_request_id_another_user_used_makes_their_own_job()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestId = Guid.NewGuid();
        var ownersRelease = await _releases.CreateFound(_owner.User, _toloka);
        var ownersJob = await Jobs().Create(
            ownersRelease.Id, _client.Id, _client.Name, _movies.Id, requestedByUserId: _owner.User.Id, requestId: requestId);
        var membersRelease = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SendDownloadRequest(requestId, membersRelease.Id, _client.Id, _movies.Id));

        //Assert
        // A request id is the user's own: another's is neither their job to answer nor a reuse to refuse.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.NotEqual(ownersJob.Id, job.Id);
        Assert.Equal(membersRelease.Id, job.ReleaseId);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.DownloadJobs.AsNoTracking().Where(row => row.RequestId == requestId).OrderBy(row => row.RequestedByUserName).ToListAsync(cancellationToken);
            Assert.Equal([(_member.User.Id, job.Id), (_owner.User.Id, ownersJob.Id)], rows.Select(row => (row.RequestedByUserId!.Value, row.Id)));
            Assert.Equal(job.Id, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).TargetId);
        });
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Theory]
    [InlineData("its destination removed")]
    [InlineData("its client removed")]
    [InlineData("its client switched off")]
    [InlineData("its search older than a day")]
    public async Task Create_sent_again_after_what_it_named_changed_still_answers_its_job(string change)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id);
        var sent = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);
        var job = await ReadJobAsync(sent, cancellationToken);
        var token = _owner.AccessToken;
        switch (change)
        {
            case "its destination removed":
                Assert.Equal(HttpStatusCode.NoContent, (await _factory.DeleteHttpRequest($"/api/settings/destinations/{_movies.Id}", token)).StatusCode);
                break;
            case "its client removed":
                Assert.Equal(HttpStatusCode.NoContent, (await _factory.DeleteHttpRequest($"/api/settings/integrations/{_client.Id}", token)).StatusCode);
                break;
            case "its client switched off":
                await _factory.WithDbAsync(db => db.IntegrationConnections
                    .Where(integration => integration.Id == _client.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
                break;
            default:
                _factory.Time.Advance(TimeSpan.FromHours(25));
                // A day later the access token has long expired: the owner signs in again.
                token = (await _users.CreateSession(_owner.User)).AccessToken;
                break;
        }

        var fetched = _factory.TorznabHttp.Requests.Count;
        var asked = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, token, request);

        //Assert
        // The retry is the same request: it answers the job it made, which keeps the names it was made with,
        // rather than a refusal of something that has changed since.
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equivalent(job, await ReadJobAsync(response, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(job.Id, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).Id);
            Assert.Equal(1, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.DownloadRequested, cancellationToken));
        });
        Assert.Equal(fetched, _factory.TorznabHttp.Requests.Count);
        Assert.Equal(asked, _factory.QBittorrentHttp.Requests.Count);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_of_the_same_release_again_under_a_new_request_id_is_409_and_sends_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        var first = await SendAsync(release);
        var job = await ReadJobAsync(first, cancellationToken);
        var fetched = _factory.TorznabHttp.Requests.Count;
        var asked = _factory.QBittorrentHttp.Requests.Count;

        //Act
        // The dialog opened again: a new submission, so a new request id.
        var response = await SendAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadySentError, await response.ReadErrorAsync(cancellationToken));
        await AssertOneJobAndOneAuditRowAsync(job.Id, cancellationToken);
        Assert.Equal(fetched, _factory.TorznabHttp.Requests.Count);
        Assert.Equal(asked, _factory.QBittorrentHttp.Requests.Count);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Theory]
    [MemberData(nameof(WaysToRecogniseTheRelease))]
    public async Task Create_of_a_release_already_sent_to_the_client_is_409_and_sends_nothing(string recognisedBy)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        ReleaseEntity sent;
        ReleaseEntity release;
        DownloadJobEntity earlier;
        switch (recognisedBy)
        {
            // Every search stores its own copy of a listing (ADR 0006): the row differs, the source's id for
            // the listing doesn't.
            case "another search's copy with the same source item id":
                sent = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId);
                earlier = await SentAsync(sent, _owner.User);
                release = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId);
                break;
            // The client is the household's: whoever sent it, it is there.
            case "another user's copy with the same source item id":
                sent = await _releases.CreateFound(_member.User, _toloka, sourceItemId: TolokaItemId);
                earlier = await SentAsync(sent, _member.User);
                release = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId);
                break;
            case "the same listed info hash":
                sent = await _releases.CreateFound(_owner.User, _toloka, infoHash: ListedInfoHash);
                earlier = await SentAsync(sent, _owner.User);
                release = await _releases.CreateFound(_owner.User, _toloka, infoHash: ListedInfoHash);
                break;
            // The earlier listing stated no hash; its job worked one out from the payload it sent.
            case "the v1 info hash the earlier job worked out":
                sent = await _releases.CreateFound(_owner.User, _toloka);
                earlier = await SentAsync(sent, _owner.User, infoHashV1: ListedInfoHash);
                release = await _releases.CreateFound(_owner.User, _toloka, infoHash: ListedInfoHash);
                break;
            default:
                sent = await _releases.CreateFound(_owner.User, _toloka);
                earlier = await SentAsync(sent, _owner.User, infoHashV2: ListedV2InfoHash);
                release = await _releases.CreateFound(_owner.User, _toloka, infoHash: ListedV2InfoHash);
                break;
        }

        //Act
        var response = await SendAsync(release);

        //Assert
        // Decision 3: refused before anything is written or sent — no job, no audit row, no fetch.
        Assert.NotEqual(sent.Id, release.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadySentError, await response.ReadErrorAsync(cancellationToken));
        await AssertOnlyTheEarlierJobAsync(earlier.Id, cancellationToken);
        AssertNothingFetchedOrSent();
    }

    [Theory]
    [InlineData(DownloadJobStatus.Created)]
    [InlineData(DownloadJobStatus.Submitted)]
    [InlineData(DownloadJobStatus.Downloading)]
    [InlineData(DownloadJobStatus.Downloaded)]
    [InlineData(DownloadJobStatus.NeedsLinking)]
    public async Task Create_of_a_release_whose_earlier_job_did_not_fail_is_409_whatever_its_status(DownloadJobStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId);
        var earlier = await SentAsync(sent, _owner.User, status: status);
        var release = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId);

        //Act
        var response = await SendAsync(release);

        //Assert
        // Still being sent, or with an outcome nobody knows yet, it may be in the client: sent once is enough.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadySentError, await response.ReadErrorAsync(cancellationToken));
        await AssertOnlyTheEarlierJobAsync(earlier.Id, cancellationToken);
        AssertNothingFetchedOrSent();
    }

    [Fact]
    public async Task Create_of_a_release_whose_earlier_job_failed_is_201_and_sends_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId, infoHash: SampleInfoHash);
        await SentAsync(sent, _owner.User, status: DownloadJobStatus.Failed, infoHashV1: SampleInfoHash);
        var release = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId, infoHash: SampleInfoHash);

        //Act
        var response = await SendAsync(release);

        //Assert
        // A failed job sent nothing new to the client, so it is no reason not to send the release now.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(SampleInfoHash, job.ClientJobId);
        Assert.Single(_qbittorrent.AddRequests);
        await _factory.WithDbAsync(async db => Assert.Equal(2, await db.DownloadJobs.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Create_of_a_release_sent_to_another_client_is_201_and_sends_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seedbox = await CreateSeedboxAsync();
        var seedboxMovies = await _destinations.Create(seedbox, "Movies", "Movies");
        var sent = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId, infoHash: SampleInfoHash);
        await Jobs().Create(sent.Id, seedbox.Id, seedbox.Name, seedboxMovies.Id, requestedByUserId: _owner.User.Id, infoHashV1: SampleInfoHash);
        var release = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId, infoHash: SampleInfoHash);

        //Act
        var response = await SendAsync(release);

        //Assert
        // Decision 3 is per client: in one client, it isn't in the other.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal("qBittorrent", job.DownloadClientName);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_of_a_listing_with_the_same_id_from_another_source_is_201_and_sends_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mazepa = await _integrations.Create("torznab", "Mazepa", "http://prowlarr.test:9696/2/api", apiKey: ApiKey);
        var sent = await _releases.CreateFound(_owner.User, mazepa, sourceItemId: TolokaItemId);
        await SentAsync(sent, _owner.User);
        var release = await _releases.CreateFound(_owner.User, _toloka, sourceItemId: TolokaItemId);

        //Act
        var response = await SendAsync(release);

        //Assert
        // A source's id names a listing of that source only: another source's same id is another release.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(DownloadJobStatus.Submitted, (await ReadJobAsync(response, cancellationToken)).Status);
        Assert.Single(_qbittorrent.AddRequests);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    /// <summary>An earlier job of <paramref name="user"/>'s that sent <paramref name="release"/> to the
    /// qBittorrent, into Movies.</summary>
    private Task<DownloadJobEntity> SentAsync(
        ReleaseEntity release,
        UserEntity user,
        DownloadJobStatus status = DownloadJobStatus.Submitted,
        string? infoHashV1 = null,
        string? infoHashV2 = null) =>
        Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, status, user.Id, infoHashV1, infoHashV2: infoHashV2);

    /// <summary>A second qBittorrent, never asked: nothing listens at its address.</summary>
    private Task<IntegrationConnectionEntity> CreateSeedboxAsync() =>
        _integrations.Create("qbittorrent", "Seedbox", "http://127.0.0.1:9", FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword);

    /// <summary>Sends <paramref name="release"/> as the owner to the qBittorrent, into Movies, under a request
    /// id of its own.</summary>
    private Task<HttpResponseMessage> SendAsync(ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage? response, CancellationToken cancellationToken) =>
        (await response!.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private Task AssertOneJobAndOneAuditRowAsync(Guid jobId, CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        Assert.Equal(jobId, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).Id);
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(AuditAction.DownloadRequested, audit.Action);
        Assert.Equal(jobId, audit.TargetId);
    });

    private Task AssertOnlyTheEarlierJobAsync(Guid earlierJobId, CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        Assert.Equal(earlierJobId, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
    });

    private void AssertNothingFetchedOrSent()
    {
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }
}
