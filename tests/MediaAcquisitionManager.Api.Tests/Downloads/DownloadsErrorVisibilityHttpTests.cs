using System.Net;
using System.Net.Http.Json;
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

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-13 — who reads why a download failed. MAM-10's rule for a service's sentence, as the Search and
// Library pages apply it: it can name the service's address or a redirect target, and the integrations are
// the owner's, so a member who sent the release (MAM-13's decision 1) reads a sentence the app writes, naming
// the service without what it said — in the send's answer and in every read of the job — while the owner
// reads the service's own. The app's own sentences name nothing of the owner's and are everyone's. The row
// keeps the service's sentence, and whose it is, whoever sent the release.
public sealed class DownloadsErrorVisibilityHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    // Spelled out rather than read from the product code: these are what the Download dialog shows.
    private const string ClientUnreachableError =
        "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
    private const string ClientFailedError = "qBittorrent answered 500 Internal Server Error. Its own log says why.";
    private const string FeedUnreachableError =
        "Couldn't reach Torznab at http://prowlarr.test:9696/1/api: nothing accepted the connection. Check the address and port, and that Torznab is running.";
    private const string ClientForMembersError =
        "“qBittorrent” couldn't take the release, so nothing was sent. Ask the owner to check “qBittorrent” under Integrations.";
    private const string SourceForMembersError =
        "“Toloka” couldn't hand over the release, so nothing was sent. Ask the owner to check “Toloka” under Integrations.";
    private const string AlreadyHasError = "“qBittorrent” already has this torrent, so nothing was sent.";
    private const string CategoryGoneError =
        "“qBittorrent” has no category “Movies” any more, so nothing was sent: it would have created one in its default folder. Create the category again in “qBittorrent”, or the owner edits the destination.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _destination = null!;

    public DownloadsErrorVisibilityHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey);
        _feed.Arm(_factory.TorznabHttp);
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
        _destination = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_by_a_member_whose_client_could_not_be_reached_names_the_client_without_its_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _qbittorrent.Unreachable = true;

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        var job = await ReadJobAsync(response, HttpStatusCode.Created, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, ClientForMembersError), (job.Status, job.Error));
        Assert.DoesNotContain("qbittorrent.test", await ReadMemberJobAsync(job.Id, cancellationToken), StringComparison.Ordinal);
        // The row keeps the client's own sentence, and whose it is, for the owner.
        await AssertRowAsync(job.Id, ClientUnreachableError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
    }

    [Fact]
    public async Task Create_by_the_owner_whose_client_could_not_be_reached_reads_the_clients_own_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka);
        _qbittorrent.Unreachable = true;

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        var job = await ReadJobAsync(response, HttpStatusCode.Created, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, ClientUnreachableError), (job.Status, job.Error));
        using var read = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}", _owner.AccessToken);
        Assert.Equal(ClientUnreachableError, (await ReadJobAsync(read, HttpStatusCode.OK, cancellationToken)).Error);
    }

    [Fact]
    public async Task Create_by_a_member_whose_feed_could_not_be_reached_names_the_source_without_its_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _feed.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        var job = await ReadJobAsync(response, HttpStatusCode.Created, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, SourceForMembersError), (job.Status, job.Error));
        Assert.DoesNotContain("prowlarr.test", await ReadMemberJobAsync(job.Id, cancellationToken), StringComparison.Ordinal);
        await AssertRowAsync(job.Id, FeedUnreachableError, DownloadJobErrorOrigin.ReleaseSource, cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_by_a_member_whose_add_failed_names_the_client_without_what_it_answered()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _qbittorrent.Intercept = request =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/torrents/add"
                ? VendorResponses.Text("", HttpStatusCode.InternalServerError)
                : null;

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        var job = await ReadJobAsync(response, HttpStatusCode.Created, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, ClientForMembersError), (job.Status, job.Error));
        await AssertRowAsync(job.Id, ClientFailedError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
    }

    [Fact]
    public async Task Create_by_a_member_for_a_torrent_qbittorrent_already_has_reads_the_apps_own_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _qbittorrent.AddTorrent(TorrentInfoHashReader.FromTorrentFile(TorznabSamples.TorrentFile()), "Interstellar", "Movies");

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        // The app's sentence names nothing of the owner's: a member reads it as it is.
        var job = await ReadJobAsync(response, HttpStatusCode.Created, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, AlreadyHasError), (job.Status, job.Error));
        await AssertRowAsync(job.Id, AlreadyHasError, DownloadJobErrorOrigin.App, cancellationToken);
    }

    [Fact]
    public async Task Create_by_a_member_whose_category_is_gone_reads_the_apps_own_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _qbittorrent.Categories.Remove("Movies");

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        var job = await ReadJobAsync(response, HttpStatusCode.Created, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, CategoryGoneError), (job.Status, job.Error));
        await AssertRowAsync(job.Id, CategoryGoneError, DownloadJobErrorOrigin.App, cancellationToken);
    }

    [Fact]
    public async Task Create_sent_again_by_a_member_answers_the_failed_job_without_the_clients_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _qbittorrent.Unreachable = true;
        var request = new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _destination.Id);
        using var first = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, request);

        //Act
        using var again = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, request);

        //Assert
        var job = await ReadJobAsync(again, HttpStatusCode.OK, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, ClientForMembersError), (job.Status, job.Error));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _destination.Id));

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    // The member's own read of the job, as raw JSON: what a later look at it shows them.
    private async Task<string> ReadMemberJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var read = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", _member.AccessToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        return await read.Content.ReadAsStringAsync(cancellationToken);
    }

    private Task AssertRowAsync(Guid jobId, string error, DownloadJobErrorOrigin origin, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken);
            Assert.Equal((DownloadJobStatus.Failed, error, (DownloadJobErrorOrigin?)origin), (row.Status, row.Error, row.ErrorOrigin));
        });
}
