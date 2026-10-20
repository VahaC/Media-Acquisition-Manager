using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
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

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, ADR 0034 — the overwrite check of a release sent to a real Transmission, end to end: the real adapter over HTTP
// to a FakeTransmissionServer. Transmission writes a torrent's own layout only, and an unfinished file under its name with
// .part: a file already at that place refuses the send like one at the file's own; a torrent Transmission already has in
// the folder, its content placed the way that Transmission cleans names (D20 — 4.0 the portable way, 4.1 on Linux as
// they are), refuses a release that would write where it will. A .torrent whose places no reading can predict — a path
// in Windows-1251 bytes with no UTF-8 beside it — and a magnet in the folder still fetching its metadata, whose files
// nobody can tell yet, send it unchecked, said so on the job. The Download dialog's question
// before Send tells a folder Transmission can't see by a reason of its own: nothing is sent there at all. Each sentence
// names the destination, never its folder (D13). MAM-31 — a folder that is unknown says why, naming Transmission: it
// couldn't be reached (its sentence for the owner alone), or no path mapping brings the folder in. The guard's rules
// over a stand-in client are DownloadsSendOverwriteFolderClientHttpTests'.
public sealed class DownloadsSendOverwriteTransmissionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string QueuedHash = "0123456789abcdef0123456789abcdef01234567";
    private const int FileLength = (3 * TestTorrent.PieceLength) + 5;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeTransmissionServer _transmission = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsSendOverwriteTransmissionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey) { TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(FileLength, seed: 1)) };
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", "/media/movies");
        _transmission = new FakeTransmissionServer();
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_factory.TransmissionHttp);
        // Transmission's /downloads is the app's /media, which the app may read.
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

    // ── refused ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_onto_a_place_a_torrent_transmission_already_has_will_write_is_refused_naming_it_and_the_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Another release of the same name, downloading into the folder, nothing of it on the disk yet.
        _transmission.AddTorrent(
            new TorrentInfoHashes(QueuedHash, null), "Film.2001.mkv", MoviesFolder, files: [new FakeTransmissionFile("Film.2001.mkv", 9_000_000, 0)]);
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“Transmission” would write over “Film.2001.mkv” in the folder of the destination “Фільми”: the torrent “Film.2001.mkv” it already has will write there, so nothing was sent.",
            cancellationToken);
    }

    [Fact]
    public async Task Create_onto_the_part_file_transmission_would_write_first_is_refused_like_the_file_itself()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // What an unfinished download of another release left: Transmission would write this one's pieces into it.
        _factory.Files.AddFile("/media/movies/Film.2001.mkv.part", TestMedia.File(inode: 5, sizeBytes: 1_000));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(release);

        //Assert
        await AssertRefusedAsync(
            response,
            "“Transmission” would write over “Film.2001.mkv.part” in the folder of the destination “Фільми”: a file of another size is already there, so nothing was sent.",
            cancellationToken);
    }

    [Theory]
    // 4.0 cleans a name the portable way on every system: its ':' is written '_'.
    [InlineData("4.0.6", "Film_ 2001.mkv")]
    // 4.1 on Linux keeps it as it is.
    [InlineData("4.1.3", "Film: 2001.mkv")]
    public async Task Create_onto_where_a_queued_torrent_transmission_has_will_write_is_refused_by_the_name_that_transmission_writes(
        string version, string written)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.Version = version == "4.0.6" ? FakeTransmissionVersion.V4_0_6 : FakeTransmissionVersion.V4_1_3;
        // Queued to download: its name as its metadata gives it, its one file as this Transmission lists it.
        _transmission.AddTorrent(
            FakeTransmissionTorrent.Of(
                new TorrentInfoHashes(QueuedHash, null), "Film: 2001.mkv", MoviesFolder, [new FakeTransmissionFile(written, 9_000_000, 0)]) with
            {
                Status = 3,
            });
        _feed.TorrentFile = TestTorrent.V1("Film: 2001.mkv", TestTorrent.Bytes(FileLength, seed: 2));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(release);

        //Assert
        // D20 — the queued torrent's content is where that Transmission writes it, read from its name alone.
        await AssertRefusedAsync(
            response,
            $"“Transmission” would write over “{written}” in the folder of the destination “Фільми”: the torrent “Film: 2001.mkv” it already has will write there, so nothing was sent.",
            cancellationToken);
    }

    // ── sent unchecked ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_of_a_torrent_whose_places_no_reading_can_predict_is_sent_unchecked_and_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _feed.TorrentFile = WindowsCyrillicTorrent();
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(release);

        //Assert
        // Transmission 4.0 writes the path's bytes as they are, which no path in the app names: Keep both sends it as an
        // unreadable list of files is sent.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (DownloadJobStatus.Submitted, (DownloadOverwriteCheck?)DownloadOverwriteCheck.NotCheckedFileList),
            (job.Status, job.OverwriteCheck));
        Assert.Single(_transmission.AddRequests);
        // Nothing of the folder read: the check stopped at the release's own files.
        Assert.Equal(1, _transmission.Calls.Count(call => call.Method == "torrent-get"));
    }

    [Fact]
    public async Task Create_while_a_magnet_in_the_folder_still_fetches_its_metadata_is_sent_unchecked_and_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Added as a magnet, its files not known yet: Transmission lists no files for it and no place of its content.
        _transmission.AddTorrent(new FakeTransmissionTorrent(
            0, QueuedHash, "Film.2001", MoviesFolder, 4, 0, 0, 0, 0, 0, 0, "", [], 0, 0, false, false));
        var release = await FindReleaseAsync();

        //Act
        var response = await SendAsync(release);

        //Assert
        // It may write where this release would: a Keep both send goes, said so on the job.
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (DownloadJobStatus.Submitted, (DownloadOverwriteCheck?)DownloadOverwriteCheck.NotCheckedClientJobs),
            (job.Status, job.OverwriteCheck));
        Assert.Single(_transmission.AddRequests);
    }

    // ── the Download dialog's question before Send ──────────────────────────────────────────────

    [Fact]
    public async Task OverwriteCheck_of_a_folder_transmission_can_not_see_says_so_by_its_own_reason_and_of_one_it_sees_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Saved while Transmission saw it; unmounted since.
        var shows = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Серіали", "/downloads/tv-shows");

        //Act
        var unseen = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={shows.Id}", _member.AccessToken);
        var seen = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        // D5 — nothing is sent there at all, which the dialog says rather than "goes unchecked".
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (unseen.StatusCode, seen.StatusCode));
        Assert.Equal("NotCheckedFolderNotSeen", await ReasonAsync(unseen, cancellationToken));
        Assert.Null(await ReasonAsync(seen, cancellationToken));
        Assert.Empty(_transmission.Writes);
    }

    [Fact]
    public async Task OverwriteCheck_while_transmission_is_unreachable_names_it_with_its_sentence_for_the_owner_and_without_for_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        // MAM-31 — the live check stopped Transmission's container.
        _transmission.Unreachable = true;

        //Act
        var owners = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={_movies.Id}", owner.AccessToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        // The dialog's warning says Transmission couldn't be asked, and the owner reads Transmission's own sentence, with
        // the fix: never "has no path in the app, or its download client can't say where it is".
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var ownersAnswer = await ReadCheckAsync(owners, cancellationToken);
        Assert.Equal(
            new DownloadOverwriteCheckResponse(
                DownloadOverwriteCheck.NotCheckedFolderUnknown,
                DownloadFolderUnknownCause.ClientNotAsked,
                "Transmission",
                "Couldn't reach Transmission at http://transmission.test:9091: nothing accepted the connection. Check the address and port, and that Transmission is running."),
            ownersAnswer);
        Assert.Equal(
            new DownloadOverwriteCheckResponse(DownloadOverwriteCheck.NotCheckedFolderUnknown, DownloadFolderUnknownCause.ClientNotAsked, "Transmission"),
            await ReadCheckAsync(members, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_folder_no_path_mapping_brings_in_says_so_naming_transmission()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Transmission's own download folder, which /downloads → /media doesn't cover.
        _transmission.AddFolder("/scratch");
        var scratch = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Scratch", "/scratch");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={scratch.Id}", _member.AccessToken);

        //Assert
        // MAM-31 — the folder Transmission sees has no path in the app: the dialog says which client's mappings to fix,
        // and no path, to anyone.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new DownloadOverwriteCheckResponse(DownloadOverwriteCheck.NotCheckedFolderUnknown, DownloadFolderUnknownCause.NoPathMapping, "Transmission"),
            await ReadCheckAsync(response, cancellationToken));
        Assert.Empty(_transmission.Writes);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<ReleaseEntity> FindReleaseAsync() => new ReleaseSeeder(_factory.Scope).CreateFound(_member.User, _toloka, "Film (2001) BDRip 1080p");

    private Task<HttpResponseMessage> SendAsync(ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

    /// <summary>
    /// A torrent of one episode under the folder Show, its season folder named in Windows-1251 bytes ("Сезон 1") with no
    /// <c>path.utf-8</c> beside it, as old torrents hold it.
    /// </summary>
    private static byte[] WindowsCyrillicTorrent()
    {
        var content = TestTorrent.Bytes(10, seed: 3);
        return TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>
            {
                TestTorrent.Dictionary(("length", 10L), ("path", new List<object> { new byte[] { 0xD1, 0xE5, 0xE7, 0xEE, 0xED, 0x20, 0x31 }, "E01.mkv" })),
            }),
            ("name", "Show"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", SHA1.HashData(content))))));
    }

    /// <summary>201 with the job Failed with the app's own <paramref name="error"/>, read by the member as it is, the same
    /// on its row, and nothing added to Transmission.</summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, (string?)error), (job.Status, job.Error));
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken));
        Assert.Equal((DownloadJobStatus.Failed, (string?)error, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App), (row!.Status, TestTexts.En(row.Error), row.ErrorOrigin));
        Assert.Empty(_transmission.AddRequests);
        Assert.Empty(_transmission.Writes);
    }

    private static async Task<string?> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("notCheckedReason").GetString();
    }

    private static async Task<DownloadOverwriteCheckResponse> ReadCheckAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadOverwriteCheckResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
