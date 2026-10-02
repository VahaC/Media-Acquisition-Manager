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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-14 — a job its client doesn't list, or whose client can't be asked, keeps its status and is still followed,
// showing why (ADR 0014). A torrent removed in qBittorrent is a reason, not an end, and following goes on once it
// comes back with the same hash (MAM-14's decision 3); a job taken without an id shows nothing for the listing
// grace and why after it. A client that can't be asked — unreachable, too old, refusing its credentials, failing,
// or an adapter that throws, for its lookup or its destination's listing alone — never makes a job missing; nor
// does a client switched off, removed, of a type this
// build can't follow, or whose password can't be read, which qBittorrent isn't even asked about. A client's own
// sentence is its owner's to read; a member reads the app's, naming the client. One client that can't be asked
// never stops another's jobs.
public sealed class DownloadJobMissingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string SeedboxAddress = "http://seedbox.test:8080";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>, which the feed's download links
    /// answer: qBittorrent's id for the torrent a send adds.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    /// <summary>A planted exception message: an unforeseen exception's text never reaches the log.</summary>
    private const string PlantedExceptionMessage = "planted-exception-message-0002";

    private const string SignIn = "POST /api/v2/auth/login";
    private const string Version = "GET /api/v2/app/version";
    private const string MoviesListing = "GET /api/v2/torrents/info?category=Movies";

    // Spelled out rather than read from the product code: these are what the Jobs page shows.
    private const string MissingReason =
        "“qBittorrent” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.";
    private const string NotListedReason =
        "“qBittorrent” took this download but hasn't listed it since. It is still followed; to send the release again, cancel this job first.";
    private const string RemovedReason =
        "The download client “qBittorrent” was removed, so this download can't be followed. Cancel the job to stop tracking it.";
    private const string SwitchedOffReason =
        "“qBittorrent” is switched off, so this download isn't followed for now. The owner can switch it on under Integrations.";
    private const string NoAdapterReason = "This version of the app can't follow downloads in “transmission”.";
    private const string PasswordUnreadableReason =
        "The stored password of “qBittorrent” can't be read with this installation's encryption key. Enter the password again and save.";
    private const string ThrewReason = "The app couldn't read what “qBittorrent” answered.";
    private const string UnreachableReason =
        "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
    private const string ServiceErrorReason = "qBittorrent answered 500 Internal Server Error. Its own log says why.";
    private const string ForMemberReason =
        "“qBittorrent” couldn't be asked about this download. Ask the owner to check “qBittorrent” under Integrations.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadJobMissingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string, string> ClientsThatCanNotBeAsked => new()
    {
        { "unreachable", "Unreachable", UnreachableReason },
        { "too old", "UnsupportedVersion", "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again." },
        { "failing", "ServiceError", ServiceErrorReason },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Poll_of_a_torrent_removed_from_qBittorrent_keeps_the_job_with_why_and_follows_it_until_it_comes_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await SendAsync(_member, cancellationToken);
        _qbittorrent.UpdateTorrent(SampleInfoHash, torrent => torrent with { State = "downloading", Progress = 0.3 });
        await PassAsync(cancellationToken);
        var downloadingUtc = (await RowAsync(sent.Id, cancellationToken)).StatusChangedUtc;
        // Someone removes it in qBittorrent itself.
        var torrent = _qbittorrent.Torrents.Single();
        _qbittorrent.RemoveTorrent(SampleInfoHash);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        await PassAsync(cancellationToken);
        var missing = await RowAsync(sent.Id, cancellationToken);
        var shown = await ReadAsync(_member, sent.Id, cancellationToken);
        var beforeStill = _factory.QBittorrentHttp.Requests.Count;
        await PassAsync(cancellationToken);
        var stillLookedFor = QBittorrentCalls()[beforeStill..];
        // Added back in qBittorrent, the same torrent, so the same hash.
        _qbittorrent.AddTorrent(torrent with { Progress = 0.5 });

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // MAM-14's decision 3: a reason, not an end — the status, its time and its progress stay, the sender reads
        // why, and every pass still looks for it, by its id and among its destination's torrents.
        Assert.Equal(
            (DownloadJobStatus.Downloading, (double?)0.3, downloadingUtc, MissingReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App, SampleInfoHash),
            (missing.Status, missing.Progress, missing.StatusChangedUtc, missing.BlockReason, missing.BlockReasonOrigin, missing.ClientJobId));
        Assert.Equal((DownloadJobStatus.Downloading, MissingReason), (shown.Status, shown.BlockReason));
        Assert.Equal([Version, Lookup(SampleInfoHash), Version, MoviesListing], stillLookedFor);
        var row = await RowAsync(sent.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Downloading, (double?)0.5, downloadingUtc, (string?)null, (DownloadJobErrorOrigin?)null),
            (row.Status, row.Progress, row.StatusChangedUtc, row.BlockReason, row.BlockReasonOrigin));
    }

    [Fact]
    public async Task Poll_of_a_job_taken_without_an_id_shows_why_only_once_the_listing_grace_is_over()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Taken without an id, and qBittorrent hasn't listed it since.
        var job = await PlantAsync(_member, Hash(1), clientJobId: null, torrent: false);
        _factory.Time.Advance(DownloadJobPoller.ListingGrace - TimeSpan.FromSeconds(1));
        await PassAsync(cancellationToken);
        var withinGrace = await RowAsync(job.Id, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // A qBittorrent before 5.2 lists a torrent a moment after taking it: nothing to show for that moment.
        Assert.Equal((DownloadJobStatus.Submitted, (string?)null), (withinGrace.Status, withinGrace.BlockReason));
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, NotListedReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App, (string?)null),
            (row.Status, row.BlockReason, row.BlockReasonOrigin, row.ClientJobId));
    }

    [Theory]
    [MemberData(nameof(ClientsThatCanNotBeAsked))]
    public async Task Poll_of_a_client_that_can_not_be_asked_never_makes_a_job_missing(string failure, string status, string expectedReason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_owner, Hash(1), progress: 0.4, torrentState: "downloading");
        var plantedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        switch (failure)
        {
            case "unreachable":
                _qbittorrent.Unreachable = true;
                break;
            case "too old":
                _qbittorrent.Version = "v4.3.9";
                break;
            default:
                _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/info"
                    ? VendorResponses.Text("Internal Server Error", HttpStatusCode.InternalServerError)
                    : null;
                break;
        }

        _factory.Logs.Clear();
        await PassAsync(cancellationToken);
        var failed = await RowAsync(job.Id, cancellationToken);
        await PassAsync(cancellationToken);
        var logged = _factory.Logs.Entries.Where(entry => entry.Category == typeof(DownloadJobPoller).FullName).Select(entry => entry.Message).ToList();
        _qbittorrent.Unreachable = false;
        _qbittorrent.Version = "v5.2.3";
        _qbittorrent.Intercept = null;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // qBittorrent's own sentence, the owner's to read; the job as it was — never missing — and the log says so
        // once, by the kind of failure, never the sentence.
        Assert.Equal(
            (DownloadJobStatus.Submitted, (double?)null, plantedUtc, Hash(1), expectedReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.DownloadClient),
            (failed.Status, failed.Progress, failed.StatusChangedUtc, failed.ClientJobId, failed.BlockReason, failed.BlockReasonOrigin));
        Assert.Equal([$"The download jobs of integration {_client.Id} (qbittorrent) can't be followed for now: {status}."], logged);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.4, (string?)null), (row.Status, row.Progress, row.BlockReason));
    }

    [Theory]
    [InlineData("failing", ServiceErrorReason)]
    [InlineData("unreachable", UnreachableReason)]
    public async Task Poll_of_a_removed_torrent_whose_destination_listing_fails_shows_the_clients_sentence_never_missing(
        string failure, string expectedReason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Listed before, sent with its v1 hash alone, and removed in qBittorrent since: its lookup finds nothing,
        // and only its destination's listing could say whether qBittorrent renamed it.
        var job = await PlantAsync(_owner, Hash(1), torrent: false);
        // The lookup answers; the listing alone fails.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/info"
            && request.RequestUri.Query.StartsWith("?category=", StringComparison.Ordinal)
                ? failure == "failing"
                    ? VendorResponses.Text("Internal Server Error", HttpStatusCode.InternalServerError)
                    : throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it.")
                : null;
        await PassAsync(cancellationToken);
        var failed = await RowAsync(job.Id, cancellationToken);
        var askedWhileFailing = QBittorrentCalls();
        _qbittorrent.Intercept = null;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // The listing that failed said nothing about the job: its status kept, with qBittorrent's own sentence,
        // never the app's "doesn't list it any more". Once the listing answers, the pass decides as usual: missing.
        Assert.Equal([SignIn, Version, Lookup(Hash(1)), Version, MoviesListing], askedWhileFailing);
        Assert.Equal(
            (DownloadJobStatus.Submitted, expectedReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.DownloadClient, Hash(1)),
            (failed.Status, failed.BlockReason, failed.BlockReasonOrigin, failed.ClientJobId));
        await AssertBlockedAsync(job.Id, MissingReason, DownloadJobErrorOrigin.App, cancellationToken);
    }

    [Fact]
    public async Task Poll_after_qBittorrent_refused_the_credentials_never_signs_in_again_until_the_owner_tests_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_owner, Hash(1), progress: 0.4, torrentState: "downloading");
        // The password changed in qBittorrent.
        _qbittorrent.Password = "changed-in-qbittorrent";
        await PassAsync(cancellationToken);
        var refused = await RowAsync(job.Id, cancellationToken);
        var afterRefusal = _factory.QBittorrentHttp.Requests.Count;
        await PassAsync(cancellationToken);
        var remembered = await RowAsync(job.Id, cancellationToken);
        // Changed back in qBittorrent: the app doesn't know, and still sends nothing.
        _qbittorrent.Password = FakeQBittorrentServer.DefaultPassword;
        await PassAsync(cancellationToken);
        var sentWhileRemembered = _factory.QBittorrentHttp.Requests.Count - afterRefusal;
        // The owner tests the connection under Integrations, which signs in once more.
        var tested = await _factory.PostHttpRequest($"/api/settings/integrations/{_client.Id}/test", _owner.AccessToken);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Every refused sign-in counts towards qBittorrent's ban of the app's address (MAM-11): a pass every 10 s
        // never adds to it. The job just shows why, as qBittorrent's sentence, until then.
        Assert.Equal(
            (DownloadJobStatus.Submitted, "qBittorrent refused the user name or password."),
            (refused.Status, refused.BlockReason));
        Assert.Equal(
            (DownloadJobStatus.Submitted, "qBittorrent refused the user name or password the last time the app signed in, so the app won't try them again: each refused sign-in counts towards qBittorrent's ban of this app's address. Correct them under Integrations, or test the connection there once qBittorrent accepts them."),
            (remembered.Status, remembered.BlockReason));
        Assert.Equal(0, sentWhileRemembered);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.4, (string?)null), (row.Status, row.Progress, row.BlockReason));
    }

    [Fact]
    public async Task Poll_of_a_switched_off_client_shows_why_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, Hash(1));
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(row => row.Id == _client.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsEnabled, false), cancellationToken));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // MAM-7's switch: a switched-off client is left alone. The app's sentence, which anyone reads.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await AssertBlockedAsync(job.Id, SwitchedOffReason, DownloadJobErrorOrigin.App, cancellationToken);
        Assert.Equal(SwitchedOffReason, (await ReadAsync(_member, job.Id, cancellationToken)).BlockReason);
    }

    [Fact]
    public async Task Poll_of_a_job_whose_client_was_removed_shows_why_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member, Hash(1));
        var removed = await _factory.DeleteHttpRequest($"/api/settings/integrations/{_client.Id}", _owner.AccessToken);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // The job outlives its client (SetNull) and keeps its name, which the reason gives.
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await AssertBlockedAsync(job.Id, RemovedReason, DownloadJobErrorOrigin.App, cancellationToken);
        Assert.Null((await RowAsync(job.Id, cancellationToken)).DownloadClientConnectionId);
    }

    [Fact]
    public async Task Poll_of_a_client_whose_password_can_not_be_read_shows_why_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_owner, Hash(1));
        // Another client's password copied onto this row: sealed for the other row, so it opens for neither.
        var seedbox = await CreateClientAsync("Seedbox", SeedboxAddress);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(row => row.Id == _client.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordEncrypted, seedbox.PasswordEncrypted), cancellationToken));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await AssertBlockedAsync(job.Id, PasswordUnreadableReason, DownloadJobErrorOrigin.App, cancellationToken);
    }

    [Fact]
    public async Task Poll_of_a_client_of_a_type_this_build_can_not_follow_shows_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var transmission = await _integrations.Create("transmission", "Transmission", kind: IntegrationKind.DownloadClient);
        var release = await _releases.CreateFound(_member.User, _toloka);
        var job = await Jobs().Create(
            release.Id, transmission.Id, transmission.Name, status: DownloadJobStatus.Submitted, requestedByUserId: _member.User.Id, infoHashV1: Hash(1));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        await AssertBlockedAsync(job.Id, NoAdapterReason, DownloadJobErrorOrigin.App, cancellationToken);
    }

    [Fact]
    public async Task Poll_of_an_adapter_that_throws_shows_the_apps_sentence_and_logs_the_type_never_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_owner, Hash(1));
        // Not a transport failure the adapter maps: something nobody foresaw, as a parser bug would be.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/info"
            ? throw new InvalidOperationException(PlantedExceptionMessage)
            : null;
        _factory.Logs.Clear();

        //Act
        await _factory.PollDownloadJobsAsync(cancellationToken);

        //Assert
        // The pass went on — the job shows the app's sentence, never missing — and the log has the type and the
        // stack, never the message.
        await AssertBlockedAsync(job.Id, ThrewReason, DownloadJobErrorOrigin.App, cancellationToken);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal(typeof(DownloadJobPoller).FullName, error.Category);
        Assert.StartsWith(
            $"The job lookup of the followed download jobs of integration {_client.Id} (qbittorrent) threw System.InvalidOperationException",
            error.Message,
            StringComparison.Ordinal);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_and_list_show_a_clients_own_sentence_to_the_owner_and_the_apps_to_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownersJob = await PlantAsync(_owner, Hash(1));
        var membersJob = await PlantAsync(_member, Hash(2));
        _qbittorrent.Unreachable = true;
        await PassAsync(cancellationToken);

        //Act
        var forOwner = await ReadAsync(_owner, ownersJob.Id, cancellationToken);
        var forMember = await ReadAsync(_member, membersJob.Id, cancellationToken);
        var ownersList = await ListAsync(_owner, cancellationToken);
        var membersList = await ListAsync(_member, cancellationToken);

        //Assert
        // Both rows keep qBittorrent's own sentence, which can name its address: the owner reads it, a member the
        // app's, naming qBittorrent without what it said — on their own job, as every job is its sender's.
        Assert.Equal(UnreachableReason, forOwner.BlockReason);
        Assert.Equal(ForMemberReason, forMember.BlockReason);
        Assert.Equal(UnreachableReason, Assert.Single(ownersList.Items).BlockReason);
        Assert.Equal(ForMemberReason, Assert.Single(membersList.Items).BlockReason);
        await AssertBlockedAsync(membersJob.Id, UnreachableReason, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
    }

    [Fact]
    public async Task Poll_of_one_client_that_can_not_be_asked_still_follows_another()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seedboxServer = new FakeQBittorrentServer(SeedboxAddress) { Unreachable = true };
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, seedboxServer);
        var seedbox = await CreateClientAsync("Seedbox", SeedboxAddress);
        var seedboxMovies = await new DestinationMappingSeeder(_factory.Scope).Create(seedbox, "Movies", "Movies");
        var release = await _releases.CreateFound(_owner.User, _toloka);
        // The older job, so its client is followed first.
        var stuck = await Jobs().Create(
            release.Id, seedbox.Id, seedbox.Name, seedboxMovies.Id, DownloadJobStatus.Submitted, _owner.User.Id, infoHashV1: Hash(2), clientJobId: Hash(2));
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var job = await PlantAsync(_owner, Hash(1), progress: 0.6, torrentState: "downloading");

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.Equal(
            "Couldn't reach qBittorrent at http://seedbox.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.",
            (await RowAsync(stuck.Id, cancellationToken)).BlockReason);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.6, (string?)null), (row.Status, row.Progress, row.BlockReason));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One pass of the app's tracker, which must not have thrown: a pass that threw is logged and
    /// would otherwise read as one that changed nothing.</summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.PollDownloadJobsAsync(cancellationToken);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, string address) =>
        _integrations.Create(
            "qbittorrent",
            name,
            address,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    /// <summary>Sends a release as <paramref name="user"/> to qBittorrent's Movies, the way the Download dialog
    /// does.</summary>
    private async Task<DownloadJobDto> SendAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        var release = await _releases.CreateFound(user.User, _toloka);
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The send answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>
    /// A Submitted job of <paramref name="user"/>'s, sent with the v1 info hash <paramref name="hash"/> — qBittorrent's
    /// id for it, unless <paramref name="clientJobId"/> says it was taken without one — and, unless
    /// <paramref name="torrent"/> says otherwise, the torrent in qBittorrent, in <paramref name="torrentState"/> at
    /// <paramref name="progress"/>.
    /// </summary>
    private async Task<DownloadJobEntity> PlantAsync(
        AuthorizedUser user, string hash, string? clientJobId = "", bool torrent = true, double progress = 0.1, string torrentState = "downloading")
    {
        var release = await _releases.CreateFound(user.User, _toloka);
        if (torrent)
            _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), $"Torrent {hash[^4..]}", "Movies", torrentState, progress));
        return await Jobs().Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            DownloadJobStatus.Submitted,
            user.User.Id,
            infoHashV1: hash,
            clientJobId: clientJobId == "" ? hash : clientJobId);
    }

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private async Task<DownloadJobEntity> RowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    /// <summary>The job still Submitted — never missing, never ended — showing <paramref name="reason"/> as
    /// <paramref name="origin"/>'s.</summary>
    private async Task AssertBlockedAsync(Guid jobId, string reason, DownloadJobErrorOrigin origin, CancellationToken cancellationToken)
    {
        var row = await RowAsync(jobId, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, reason, (DownloadJobErrorOrigin?)origin, (string?)null),
            (row.Status, row.BlockReason, row.BlockReasonOrigin, row.Error));
    }

    /// <summary>The job as <paramref name="user"/> reads it over HTTP.</summary>
    private async Task<DownloadJobDto> ReadAsync(AuthorizedUser user, Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The first page of <paramref name="user"/>'s jobs, as the Jobs page reads it.</summary>
    private async Task<DownloadJobListResponse> ListAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(BaseUrl, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>A v1 info hash of its own for each <paramref name="n"/>.</summary>
    private static string Hash(int n) => n.ToString("x40", CultureInfo.InvariantCulture);

    /// <summary>Every request qBittorrent received, as method and path with its query, in order.</summary>
    private List<string> QBittorrentCalls() => [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The job lookup for <paramref name="torrentIds"/>, joined by <c>|</c> and escaped as the adapter
    /// sends them.</summary>
    private static string Lookup(params string[] torrentIds) =>
        $"GET /api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join('|', torrentIds))}";
}
