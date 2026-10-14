using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, ADR 0034, ADR 0039 — the check before a send to a client whose destinations are folders: the places are the
// torrent's own layout only (a folder client has no other), each with its unfinished name too (the client's .part, D11),
// a file there counted once however many of its places are taken; the torrents already queued are every torrent the
// client saves in that folder. A refusal names the destination, never its folder (D13). What the check couldn't look at
// is sent as before and said so. The torrents are real (TestTorrent).
public sealed class DownloadsSendOverwriteFolderClientHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string Movies = "/media/movies";
    private const string MoviesFolder = "/downloads/movies";
    private const int Piece = TestTorrent.PieceLength;

    private readonly FolderClientApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _destination = null!;

    public DownloadsSendOverwriteFolderClientHttpTests(FolderClientApiFactory factory) => _factory = factory;

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
        // Transmission's /downloads/movies is the app's /media/movies.
        _client = await integrations.Create(FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _destination = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.FolderClient.Arm();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── refused ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_file_would_land_on_a_file_of_another_size_fails_naming_the_destination_to_a_member_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: content.Length + 1));
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        // D13 — the app's sentence, the same for everyone, names the destination and the file below its folder.
        await AssertRefusedAsync(
            response,
            "“Transmission” would write over “Film.2001.mkv” in the folder of the destination “Фільми”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_would_land_on_its_unfinished_name_already_holding_another_file_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        // D11 — the client adopts a file already at the unfinished name and writes the torrent's pieces into it.
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv.part", TestMedia.File(inode: 1, sizeBytes: 100));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“Transmission” would write over “Film.2001.mkv.part” in the folder of the destination “Фільми”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_is_taken_at_its_name_and_its_unfinished_name_counts_that_file_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 100));
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv.part", TestMedia.File(inode: 2, sizeBytes: 200));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // One file of the release: no "one more of the release's files" for its second place.
        await AssertRefusedAsync(
            response,
            "“Transmission” would write over “Film.2001.mkv” in the folder of the destination “Фільми”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_a_torrent_queued_in_the_folder_will_write_fails_naming_that_torrent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var episode = TestTorrent.Bytes((2 * Piece) + 3, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], episode)]);
        // Queued, its files not on the disk yet, whatever its label: every torrent saving in the folder counts.
        _factory.FolderClient.AddTorrent(new FakeFolderTorrent(
            new string('1', 40), "Show", MoviesFolder, [new ClientJobFile("Show/S01E01.mkv", episode.Length, 0, true)], ClientJobState.Queued, 0));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“Transmission” would write over “Show/S01E01.mkv” in the folder of the destination “Фільми”: the torrent “Show” it already has will write there, so nothing was sent.",
            cancellationToken);
        Assert.Contains($"ListJobFiles {new string('1', 40)}", _factory.FolderClient.Calls);
    }

    [Fact]
    public async Task Create_whose_overwrite_check_throws_fails_with_the_apps_sentence_naming_the_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        // Its own bytes, so the check reads a piece — and the read breaks on a bug.
        _factory.Files.AddContent($"{Movies}/Film.2001.mkv", content);
        _factory.Files.DuringPiecesHash = (_, _) => throw new InvalidOperationException("planted-exception-message-0030");
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "The app couldn't check whether this release would write over a file in the folder of the destination “Фільми”, so nothing was sent. The app's log says why.",
            cancellationToken);
    }

    // ── sent ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_looks_only_where_the_client_writes_a_torrent_of_several_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var episode = TestTorrent.Bytes((2 * Piece) + 3, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], episode)]);
        // Where qBittorrent's NoSubfolder layout would write the episode: no place of a folder client's.
        _factory.Files.AddFile($"{Movies}/S01E01.mkv", TestMedia.File(inode: 1, sizeBytes: 100));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
        Assert.DoesNotContain($"{Movies}/S01E01.mkv", _factory.Files.DescribedPaths);
    }

    [Fact]
    public async Task Create_looks_only_where_the_client_writes_a_torrent_of_one_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        // Where qBittorrent's Subfolder layout would write it.
        _factory.Files.AddFile($"{Movies}/Film.2001/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 100));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
        Assert.Equal([$"{Movies}/Film.2001.mkv", $"{Movies}/Film.2001.mkv.part"], _factory.Files.DescribedPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Create_ignores_a_torrent_of_the_same_name_saving_in_another_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var episode = TestTorrent.Bytes((2 * Piece) + 3, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], episode)]);
        _factory.FolderClient.AddTorrent(new FakeFolderTorrent(
            new string('2', 40), "Show", "/downloads/tv-shows", [new ClientJobFile("Show/S01E01.mkv", episode.Length, 0, true)], ClientJobState.Queued, 0));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
    }

    [Fact]
    public async Task Create_when_the_client_can_not_list_the_folders_torrents_is_sent_and_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.FolderClient.DestinationJobsFailure = IntegrationCallResult<IReadOnlyList<ClientJob>>.Failed(
            IntegrationCallStatus.TimedOut, "Transmission didn't answer in time.");
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedClientJobs, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<ReleaseEntity> FindReleaseAsync(AuthorizedUser user) => _releases.CreateFound(user.User, _toloka, "Film (2001) BDRip 1080p");

    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _destination.Id));

    /// <summary>201 with the job Failed with the app's own <paramref name="error"/>, the same on its row with no check
    /// recorded, and nothing handed to the client.</summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, error, (DownloadOverwriteCheck?)null), (job.Status, job.Error, job.OverwriteCheck));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (DownloadJobStatus.Failed, error, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App),
                (row.Status, row.Error, row.ErrorOrigin));
        });
        Assert.Empty(_factory.FolderClient.Submissions);
    }

    /// <summary>201 with the job Submitted and <paramref name="check"/> recorded, the release handed to the client
    /// once.</summary>
    private async Task AssertSentAsync(HttpResponseMessage response, DownloadOverwriteCheck check, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Submitted, (string?)null, (DownloadOverwriteCheck?)check), (job.Status, job.Error, job.OverwriteCheck));
        Assert.Single(_factory.FolderClient.Submissions);
    }
}
