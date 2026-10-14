using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
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

// MAM-30, ADR 0039, ADR 0013 — sending a release to a real Transmission, end to end: the real adapter over HTTP to a
// FakeTransmissionServer answering as 4.1.3 does. One send is one torrent-add — the .torrent as metainfo or the magnet as
// filename, the destination's folder as download-dir, its name as the torrent's one label, paused false — on a session id
// a session-get has just confirmed, and the job is followed by the hashString Transmission answers. A 409 between the two
// (the id rotated) sends the add once more and never a third time (D8); a torrent Transmission already has is refused
// with where it is and its labels, or without them when that read fails (D9); a BitTorrent v2-only release is refused
// before Transmission is asked anything (D12); Transmission's own refusals, its temporary folder (D6), an address nothing
// answers at and refused credentials end the job with their sentences, the refused credentials never sent again until a
// connection test gets through with them (D7); an add whose answer never came is looked up, never sent again. Members
// read their jobs without Transmission's folders and Transmission's sentences, which can name them (D13). The Api's own
// rules over a stand-in client are DownloadsSendFolderClientHttpTests'.
public sealed class DownloadsSendTransmissionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string IntegrationsUrl = "/api/settings/integrations";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string ShowsFolder = "/downloads/tv-shows";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string SessionRefusedError = "Transmission refused the session id it had just sent.";
    private const string DuplicateError =
        "Transmission already has this torrent, in “/downloads/tv-shows” with the label “Серіали”, so it added nothing.";
    private const string DuplicateUnreadError = "Transmission already has this torrent, so it added nothing.";
    private const string V2OnlyError =
        "“Transmission” can't take a torrent that is BitTorrent v2 only, which this release is, so nothing was sent.";
    private const string AddRefusedError = "Transmission refused the torrent: invalid or corrupt torrent file.";
    private const string TemporaryFolderError =
        "Transmission keeps unfinished downloads in its temporary folder “/downloads/incomplete”, where the app can't follow them. In Transmission's web interface open Edit preferences, Torrents, untick “Use temporary folder”, then test again.";
    private const string CredentialsRefusedError = "Transmission refused the user name or password.";
    private const string RememberedCredentialsRefusedError =
        "Transmission refused the user name or password the last time the app used them, so the app won't send them again: with its anti-brute-force setting on, every refusal counts towards locking its RPC. Correct them under Integrations, or test the connection there once Transmission accepts them.";
    private const string UnreachableError =
        "Couldn't reach Transmission at http://transmission.test:9091: nothing accepted the connection. Check the address and port, and that Transmission is running.";
    private const string TimedOutError = "Transmission at http://transmission.test:9091 didn't answer within 15 seconds.";
    private const string FolderUnusableError =
        "“Transmission” can't use the folder of the destination “Серіали” any more, so nothing was sent. The owner checks the destination under Destinations in the user menu.";
    private const string MembersClientError =
        "“Transmission” couldn't take the release, so nothing was sent. Ask the owner to check “Transmission” under Integrations.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeTransmissionServer _transmission = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private byte[] _torrent = null!;
    private string _hash = null!;

    public DownloadsSendTransmissionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _torrent = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1));
        _hash = TorrentInfoHashReader.FromTorrentFile(_torrent).V1!;
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey) { TorrentFile = _torrent };
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", "/media/movies");
        _transmission = new FakeTransmissionServer();
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_factory.TransmissionHttp);
        // Transmission's /downloads is the app's /media: the destination Фільми is the library's folder.
        _client = await integrations.Create(
            "transmission",
            "Transmission",
            _transmission.BaseUrl,
            userName: FakeTransmissionServer.DefaultUserName,
            password: FakeTransmissionServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── sent ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_sends_one_torrent_add_with_the_torrent_its_folder_its_label_and_paused_false_and_follows_the_hash_transmission_answers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, (string?)_hash, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked, (DownloadDestinationKind?)DownloadDestinationKind.Folder),
            (job.Status, job.ClientJobId, job.OverwriteCheck, job.DestinationKind));
        var row = await JobRowAsync(cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, (string?)_hash, (string?)_hash, MoviesFolder, (DownloadDestinationKind?)DownloadDestinationKind.Folder),
            (row.Status, row.ClientJobId, row.InfoHashV1, row.ClientDestinationKey, row.DestinationKind));
        // Exactly these four arguments, nothing else: no files left out, no priorities, no start setting of the owner's.
        var add = Assert.Single(_transmission.AddRequests);
        Assert.Equal(["metainfo", "download-dir", "labels", "paused"], add.ArgumentNames);
        Assert.Equal(
            ((ReleasePayloadKind?)ReleasePayloadKind.TorrentFile, (string?)MoviesFolder, (bool?)false),
            (add.Kind, add.DownloadDir, add.Paused));
        Assert.Equal(_torrent, add.Metainfo);
        Assert.Equal(["Фільми"], add.Labels!);
        // Its destination's folder listed (Transmission's own and the library's, one folder), the torrent looked up, the
        // folder's torrents read for the overwrite check, then the one add — each call on a session just confirmed.
        Assert.Equal(
            ["session-get", "free-space", "free-space", "session-get", "torrent-get", "session-get", "torrent-get", "session-get", "torrent-add"],
            _transmission.Calls.Select(call => call.Method));
        Assert.Equal(["torrent-add"], _transmission.Writes.Select(call => call.Method));
        var torrent = Assert.Single(_transmission.Torrents);
        Assert.Equal((_hash, MoviesFolder, 4), (torrent.HashString, torrent.DownloadDir, torrent.Status));
    }

    [Fact]
    public async Task Create_of_a_magnet_sends_the_link_as_the_filename_and_follows_its_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string Hash = "c9e15763f722f23e98a29decdfae341b98d53056";
        var magnet = $"magnet:?xt=urn:btih:{Hash}&dn=Film.2001.mkv";
        var release = await _releases.CreateFound(_owner.User, _toloka, "Film (2001) WEBRip", magnet, payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, (string?)Hash, (DownloadOverwriteCheck?)DownloadOverwriteCheck.NotCheckedMagnet),
            (job.Status, job.ClientJobId, job.OverwriteCheck));
        var add = Assert.Single(_transmission.AddRequests);
        Assert.Equal(["filename", "download-dir", "labels", "paused"], add.ArgumentNames);
        Assert.Equal(((ReleasePayloadKind?)ReleasePayloadKind.Magnet, (string?)magnet, (string?)MoviesFolder), (add.Kind, add.Filename, add.DownloadDir));
        Assert.Equal(["Фільми"], add.Labels!);
    }

    [Fact]
    public async Task Create_whose_session_id_rotated_before_the_add_sends_the_add_once_more_with_the_id_the_409_hands_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        // Transmission restarts between the session-get and the add: the add is answered 409, unread.
        var rotated = false;
        _transmission.Intercept = (_, call) =>
        {
            if (call?.Method == "torrent-add" && !rotated)
            {
                rotated = true;
                _transmission.RotateSession();
            }

            return null;
        };

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        // D8 — Transmission answers a 409 before it reads the body, so the second add is the only one it read.
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Submitted, (string?)_hash), (job.Status, job.ClientJobId));
        var adds = _factory.TransmissionHttp.Requests.Where(request => FakeTransmissionServer.Describe(request) == "torrent-add").ToList();
        Assert.Equal(2, adds.Count);
        Assert.Equal([_transmission.SessionId], adds[1].HeaderValues(FakeTransmissionServer.SessionIdHeader));
        Assert.Single(_transmission.AddRequests);
        Assert.Single(_transmission.Torrents);
    }

    // ── refused ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_add_transmission_answers_409_twice_fails_and_is_never_sent_a_third_time_or_looked_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        _transmission.Intercept = (_, call) =>
        {
            if (call?.Method == "torrent-add")
                _transmission.RotateSession();
            return null;
        };

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        // D8 — nothing was read, so nothing was taken: refused, never "may have been taken" and looked for.
        await AssertFailedAsync(response, SessionRefusedError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
        Assert.Equal(2, _factory.TransmissionHttp.Requests.Count(request => FakeTransmissionServer.Describe(request) == "torrent-add"));
        Assert.Equal("torrent-add", FakeTransmissionServer.Describe(_factory.TransmissionHttp.Requests[^1]));
        Assert.Empty(_transmission.AddRequests);
    }

    [Fact]
    public async Task Create_of_a_torrent_transmission_got_meanwhile_fails_saying_where_it_is_and_with_which_label()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        // Added in Transmission by hand right after the app looked it up, into another folder with its own label.
        AddedMeanwhile();

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        // D9 — Transmission applied nothing of this add to the torrent it has: never adopted as this job.
        await AssertFailedAsync(response, DuplicateError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
        var held = Assert.Single(_transmission.Torrents);
        Assert.Equal(ShowsFolder, held.DownloadDir);
        Assert.Equal(["Серіали"], held.Labels);
        // Its folder and labels read once, by its hash, to say so.
        var read = _transmission.Calls[^1];
        Assert.Equal(
            ("torrent-get", $"[\"{_hash}\"]", """["hashString","downloadDir","labels"]"""),
            (read.Method, read.Arguments["ids"]!.ToJsonString(), read.Arguments["fields"]!.ToJsonString()));
    }

    [Fact]
    public async Task Create_of_a_torrent_transmission_got_meanwhile_fails_without_where_it_is_when_that_read_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        AddedMeanwhile();
        _transmission.RpcIntercept = call =>
            call.Method == "torrent-get" && call.Arguments["fields"] is JsonArray fields && fields.Any(field => field?.GetValue<string>() == "labels")
                ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "planted refusal" }
                : null;

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        await AssertFailedAsync(response, DuplicateUnreadError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_bittorrent_v2_only_release_fails_before_transmission_is_asked_anything()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _feed.TorrentFile = TestTorrent.V2("Film.2001.mkv", [(["Film.2001.mkv"], TestTorrent.Bytes(10, seed: 2))]);
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release, _movies);

        //Assert
        // D12 — what the release is, in the app's own words, which the member reads as they are.
        await AssertFailedAsync(response, V2OnlyError, DownloadJobErrorOrigin.App, cancellationToken);
        Assert.Empty(_factory.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task Create_whose_add_transmission_refuses_fails_with_its_reason_and_is_not_looked_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        _transmission.RpcIntercept = call =>
            call.Method == "torrent-add" ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "invalid or corrupt torrent file" } : null;

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        await AssertFailedAsync(response, AddRefusedError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
        Assert.Equal("torrent-add", _transmission.Calls[^1].Method);
        Assert.Empty(_transmission.Torrents);
    }

    [Fact]
    public async Task Create_to_a_transmission_keeping_unfinished_downloads_in_its_temporary_folder_fails_saying_how_to_turn_it_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        _transmission.IncompleteDirEnabled = true;

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        // D6 — the app couldn't follow the download to its folder, so nothing goes.
        await AssertFailedAsync(response, TemporaryFolderError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
        Assert.Equal(["session-get"], _transmission.Calls.Select(call => call.Method));
    }

    [Fact]
    public async Task Create_to_a_transmission_nothing_answers_at_fails_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        _transmission.Unreachable = true;

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        await AssertFailedAsync(response, UnreachableError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
    }

    [Fact]
    public async Task Create_after_transmission_refused_the_password_sends_it_no_more_until_a_connection_test_gets_through()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Transmission's password changed there; the integration still holds the old one.
        _transmission.Password = "changed-in-transmission";
        var first = await FindReleaseAsync(_owner);
        var second = await FindReleaseAsync(_owner);
        var third = await FindReleaseAsync(_owner);

        //Act
        var refused = await SendAsync(_owner, first, _movies);
        var requestsAfterRefusal = _factory.TransmissionHttp.Requests.Count;
        var remembered = await SendAsync(_owner, second, _movies);
        var requestsAfterRemembered = _factory.TransmissionHttp.Requests.Count;
        // The owner puts Transmission's password back, and tests the connection under Integrations.
        _transmission.Password = FakeTransmissionServer.DefaultPassword;
        var test = await _factory.PostHttpRequest($"{IntegrationsUrl}/{_client.Id}/test", _owner.AccessToken);
        var sent = await SendAsync(_owner, third, _movies);

        //Assert
        // D7 — one refusal, then nothing sent: with anti-brute-force on, every wrong password counts towards a lock.
        Assert.Equal((DownloadJobStatus.Failed, (string?)CredentialsRefusedError), await StatusAsync(refused, cancellationToken));
        Assert.Equal(1, requestsAfterRefusal);
        Assert.Equal((DownloadJobStatus.Failed, (string?)RememberedCredentialsRefusedError), await StatusAsync(remembered, cancellationToken));
        Assert.Equal(requestsAfterRefusal, requestsAfterRemembered);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Transmission 4.1.3."),
            await test.Content.ReadFromJsonAsync<ConnectionTestResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken));
        Assert.Equal((DownloadJobStatus.Submitted, (string?)null), await StatusAsync(sent, cancellationToken));
        Assert.Single(_transmission.AddRequests);
    }

    // ── an answer that never came ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_add_timed_out_and_transmission_lists_nothing_fails_and_never_sends_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        _transmission.Intercept = (_, call) => call?.Method == "torrent-add" ? throw new TaskCanceledException() : null;

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        // ADR 0013 — a write whose answer never came is looked for, not sent again: only a 409 is (D8).
        await AssertFailedAsync(response, TimedOutError, DownloadJobErrorOrigin.DownloadClient, cancellationToken);
        Assert.Single(_factory.TransmissionHttp.Requests, request => FakeTransmissionServer.Describe(request) == "torrent-add");
        Assert.Equal("torrent-get", FakeTransmissionServer.Describe(_factory.TransmissionHttp.Requests[^1]));
    }

    [Fact]
    public async Task Create_whose_add_timed_out_after_transmission_took_it_finds_it_by_its_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_owner);
        _transmission.Intercept = (_, call) =>
        {
            if (call?.Method != "torrent-add")
                return null;
            _transmission.AddTorrent(TorrentInfoHashReader.FromTorrentFile(_torrent), "Film.2001.mkv", MoviesFolder, ["Фільми"]);
            throw new TaskCanceledException();
        };

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Submitted, (string?)_hash, (string?)null), (job.Status, job.ClientJobId, job.Error));
        Assert.Single(_factory.TransmissionHttp.Requests, request => FakeTransmissionServer.Describe(request) == "torrent-add");
    }

    // ── what members see ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_of_a_members_transmission_job_gives_no_folder_while_the_owners_own_job_gives_its_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var membersJob = await ReadJobAsync(await SendAsync(_member, await FindReleaseAsync(_member), _movies), cancellationToken);
        _transmission.RemoveTorrent(_hash);
        var ownersJob = await ReadJobAsync(await SendAsync(_owner, await FindReleaseAsync(_owner), _movies), cancellationToken);

        //Act
        var member = await _factory.GetHttpRequest($"{BaseUrl}/{membersJob.Id}", _member.AccessToken);
        var owner = await _factory.GetHttpRequest($"{BaseUrl}/{ownersJob.Id}", _owner.AccessToken);

        //Assert
        // D13 — the folder is the owner's business, as his path mappings are.
        var read = await ReadJobAsync(member, cancellationToken);
        Assert.Equal(("Фільми", (string?)null, DownloadJobStatus.Submitted), (read.DestinationName, read.ClientDestinationKey, read.Status));
        Assert.Equal(MoviesFolder, (await ReadJobAsync(owner, cancellationToken)).ClientDestinationKey);
    }

    [Fact]
    public async Task Create_of_a_members_release_transmission_already_has_reads_the_apps_sentence_never_transmissions_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_member);
        AddedMeanwhile();

        //Act
        var response = await SendAsync(_member, release, _movies);

        //Assert
        // Transmission's own sentence names its folder: the member reads the app's, the row keeps Transmission's.
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, (string?)MembersClientError), (job.Status, job.Error));
        Assert.Equal(DuplicateError, (await JobRowAsync(cancellationToken)).Error);
    }

    [Fact]
    public async Task Create_to_a_folder_transmission_can_not_see_fails_naming_the_destination_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Saved while Transmission saw it; unmounted since.
        var shows = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Серіали", ShowsFolder);
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release, shows);

        //Assert
        // D5 — Transmission would create the folder in its own filesystem. D13 — the app's sentence names the destination.
        await AssertFailedAsync(response, FolderUnusableError, DownloadJobErrorOrigin.App, cancellationToken);
        Assert.Empty(_transmission.Writes);
        Assert.Contains(ShowsFolder, _transmission.Calls.Where(call => call.Method == "free-space").Select(call => call.Arguments["path"]!.GetValue<string>()));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<ReleaseEntity> FindReleaseAsync(AuthorizedUser user) => _releases.CreateFound(user.User, _toloka, "Film (2001) BDRip 1080p");

    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release, DestinationMappingEntity destination) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, destination.Id));

    /// <summary>The release's torrent added in Transmission by hand, into its series folder with its own label, right as
    /// the app's add comes in — after the app looked it up.</summary>
    private void AddedMeanwhile()
    {
        var added = false;
        _transmission.Intercept = (_, call) =>
        {
            if (call?.Method == "torrent-add" && !added)
            {
                added = true;
                _transmission.AddTorrent(TorrentInfoHashReader.FromTorrentFile(_torrent), "Film.2001.mkv", ShowsFolder, ["Серіали"]);
            }

            return null;
        };
    }

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static async Task<(DownloadJobStatus Status, string? Error)> StatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var job = await ReadJobAsync(response, cancellationToken);
        return (job.Status, job.Error);
    }

    /// <summary>The one job there is, as stored now.</summary>
    private async Task<DownloadJobEntity> JobRowAsync(CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken));
        return row!;
    }

    /// <summary>201 with the job Failed with <paramref name="error"/>, the same on its row with whose sentence it is, and
    /// nothing added to Transmission.</summary>
    private async Task AssertFailedAsync(HttpResponseMessage response, string error, DownloadJobErrorOrigin origin, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, (string?)error, (string?)null), (job.Status, job.Error, job.ClientJobId));
        var row = await JobRowAsync(cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, (string?)error, (DownloadJobErrorOrigin?)origin), (row.Status, row.Error, row.ErrorOrigin));
        Assert.DoesNotContain(_transmission.Torrents, torrent => torrent.DownloadDir == MoviesFolder);
    }
}
