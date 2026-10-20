using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-38, ADR 0034 — a send whose files would land on a different file already in the destination's folder is
// refused before qBittorrent is handed anything: Failed with the app's sentence naming the file, nothing added.
// The same bytes already there — a Toloka update re-adding a series' old episodes — are sent, the job "Checked",
// as is a folder that is only the same folder. A torrent qBittorrent already has in the category, whose files
// may not be on the disk yet, counts as already there. What the app can't look at is sent as before and said so
// on the job: a folder outside the Media folders or without a path in the app, a place behind a link leading out
// of them, a name the app can't look up, a magnet, a .torrent whose files it can't read, a qBittorrent that can't
// tell its torrents or their files. A place that can't be looked at never hides one that refuses the send. The check
// runs after qBittorrent is asked whether it already has the torrent, and only reads: every write to qBittorrent is
// the sign-in or the one add. The torrents are real (TestTorrent): the pieces the check compares hash as the planted
// bytes do.
public sealed class DownloadsSendOverwriteHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string Movies = "/media/movies";
    private const int Piece = TestTorrent.PieceLength;
    private const string PlantedExceptionMessage = "planted-exception-message-0038";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _destination = null!;

    public DownloadsSendOverwriteHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey);
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        // qBittorrent's Movies saves to /downloads/movies, which is the app's /media/movies.
        _client = await CreateClientAsync("qBittorrent", [new PathMappingPair("/downloads", "/media")]);
        _destination = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── refused ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_file_would_land_on_a_file_of_another_size_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: content.Length + 1));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
        Assert.Empty(_factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_whose_file_would_land_on_a_file_of_the_same_size_with_other_bytes_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        _factory.Files.AddContent($"{Movies}/Film.2001.mkv", TestTorrent.Bytes(content.Length, seed: 2));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: a file of the same size but other content is already there, so nothing was sent.",
            cancellationToken);
        Assert.Equal([$"{Movies}/Film.2001.mkv"], _factory.Files.PiecesHashedPaths);
        AssertOnlyReadsAnd(adds: 0);
    }

    [Fact]
    public async Task Create_whose_file_would_land_on_a_file_the_app_can_not_read_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.Files.AddProblem($"{Movies}/Film.2001.mkv", FileInventoryStatus.Unreadable, "The app may not read “/media/movies/Film.2001.mkv”.");
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: a file the app couldn't read to compare is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_would_land_on_a_file_of_the_same_size_the_app_can_not_read_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes((2 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        // Described as the torrent's size, but no bytes to read: the read of its pieces fails.
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: content.Length));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: a file the app couldn't read to compare is already there, so nothing was sent.",
            cancellationToken);
        Assert.Equal([$"{Movies}/Film.2001.mkv"], _factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_whose_file_changed_while_it_was_compared_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        // Described as the torrent's size, but shorter by the time its last piece is read.
        _factory.Files.AddContent(
            $"{Movies}/Film.2001.mkv",
            content[..(2 * Piece)],
            new FileFacts(content.Length, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 2049, 9_100_000, 1));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: the file already there changed while the app compared it, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_one_file_would_land_in_a_folder_of_its_name_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes(100, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        _factory.Files.AddFile($"{Movies}/Film.2001/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 5));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001/Film.2001.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_would_land_on_something_that_is_not_a_file_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.Files.AddProblem($"{Movies}/Film.2001.mkv", FileInventoryStatus.NotARegularFile, "“/media/movies/Film.2001.mkv” is a folder.");
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: something that isn't a file is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_several_files_collide_names_the_first_and_counts_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1(
            "Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1)), (["S01E02.mkv"], TestTorrent.Bytes(200, seed: 2))]);
        _factory.Files.AddFile($"{Movies}/Show/S01E01.mkv", TestMedia.File(inode: 1, sizeBytes: 7));
        _factory.Files.AddFile($"{Movies}/Show/S01E02.mkv", TestMedia.File(inode: 2, sizeBytes: 8));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E01.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent. One more of the release's files would land on a file already there too.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_one_file_collides_under_two_layouts_counts_it_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 7));
        _factory.Files.AddFile($"{Movies}/Film.2001/Film.2001.mkv", TestMedia.File(inode: 2, sizeBytes: 8));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        // Two places, one release file: no "one more".
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_later_file_lands_on_a_file_of_its_size_counts_it_as_already_there_without_reading_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var second = TestTorrent.Bytes((2 * Piece) + 3, seed: 2);
        _feed.TorrentFile = TestTorrent.V1(
            "Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1)), (["S01E02.mkv"], second)], padFiles: true);
        _factory.Files.AddFile($"{Movies}/Show/S01E01.mkv", TestMedia.File(inode: 1, sizeBytes: 7));
        _factory.Files.AddContent($"{Movies}/Show/S01E02.mkv", TestTorrent.Bytes(second.Length, seed: 3));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E01.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent. One more of the release's files would land on a file already there too.",
            cancellationToken);
        Assert.Empty(_factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_whose_file_collides_before_a_link_leading_out_of_the_media_folders_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1(
            "Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1)), (["S01E02.mkv"], TestTorrent.Bytes(200, seed: 2))]);
        _factory.Files.AddFile($"{Movies}/Show/S01E01.mkv", TestMedia.File(inode: 1, sizeBytes: 7));
        _factory.Files.AddProblem(
            $"{Movies}/Show/S01E02.mkv", FileInventoryStatus.OutsideAllowedRoots, "A link on the way to “/media/movies/Show/S01E02.mkv” leads out of the media folders.");
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E01.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_other_file_collides_fails_though_one_files_name_can_not_be_looked_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1(
            "Show", [(["S01E01\u0085.mkv"], TestTorrent.Bytes(100, seed: 1)), (["S01E02.mkv"], TestTorrent.Bytes(200, seed: 2))]);
        _factory.Files.AddFile($"{Movies}/Show/S01E02.mkv", TestMedia.File(inode: 1, sizeBytes: 7));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E02.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_a_torrent_qbittorrent_already_has_will_write_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        // Another release under the same name, queued: nothing of it is on the disk yet.
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
            new TorrentInfoHashes("1111111111111111111111111111111111111111", null), "Film.2001.mkv", "Movies", state: "queuedDL", progress: 0, savePath: "/downloads/movies"));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: the torrent “Film.2001.mkv” it already has will write there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_lies_in_the_folder_of_a_torrent_qbittorrent_already_has_and_lists_it_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("2222222222222222222222222222222222222222", null), "Show", "Movies", state: "downloading", progress: 0, savePath: "/downloads/movies")
            with { Files = [new FakeQBittorrentFile("Show/S01E01.mkv", 900, 0)] });
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E01.mkv” in the folder of the category “Movies”: the torrent “Show” it already has will write there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_file_a_torrent_still_in_qbittorrents_folder_for_incomplete_torrents_will_write_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        // Downloading into the folder for incomplete torrents, then moved to its save path once finished.
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("4444444444444444444444444444444444444444", null), "Show", "Movies", state: "downloading", progress: 0.3, savePath: "/downloads/movies")
            with { ContentPath = "/downloads/incomplete/Show", Files = [new FakeQBittorrentFile("Show/S01E01.mkv", 900, 0.3)] });
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E01.mkv” in the folder of the category “Movies”: the torrent “Show” it already has will write there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_whose_one_file_a_torrent_still_in_qbittorrents_folder_for_incomplete_torrents_will_write_fails_without_asking_its_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("5555555555555555555555555555555555555555", null), "Film.2001.mkv", "Movies", state: "downloading", progress: 0.3, savePath: "/downloads/movies")
            with { ContentPath = "/downloads/incomplete/Film.2001.mkv" });
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: the torrent “Film.2001.mkv” it already has will write there, so nothing was sent.",
            cancellationToken);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_whose_file_a_torrent_listed_after_more_than_20_others_will_write_fails_without_asking_any_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        PlantShowTorrents(21);
        // Last in the list: a torrent of one file saved straight to where the release's file goes.
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
            new TorrentInfoHashes("6666666666666666666666666666666666666666", null), "S01E01.mkv", "Movies", state: "queuedDL", progress: 0, savePath: "/downloads/movies/Show"));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Show/S01E01.mkv” in the folder of the category “Movies”: the torrent “S01E01.mkv” it already has will write there, so nothing was sent.",
            cancellationToken);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_of_a_torrent_qbittorrent_already_has_fails_before_the_overwrite_check_looks_at_anything()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes((2 * Piece) + 5, seed: 1);
        var torrent = TestTorrent.V1("Film.2001.mkv", content);
        _feed.TorrentFile = torrent;
        // The release's own torrent, queued at the very place its file goes, and the same bytes already there.
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
            TorrentInfoHashReader.FromTorrentFile(torrent), "Film.2001.mkv", "Movies", state: "queuedDL", progress: 0, savePath: "/downloads/movies"));
        _factory.Files.AddContent($"{Movies}/Film.2001.mkv", content);
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(response, "“qBittorrent” already has this torrent, so nothing was sent.", cancellationToken);
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_whose_overwrite_check_throws_fails_with_the_apps_sentence_and_logs_the_type_never_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var content = TestTorrent.Bytes((2 * Piece) + 5, seed: 1);
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", content);
        _factory.Files.AddContent($"{Movies}/Film.2001.mkv", content);
        // Not a failure the inventory answers: something nobody foresaw, as running out of memory would be.
        _factory.Files.DuringPiecesHash = (_, _) => throw new InvalidOperationException(PlantedExceptionMessage);
        var release = await FindReleaseAsync();
        _factory.Logs.Clear();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "The app couldn't check whether this release would write over a file in the folder of the category “Movies”, so nothing was sent. The app's log says why.",
            cancellationToken);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal(typeof(DownloadSender).FullName, error.Category);
        Assert.Contains("threw System.InvalidOperationException", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_refused_reads_the_same_sentence_for_the_member_who_sent_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 5));
        var release = await _releases.CreateFound(_member.User, _toloka, "Film (2001) BDRip 1080p");

        //Act
        var response = await SendAsync(_member, release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“qBittorrent” would write over “Film.2001.mkv” in the folder of the category “Movies”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    // ── sent, checked ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_files_already_there_are_the_same_bytes_is_sent_as_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var oldEpisode = TestTorrent.Bytes((2 * Piece) + 9, seed: 1);
        var newEpisode = TestTorrent.Bytes(Piece, seed: 2);
        // A Toloka update: the old episode again, byte for byte, and a new one.
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], oldEpisode), (["S01E02.mkv"], newEpisode)], padFiles: true);
        _factory.Files.AddContent($"{Movies}/Show/S01E01.mkv", oldEpisode);
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
        Assert.Equal([$"{Movies}/Show/S01E01.mkv"], _factory.Files.PiecesHashedPaths);
        AssertOnlyReadsAnd(adds: 1);
    }

    [Fact]
    public async Task Create_of_a_v2_torrent_whose_file_already_there_is_the_same_bytes_is_sent_as_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var episode = TestTorrent.Bytes((3 * Piece) + 1, seed: 3);
        _feed.TorrentFile = TestTorrent.V2("Show", [(["S01E01.mkv"], episode), (["S01E01.srt"], TestTorrent.Bytes(40, seed: 4))]);
        _factory.Files.AddContent($"{Movies}/Show/S01E01.mkv", episode);
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
        Assert.Equal([$"{Movies}/Show/S01E01.mkv"], _factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_of_a_v2_torrent_with_pieces_of_four_blocks_whose_files_already_there_are_the_same_bytes_is_sent_as_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        // Two and a half pieces, the last a tree as wide as a whole piece; and a file of two blocks, a tree of two.
        var episode = TestTorrent.Bytes((8 * Piece) + (2 * Piece), seed: 5);
        var subtitles = TestTorrent.Bytes(2 * Piece, seed: 6);
        _feed.TorrentFile = TestTorrent.V2("Show", [(["S01E01.mkv"], episode), (["S01E01.srt"], subtitles)], pieceLength: 4 * Piece);
        _factory.Files.AddContent($"{Movies}/Show/S01E01.mkv", episode);
        _factory.Files.AddContent($"{Movies}/Show/S01E01.srt", subtitles);
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
        Assert.Equal([$"{Movies}/Show/S01E01.mkv", $"{Movies}/Show/S01E01.srt"], _factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_whose_folder_alone_is_already_there_is_sent_as_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        _factory.Files.AddFile($"{Movies}/Show/Other.release.mkv", TestMedia.File(inode: 1, sizeBytes: 5));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
    }

    [Fact]
    public async Task Create_beside_a_torrent_whose_folder_holds_other_files_is_sent_as_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E09.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("3333333333333333333333333333333333333333", null), "Show", "Movies", savePath: "/downloads/movies")
            with { Files = [new FakeQBittorrentFile("Show/S01E01.mkv", 900, 1)] });
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.Checked, cancellationToken);
        Assert.Contains("GET /api/v2/torrents/files?hash=3333333333333333333333333333333333333333", QBittorrentCalls());
    }

    // ── sent, unchecked ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_whose_folder_is_outside_the_media_folders_is_sent_unchecked_and_looks_at_no_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.Files.AddFile($"{Movies}/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 5));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedFolderOutsideMediaFolders, cancellationToken);
        Assert.Empty(_factory.Files.DescribedPaths);
    }

    [Fact]
    public async Task Create_whose_file_lies_behind_a_link_leading_out_of_the_media_folders_is_sent_unchecked_and_reads_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _factory.Files.AddProblem(
            $"{Movies}/Film.2001.mkv", FileInventoryStatus.OutsideAllowedRoots, "A link on the way to “/media/movies/Film.2001.mkv” leads out of the media folders.");
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedFolderOutsideMediaFolders, cancellationToken);
        Assert.Empty(_factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Create_whose_file_name_holds_a_character_the_app_can_not_look_up_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        // DEL: libtorrent keeps it in a name on Linux, and the app's paths refuse every control character.
        _feed.TorrentFile = TestTorrent.V1("Film\u007F2001.mkv", TestTorrent.Bytes(100, seed: 1));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedFileName, cancellationToken);
        Assert.Empty(_factory.Files.DescribedPaths);
    }

    [Fact]
    public async Task Create_whose_category_folder_has_no_path_in_the_app_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        // A pair that covers none of qBittorrent's folders: /downloads/movies has no app path.
        var client = await CreateClientAsync("qBittorrent elsewhere", [new PathMappingPair("/elsewhere", "/media")]);
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Films", "Movies");
        var release = await FindReleaseAsync();

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, client.Id, destination.Id));

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedFolderUnknown, cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_magnet_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        var release = await _releases.CreateFound(
            _owner.User,
            _toloka,
            "Film (2001) WEBRip",
            "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Film",
            payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedMagnet, cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_torrent_whose_files_the_app_can_not_read_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        // Hashable, so qBittorrent takes it, but two pieces' hashes for one byte: no list of files the app trusts.
        _feed.TorrentFile = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L), ("name", "Film.2001.mkv"), ("piece length", (long)Piece), ("pieces", SHA1.HashData(new byte[2])[..20].Concat(new byte[20]).ToArray())))));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedFileList, cancellationToken);
    }

    [Fact]
    public async Task Create_when_qbittorrent_can_not_list_its_torrents_in_the_category_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/info"
            && request.RequestUri.Query.StartsWith("?category=", StringComparison.Ordinal)
                ? VendorResponses.Text("boom", HttpStatusCode.InternalServerError)
                : null;
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedClientJobs, cancellationToken);
    }

    [Fact]
    public async Task Create_when_a_torrent_qbittorrent_has_in_the_category_has_no_metadata_yet_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(100, seed: 1));
        // A magnet still fetching its metadata: qBittorrent lists no content path and no files for it yet.
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("7777777777777777777777777777777777777777", null), "Some magnet", "Movies", state: "metaDL", progress: 0, savePath: "/downloads/movies")
            with { ContentPath = "" });
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedClientJobs, cancellationToken);
    }

    [Fact]
    public async Task Create_when_qbittorrent_can_not_list_a_torrents_files_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("8888888888888888888888888888888888888888", null), "Show", "Movies", state: "downloading", progress: 0, savePath: "/downloads/movies")
            with { Files = [new FakeQBittorrentFile("Show/S01E01.mkv", 900, 0)] });
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/files"
            ? VendorResponses.Text("boom", HttpStatusCode.InternalServerError)
            : null;
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedClientJobs, cancellationToken);
    }

    [Fact]
    public async Task Create_when_more_than_20_torrents_may_write_its_file_asks_for_the_files_of_20_and_is_sent_unchecked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        PlantShowTorrents(21);
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedClientJobs, cancellationToken);
        Assert.Equal(20, QBittorrentCalls().Count(call => call.StartsWith("GET /api/v2/torrents/files", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Create_when_reading_a_torrents_files_throws_is_sent_unchecked_and_logs_the_type_never_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await AllowMediaAsync();
        _feed.TorrentFile = TestTorrent.V1("Show", [(["S01E01.mkv"], TestTorrent.Bytes(100, seed: 1))]);
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes("9999999999999999999999999999999999999999", null), "Show", "Movies", state: "downloading", progress: 0, savePath: "/downloads/movies")
            with { Files = [new FakeQBittorrentFile("Show/S01E01.mkv", 900, 0)] });
        // Not a transport failure the adapter maps: something nobody foresaw, as a parser bug would be.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/files"
            ? throw new InvalidOperationException(PlantedExceptionMessage)
            : null;
        var release = await FindReleaseAsync();
        _factory.Logs.Clear();

        //Act
        var response = await SendAsync(_owner, release);

        //Assert
        await AssertSentAsync(response, DownloadOverwriteCheck.NotCheckedClientJobs, cancellationToken);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal(typeof(DownloadOverwriteGuard).FullName, error.Category);
        Assert.StartsWith(
            $"The overwrite check's read of the job files through integration {_client.Id} (qbittorrent) threw System.InvalidOperationException",
            error.Message,
            StringComparison.Ordinal);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, IReadOnlyList<PathMappingPair> pathMappings) =>
        _integrations.Create(
            "qbittorrent",
            name,
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: pathMappings);

    private Task AllowMediaAsync() => new AllowedRootSeeder(_factory.Scope).Allow("/media");

    private Task<ReleaseEntity> FindReleaseAsync() => _releases.CreateFound(_owner.User, _toloka, "Film (2001) BDRip 1080p");

    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _destination.Id));

    /// <summary>201 with the job Failed with the app's own <paramref name="error"/>, the same on its row with no
    /// check recorded, and nothing handed to qBittorrent.</summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, error, (DownloadOverwriteCheck?)null), (job.Status, job.Error, job.OverwriteCheck));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (DownloadJobStatus.Failed, error, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App, (DownloadOverwriteCheck?)null),
                (row.Status, TestTexts.En(row.Error), row.ErrorOrigin, row.OverwriteCheck));
        });
        Assert.Empty(_qbittorrent.AddRequests);
    }

    /// <summary>201 with the job Submitted, <paramref name="check"/> recorded on it and its row, and the release
    /// handed to qBittorrent once.</summary>
    private async Task AssertSentAsync(HttpResponseMessage response, DownloadOverwriteCheck check, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Submitted, (string?)null, (DownloadOverwriteCheck?)check), (job.Status, job.Error, job.OverwriteCheck));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((DownloadJobStatus.Submitted, (DownloadOverwriteCheck?)check), (row.Status, row.OverwriteCheck));
        });
        Assert.Single(_qbittorrent.AddRequests);
    }

    private string[] QBittorrentCalls() =>
        [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>
    /// ADR 0034's anti-consequence: the check only reads. Every request to qBittorrent is a GET, the sign-in, or
    /// one of <paramref name="adds"/> submissions — never a stop, a pause or a removal.
    /// </summary>
    private void AssertOnlyReadsAnd(int adds)
    {
        var writes = QBittorrentCalls().Where(call => !call.StartsWith("GET ", StringComparison.Ordinal) && call != "POST /api/v2/auth/login");
        Assert.Equal(Enumerable.Repeat("POST /api/v2/torrents/add", adds), writes);
    }

    /// <summary>
    /// <paramref name="count"/> torrents in Movies with a folder named Show, as the release has, each holding other
    /// episodes: each may write where the release's file goes, so each one's files are asked for.
    /// </summary>
    private void PlantShowTorrents(int count)
    {
        for (var index = 1; index <= count; index++)
        {
            var hash = index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture);
            _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), "Show", "Movies", savePath: "/downloads/movies")
                with { Files = [new FakeQBittorrentFile($"Show/S02E{index:00}.mkv", 900, 1)] });
        }
    }
}
