using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Services.Integrations;
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

// MAM-13 — how a send that goes wrong ends: always 201 with the job, its one audit row, and the job's own
// outcome — Failed with the sentence of the step that stopped it, each step in the sender's order (the
// payload fetched, the destination's category confirmed, the client asked whether it already has the
// torrent, then the one submission), and exactly what reached qBittorrent: nothing added for a failure
// before the submission, and the submission never sent twice. An answer that never came is not a no
// (house rule 8): the client's list decides — Submitted when the torrent is there, NeedsLinking when it
// can't be told which job it became, Failed with the client's sentence when it isn't. The successes are
// DownloadsSendHttpTests, the refusals before a job is written DownloadsValidationHttpTests.
public sealed class DownloadsSendFailuresHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>, which the feed's download
    /// links answer: qBittorrent's id for the torrent.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    // Spelled out rather than read from the product code: these are what the job shows on the search page.
    private const string FeedNotFoundError =
        "The Torznab feed couldn't hand over this release (404 Not Found): it may be gone, or the feed failed to fetch it; its own log says which.";
    private const string FeedNotATorrentError = "The Torznab feed answered the download with something that isn't a .torrent file.";
    private const string FeedUnreachableError =
        "Couldn't reach Torznab at http://prowlarr.test:9696/1/api: nothing accepted the connection. Check the address and port, and that Torznab is running.";
    private const string LocatorUnreadableError =
        "The release's download link can't be read with this installation's encryption key, so nothing was sent. Search again.";
    private const string SourceKeyUnreadableError =
        "The stored API key of “Hurtom” can't be read with this installation's encryption key. Enter the API key again and save.";
    private const string ClientPasswordUnreadableError =
        "The stored password of “qBittorrent” can't be read with this installation's encryption key. Enter the password again and save.";
    private const string CategoryGoneError =
        "“qBittorrent” has no category “Movies” any more, so nothing was sent: it would have created one in its default folder. Create the category again in “qBittorrent”, or the owner edits the destination.";
    private const string AlreadyHasError = "“qBittorrent” already has this torrent, so nothing was sent.";
    private const string ClientFailedError = "qBittorrent answered 500 Internal Server Error. Its own log says why.";
    private const string AddedNothingError = "qBittorrent added nothing: it already has this torrent, or it couldn't use what it was sent.";
    private const string AddFailedError = "qBittorrent didn't add the release: it already has this torrent, or it couldn't use what it was sent.";
    private const string UnreadableTorrentError = "qBittorrent couldn't read the .torrent file it was sent (415 Unsupported Media Type).";
    private const string SessionRefusedError = "qBittorrent refused the session it had just opened.";
    private const string UnforeseenAnswerError = "The app couldn't read what “qBittorrent” answered.";

    /// <summary>A planted exception message: an unforeseen exception's text never reaches the log.</summary>
    private const string PlantedExceptionMessage = "planted-exception-message-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _destination = null!;

    public DownloadsSendFailuresHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> FeedFailures => new()
    {
        { "404 Not Found", FeedNotFoundError },
        { "an HTML page", FeedNotATorrentError },
        { "unreachable", FeedUnreachableError },
    };

    public static TheoryData<string, string> ClientsThatCanNotBeAsked => new()
    {
        { "unreachable", "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running." },
        { "too old", "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again." },
        { "refused sign-in", "qBittorrent refused the user name or password." },
    };

    public static TheoryData<string> WaysAnAnswerNeverCame => ["timed out", "connection lost", "unreadable answer"];

    public static TheoryData<string, string> AnswersThatNeverCame => new()
    {
        { "timed out", "qBittorrent at http://qbittorrent.test:8080 didn't answer within 15 seconds." },
        { "connection lost", "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the connection failed before it answered." },
        { "unreadable answer", "The service at this address answered, but not the way qBittorrent answers a new torrent." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        // The owner sends: a failure's sentence can name the client's or the feed's address, which is the
        // owner's to read (the MAM-8 rule); what a member's job shows of it is not this class's to pin.
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey);
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        _destination = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the payload ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FeedFailures))]
    public async Task Create_whose_feed_download_fails_fails_with_the_feeds_sentence_and_asks_no_client(string failure, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        _feed.Intercept = failure switch
        {
            // Jackett's answer to every failed download: a bare 404.
            "404 Not Found" => _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            // A tracker's sign-in page passed through by the gateway.
            "an HTML page" => _ => VendorResponses.Text("<html><body>Please sign in</body></html>", HttpStatusCode.OK, "text/html"),
            "unreachable" => _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        // Nothing fetched, so no hash to record and nothing for the client: it isn't even asked for its categories.
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, expectedError, TorrentInfoHashes.None, cancellationToken);
        Assert.Equal(["GET /1/download"], _factory.TorznabHttp.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_whose_stored_link_can_not_be_opened_fails_logs_a_warning_and_fetches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        var other = await FindReleaseAsync("Interstellar (2014) WEB-DL 2160p");
        // Another release's sealed link copied onto this row: sealed under the installation's key, but for
        // the other release, so it opens for neither this release nor anyone who swapped it.
        await _factory.WithDbAsync(db => db.Releases
            .Where(row => row.Id == release.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PayloadLocatorEncrypted, other.PayloadLocatorEncrypted), cancellationToken));
        _factory.Logs.Clear();

        //Act
        var response = await SendAsync(release);

        //Assert
        var job = await AssertJobAsync(response, DownloadJobStatus.Failed, null, LocatorUnreadableError, TorrentInfoHashes.None, cancellationToken);
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        // A key problem the owner should see in the log: the release's id and the job's, never the link.
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(DownloadSender).FullName && entry.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal($"The download link of release {release.Id} could not be decrypted; download job {job.Id} sent nothing.", warning.Message);
    }

    [Fact]
    public async Task Create_whose_sources_api_key_can_not_be_read_fails_and_fetches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A feed whose key was sealed under another installation key (a restored database, a changed key).
        var hurtom = await _integrations.CreateWithUnreadableApiKey("torznab", "Hurtom");
        var release = await _releases.CreateFound(_owner.User, hurtom);
        _factory.Logs.Clear();

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, SourceKeyUnreadableError, TorrentInfoHashes.None, cancellationToken);
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(IntegrationSettingsFactory).FullName);
        Assert.Equal(
            (LogLevel.Warning, $"The stored API key of integration {hurtom.Id} could not be decrypted with the configured encryption key."),
            (warning.Level, warning.Message));
    }

    // ── the destination ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_clients_password_can_not_be_read_fails_and_asks_the_client_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // Another client's password copied onto this row: sealed for the other row, so it opens for neither.
        var seedbox = await CreateClientAsync("Seedbox", "http://seedbox.test:8080");
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(row => row.Id == _client.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordEncrypted, seedbox.PasswordEncrypted), cancellationToken));

        //Act
        var response = await SendAsync(release);

        //Assert
        // The payload was fetched and its hash recorded; the client's sign-in was never sent.
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, ClientPasswordUnreadableError, SampleHashes(), cancellationToken);
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_whose_category_is_gone_fails_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // Removed in qBittorrent after the destination was saved: a submission into it would create it again,
        // with no folder of its own, in qBittorrent's default folder (ADR 0010).
        _qbittorrent.Categories.Remove("Movies");

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, CategoryGoneError, SampleHashes(), cancellationToken);
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories"],
            QBittorrentCalls());
        Assert.Empty(_qbittorrent.AddRequests);
        Assert.False(_qbittorrent.Categories.ContainsKey("Movies"));
    }

    [Theory]
    [MemberData(nameof(ClientsThatCanNotBeAsked))]
    public async Task Create_while_qbittorrent_can_not_be_asked_fails_with_its_sentence_and_sends_nothing(string failure, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        switch (failure)
        {
            case "unreachable":
                _qbittorrent.Unreachable = true;
                break;
            case "too old":
                _qbittorrent.Version = "v4.3.9";
                break;
            case "refused sign-in":
                _qbittorrent.Password = "changed-in-qbittorrent";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure), failure, null);
        }

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, expectedError, SampleHashes(), cancellationToken);
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, request => IsAdd(request) || IsLookup(request));
        Assert.Empty(_qbittorrent.AddRequests);
        Assert.Empty(_qbittorrent.Torrents);
    }

    // ── what the client already has ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_for_a_torrent_qbittorrent_already_has_fails_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // Added in qBittorrent itself, outside the app (MAM-13's decision 4): never adopted as this job's.
        var added = _qbittorrent.AddTorrent(SampleHashes(), "Interstellar", "Movies");

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, AlreadyHasError, SampleHashes(), cancellationToken);
        Assert.Equal(
        [
            "POST /api/v2/auth/login",
            "GET /api/v2/app/version",
            "GET /api/v2/torrents/categories",
            "GET /api/v2/app/version",
            $"GET /api/v2/torrents/info?hashes={SampleInfoHash}",
        ],
            QBittorrentCalls());
        Assert.Empty(_qbittorrent.AddRequests);
        Assert.Equal([added], _qbittorrent.Torrents);
    }

    [Fact]
    public async Task Create_whose_lookup_of_what_qbittorrent_has_fails_fails_with_its_sentence_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // Without the list, the app can't tell a torrent the owner added from one it may add.
        _qbittorrent.Intercept = request => IsLookup(request) ? VendorResponses.Text("", HttpStatusCode.InternalServerError) : null;

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, ClientFailedError, SampleHashes(), cancellationToken);
        Assert.Equal($"GET /api/v2/torrents/info?hashes={SampleInfoHash}", QBittorrentCalls()[^1]);
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, IsAdd);
    }

    // ── the one submission ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("v5.2.3", AddedNothingError)]
    [InlineData("v5.1.4", AddFailedError)]
    public async Task Create_refused_by_qbittorrent_fails_with_its_sentence_and_sends_once(string version, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        _qbittorrent.Version = version;
        // Added in qBittorrent between the app's look and its add: qBittorrent itself refuses the add, 409
        // from 5.2 and "Fails." before — a no, so nothing is looked up after it or adopted.
        _qbittorrent.Intercept = request =>
        {
            if (IsAdd(request) && _qbittorrent.Torrents.Count == 0)
                _qbittorrent.AddTorrent(SampleHashes(), "Interstellar", "Movies");
            return null;
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, expectedError, SampleHashes(), cancellationToken);
        Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal("POST /api/v2/torrents/add", QBittorrentCalls()[^1]);
        Assert.Single(_qbittorrent.Torrents);
    }

    [Fact]
    public async Task Create_whose_add_answered_500_but_listed_the_torrent_records_it_submitted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // qBittorrent took the torrent, then failed to answer: the answer is not a no (house rule 8).
        _qbittorrent.Intercept = request =>
        {
            if (!IsAdd(request))
                return null;
            _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(SampleHashes(), "Interstellar", "Movies", "downloading", 0));
            return VendorResponses.Text("", HttpStatusCode.InternalServerError);
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Submitted, SampleInfoHash, null, SampleHashes(), cancellationToken);
        // Sent once, never again; then its list read for the torrent.
        Assert.Equal(
        [
            "POST /api/v2/auth/login",
            "GET /api/v2/app/version",
            "GET /api/v2/torrents/categories",
            "GET /api/v2/app/version",
            $"GET /api/v2/torrents/info?hashes={SampleInfoHash}",
            "GET /api/v2/app/version",
            "POST /api/v2/torrents/add",
            "GET /api/v2/app/version",
            $"GET /api/v2/torrents/info?hashes={SampleInfoHash}",
        ],
            QBittorrentCalls());
    }

    [Fact]
    public async Task Create_whose_add_answered_500_and_listed_the_torrent_only_on_the_second_look_records_it_submitted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        var added = false;
        var lookupsSinceAdd = 0;
        _qbittorrent.Intercept = request =>
        {
            if (IsAdd(request))
            {
                // qBittorrent took the torrent, then failed to answer.
                _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(SampleHashes(), "Interstellar", "Movies", "downloading", 0));
                added = true;
                return VendorResponses.Text("", HttpStatusCode.InternalServerError);
            }

            // A qBittorrent before 5.2 lists a torrent only once it has loaded it, a moment after it took it.
            return added && IsLookup(request) && ++lookupsSinceAdd == 1
                ? VendorResponses.Text("[]", HttpStatusCode.OK, "application/json")
                : null;
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Submitted, SampleInfoHash, null, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        // Looked for once before the add, then twice after it: the second look found it.
        Assert.Equal(3, _factory.QBittorrentHttp.Requests.Count(IsLookup));
    }

    [Fact]
    public async Task Create_whose_add_answered_500_and_listed_nothing_fails_with_the_500_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        _qbittorrent.Intercept = request => IsAdd(request) ? VendorResponses.Text("", HttpStatusCode.InternalServerError) : null;

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, ClientFailedError, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        // Looked for once before the add and twice after it, half a second apart: not listed, it wasn't taken.
        Assert.Equal(3, _factory.QBittorrentHttp.Requests.Count(IsLookup));
        Assert.True(IsLookup(_factory.QBittorrentHttp.Requests[^1]));
        Assert.Empty(_qbittorrent.Torrents);
    }

    [Fact]
    public async Task Create_whose_add_answered_500_and_whose_list_then_failed_fails_with_the_500_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        var addSent = false;
        _qbittorrent.Intercept = request =>
        {
            if (IsAdd(request))
            {
                addSent = true;
                return VendorResponses.Text("", HttpStatusCode.InternalServerError);
            }

            if (addSent && IsLookup(request))
                throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
            return null;
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        // Whether it was taken can't be told, so the job keeps the add's own answer, not the lookup's.
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, ClientFailedError, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        Assert.True(IsLookup(_factory.QBittorrentHttp.Requests[^1]));
    }

    [Fact]
    public async Task Create_whose_add_met_a_refused_session_fails_and_never_sends_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // qBittorrent's recorded answer to a session it doesn't know, to the add alone: a read is renewed and
        // sent again, the add never (house rule 8).
        _qbittorrent.Intercept = request => IsAdd(request) ? VendorResponses.Recorded(FakeQBittorrentServer.ExpiredSessionResponseFile) : null;

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, SessionRefusedError, SampleHashes(), cancellationToken);
        // The refused session is dropped: the lookup after the add signs in afresh, and nothing is added again;
        // not listed, it is looked for once more on the session that lookup opened.
        Assert.Equal(
        [
            "GET /api/v2/app/version",
            "POST /api/v2/torrents/add",
            "POST /api/v2/auth/login",
            "GET /api/v2/app/version",
            $"GET /api/v2/torrents/info?hashes={SampleInfoHash}",
            "GET /api/v2/app/version",
            $"GET /api/v2/torrents/info?hashes={SampleInfoHash}",
        ],
            QBittorrentCalls()[^7..]);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
    }

    [Fact]
    public async Task Create_taken_without_an_id_whose_list_then_failed_records_it_submitted_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // Before 5.2: "Ok." and no id. The client said it took it, so a list it can't give after doesn't undo that.
        _qbittorrent.Version = "v5.1.4";
        _qbittorrent.Intercept = request =>
            _qbittorrent.AddRequests.Count > 0 && IsLookup(request) ? VendorResponses.Text("", HttpStatusCode.InternalServerError) : null;

        //Act
        var response = await SendAsync(release);

        //Assert
        // Taken without an id, with its info hash for MAM-14 to find it by.
        await AssertJobAsync(response, DownloadJobStatus.Submitted, null, null, SampleHashes(), cancellationToken);
        Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal(SampleInfoHash, Assert.Single(_qbittorrent.Torrents).Hash);
        // Once before the add and once after it: a list that failed isn't asked for again.
        Assert.Equal(2, _factory.QBittorrentHttp.Requests.Count(IsLookup));
    }

    [Theory]
    [MemberData(nameof(WaysAnAnswerNeverCame))]
    public async Task Create_whose_add_answer_never_came_but_listed_the_torrent_records_it_submitted(string failure)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        _qbittorrent.Intercept = request =>
        {
            if (!IsAdd(request))
                return null;
            _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(SampleHashes(), "Interstellar", "Movies", "downloading", 0));
            return AnswerThatNeverCame(failure);
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Submitted, SampleInfoHash, null, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        Assert.True(IsLookup(_factory.QBittorrentHttp.Requests[^1]));
    }

    [Theory]
    [MemberData(nameof(AnswersThatNeverCame))]
    public async Task Create_whose_add_answer_never_came_and_listed_nothing_fails_with_its_sentence(string failure, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        _qbittorrent.Intercept = request => IsAdd(request) ? AnswerThatNeverCame(failure) : null;

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, expectedError, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        Assert.True(IsLookup(_factory.QBittorrentHttp.Requests[^1]));
        Assert.Empty(_qbittorrent.Torrents);
    }

    // MAM-13 — qBittorrent names a torrent by its hash, so two jobs for one hash take a client that lists one
    // torrent twice under two ids; the core's rule then has the job linked by hand rather than guessed —
    // after an add it answered (here 5.1's "Ok.", without an id) and after one whose answer never came alike.
    [Theory]
    [InlineData("answered Ok.")]
    [InlineData("answered 500")]
    public async Task Create_with_two_jobs_listed_for_the_hash_needs_linking(string answer)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        _qbittorrent.Version = "v5.1.4";
        var addSent = false;
        _qbittorrent.Intercept = request =>
        {
            if (IsAdd(request))
            {
                addSent = true;
                return answer == "answered 500" ? VendorResponses.Text("", HttpStatusCode.InternalServerError) : null;
            }

            return addSent && IsLookup(request)
                ? Listing((SampleInfoHash, SampleInfoHash), ("0123456789abcdef0123456789abcdef01234567", SampleInfoHash))
                : null;
        };

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.NeedsLinking, null, null, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        // Once before the add and once after it: an ambiguous list is the answer, not a reason to look again.
        Assert.Equal(2, _factory.QBittorrentHttp.Requests.Count(IsLookup));
    }

    [Fact]
    public async Task Create_taken_without_an_id_or_an_info_hash_needs_linking()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two different v1 topics in one link: the app picks neither, so it knows no hash to look for.
        var release = await FindReleaseAsync(
            locator: "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&xt=urn:btih:89abcdef0123456789abcdef0123456789abcdef&dn=Interstellar",
            payloadKind: ReleasePayloadKind.Magnet);
        // 5.2's answer to a release it took but hasn't added yet: no id.
        _qbittorrent.Intercept = request => IsAdd(request) ? VendorResponses.Text("", HttpStatusCode.Accepted) : null;

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertJobAsync(response, DownloadJobStatus.NeedsLinking, null, null, TorrentInfoHashes.None, cancellationToken);
        // A magnet is the payload itself: nothing fetched. And no hash, so no list read before or after.
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, IsLookup);
    }

    [Fact]
    public async Task Create_of_a_torrent_file_qbittorrent_can_not_read_fails_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // A .torrent by its shape — a dictionary with an info key — whose info is no dictionary: neither the
        // app nor qBittorrent can read a hash from it.
        _feed.TorrentFile = Encoding.ASCII.GetBytes("d4:infoi1ee");

        //Act
        var response = await SendAsync(release);

        //Assert
        // qBittorrent's 415 added nothing; with no hash there is no list to read before or after.
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, UnreadableTorrentError, TorrentInfoHashes.None, cancellationToken);
        var add = Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal(_feed.TorrentFile, add.TorrentFile);
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, IsLookup);
        Assert.Empty(_qbittorrent.Torrents);
    }

    [Fact]
    public async Task Create_whose_add_threw_something_unforeseen_fails_and_logs_the_type_never_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // Not a transport failure the adapter maps: something nobody foresaw, as a parser bug would be.
        _qbittorrent.Intercept = request =>
        {
            if (IsAdd(request))
                throw new InvalidOperationException(PlantedExceptionMessage);
            return null;
        };
        _factory.Logs.Clear();

        //Act
        var response = await SendAsync(release);

        //Assert
        // An answer the app couldn't read may have been taken: the list was read, and it wasn't there.
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, UnforeseenAnswerError, SampleHashes(), cancellationToken);
        Assert.Single(_factory.QBittorrentHttp.Requests, IsAdd);
        Assert.True(IsLookup(_factory.QBittorrentHttp.Requests[^1]));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(DownloadSender).FullName && entry.Level == LogLevel.Error);
        Assert.StartsWith(
            $"The submission of a download through integration {_client.Id} (qbittorrent) threw System.InvalidOperationException",
            error.Message,
            StringComparison.Ordinal);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
    }

    // ── the job's error ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_error_is_longer_than_its_column_stores_it_cut_with_an_ellipsis()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync();
        // A client behind a reverse proxy with a long base path: the transport sentence names the address.
        var address = $"http://seedbox.test:8080/{new string('p', 1000)}";
        var seedbox = new FakeQBittorrentServer(address) { Unreachable = true };
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, seedbox);
        var client = await CreateClientAsync("Seedbox", address);
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Movies", "Movies");
        var sentence =
            $"Couldn't reach qBittorrent at {address}: nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
        var expectedError = string.Concat(sentence.AsSpan(0, 999), "…");

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, client.Id, destination.Id));

        //Assert
        Assert.Equal(1000, expectedError.Length);
        await AssertJobAsync(response, DownloadJobStatus.Failed, null, expectedError, SampleHashes(), cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A qBittorrent that signs in with the fake server's credentials and maps its /downloads to the app's
    // /media, as ADR 0003's reference installation does.
    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, string address) =>
        _integrations.Create(
            "qbittorrent",
            name,
            address,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    // A Toloka release of the owner's search from a moment ago: a .torrent behind the feed's download link
    // unless a test gives a magnet.
    private Task<ReleaseEntity> FindReleaseAsync(
        string title = "Interstellar (2014) BDRip 1080p", string? locator = null, ReleasePayloadKind? payloadKind = null) =>
        _releases.CreateFound(_owner.User, _toloka, title, locator, payloadKind: payloadKind);

    // The owner's send of the release to qBittorrent's Movies, under a request id of its own.
    private Task<HttpResponseMessage> SendAsync(ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _destination.Id));

    /// <summary>
    /// 201 with the job ended as given, the same on its row — written at the clock's one instant, with the
    /// hashes worked out from the payload — and its one audit row, whatever came of the sending.
    /// </summary>
    private async Task<DownloadJobDto> AssertJobAsync(
        HttpResponseMessage response,
        DownloadJobStatus status,
        string? clientJobId,
        string? error,
        TorrentInfoHashes hashes,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal($"/api/downloads/{job.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal((status, clientJobId, error), (job.Status, job.ClientJobId, job.Error));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        Assert.Equal((now, now), (job.CreatedUtc, job.StatusChangedUtc));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(job.Id, row.Id);
            Assert.Equal((status, clientJobId, error), (row.Status, row.ClientJobId, row.Error));
            Assert.Equal((hashes.V1, hashes.V2), (row.InfoHashV1, row.InfoHashV2));
            Assert.Equal(now, row.StatusChangedUtc);
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.DownloadRequested, (AuditTargetKind?)AuditTargetKind.DownloadJob, (Guid?)job.Id, (Guid?)_owner.User.Id),
                (audit.Action, audit.TargetKind, audit.TargetId, audit.ActorUserId));
        });
        return job;
    }

    // Every request qBittorrent got, as "METHOD /path?query".
    private string[] QBittorrentCalls() =>
        [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    private static bool IsAdd(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/torrents/add";

    private static bool IsAdd(RecordedHttpRequest request) =>
        request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/v2/torrents/add";

    private static bool IsLookup(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v2/torrents/info";

    private static bool IsLookup(RecordedHttpRequest request) =>
        request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/v2/torrents/info";

    // The hashes of the feed's default .torrent.
    private static TorrentInfoHashes SampleHashes() => TorrentInfoHashReader.FromTorrentFile(TorznabSamples.TorrentFile());

    /// <summary>
    /// An add whose answer leaves open whether qBittorrent took it: the client's own timeout running out (a
    /// cancellation nobody asked for), the connection lost before the answer, or an answer that isn't
    /// qBittorrent's.
    /// </summary>
    private static HttpResponseMessage AnswerThatNeverCame(string failure) => failure switch
    {
        "timed out" => throw new TaskCanceledException(),
        "connection lost" => throw new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely."),
        "unreadable answer" => VendorResponses.Text("<html><body>Bad gateway</body></html>", HttpStatusCode.OK, "text/html"),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
    };

    /// <summary><c>torrents/info</c> as 5.2 lists the torrents: each by its id and the v1 hash it holds.</summary>
    private static HttpResponseMessage Listing(params (string Hash, string InfoHashV1)[] torrents) =>
        VendorResponses.Text(
            new JsonArray([
                .. torrents.Select(torrent => (JsonNode)new JsonObject
                {
                    ["category"] = "Movies",
                    ["content_path"] = "/downloads/movies/Interstellar",
                    ["hash"] = torrent.Hash,
                    ["infohash_v1"] = torrent.InfoHashV1,
                    ["infohash_v2"] = "",
                    ["name"] = "Interstellar",
                    ["progress"] = 0,
                    ["state"] = "downloading",
                }),
            ]).ToJsonString(),
            HttpStatusCode.OK,
            "application/json");
}
