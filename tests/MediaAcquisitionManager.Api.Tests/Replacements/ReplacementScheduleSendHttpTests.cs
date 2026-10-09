using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-24, ADR 0032, ADR 0034 — a scheduled replacement is sent in strict mode, after its job is written (Vasyl's decision of
// 2026-10-09): an obstacle found only at Send refuses the whole send, the job Failed with the app's sentence, nothing
// handed to qBittorrent, and the replacement Failed as refused before it was sent — never quietly sent as Keep both. A file
// already where the release lands refuses it, the same bytes included and without reading them; a gateway link that turns
// out a magnet; a category whose folder isn't inside a library; and every way the check couldn't look everywhere — a folder
// outside the Media folders, a place behind a link leading out of them, a file name the app can't look up, a .torrent whose
// list of files can't be read, or a qBittorrent that can't list the category's torrents. A send the check let through
// whose add qBittorrent then refused fails the replacement as a failed download. The torrents are real (TestTorrent).
public sealed class ReplacementScheduleSendHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string TolokaApiKey = "planted-torznab-key-0024";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string FilmsFolder = "/media/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TorrentName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Alien.1979";

    // Sentences the Jobs page and the dialog show, spelled out rather than read from product code.
    private const string RefusedBeforeSentReason = "The download was refused before it was sent, so nothing will be replaced.";
    private const string DownloadFailedReason = "The download failed, so nothing will be replaced.";
    private const string AlreadyThereError =
        "“qBittorrent” would write over “Alien.1979.Directors.Cut.2160p.mkv” in the folder of the category “Movies”: a file is already there, and a replacement never lands on a file already there, so nothing was sent.";
    private const string MagnetError =
        "This release turned out to be a magnet link, whose files the app can't check before sending, so nothing was sent and no copy will be replaced. Pick Keep both to download it.";
    private const string OutsideLibrariesError =
        "The folder of the category “Cartoons” isn't inside a library the app knows, so nothing was sent and no copy will be replaced. Pick Keep both to download it, or the owner fixes the destination.";
    private const string OutsideMediaFoldersError =
        "The app couldn't check every place this release would write in the folder of the category “Movies” (the folder, or a link on the way to one of its places, lies outside the Media folders), so nothing was sent and no copy will be replaced. Pick Keep both to download it.";
    private const string ClientJobsUncheckedError =
        "The app couldn't check every place this release would write in the folder of the category “Movies” (the client couldn't list its torrents in the category, or their files), so nothing was sent and no copy will be replaced. Pick Keep both to download it.";
    private const string FileNameUncheckedError =
        "The app couldn't check every place this release would write in the folder of the category “Movies” (a file's name holds characters the app can't look up), so nothing was sent and no copy will be replaced. Pick Keep both to download it.";
    private const string FileListUncheckedError =
        "The app couldn't check every place this release would write in the folder of the category “Movies” (the .torrent's list of files can't be read), so nothing was sent and no copy will be replaced. Pick Keep both to download it.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private ILibraryLocationSeeder _libraries = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private DestinationMappingEntity _movies = null!;
    private byte[] _content = null!;
    private Guid _theatrical;

    public ReplacementScheduleSendHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _content = TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1);
        _feed = new FakeTorznabServer(TolokaAddress, TolokaApiKey) { TorrentFile = TestTorrent.V1(TorrentName, _content) };
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: TolokaApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        // qBittorrent's /downloads is the app's /media: the category Movies saves into the library folder /media/movies.
        _client = await CreateClientAsync("qBittorrent", "/media");
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
        _releases = new ReleaseSeeder(_factory.Scope);
        _libraries = new LibraryLocationSeeder(_factory.Scope);
        var films = await _libraries.Create(_jellyfin, "Фільми", FilmsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");

        // Alien's theatrical cut, inspected, in the ticked Фільми: a copy the owner may tick.
        var works = new MediaWorkSeeder(_factory.Scope);
        var alien = await works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await works.AddVersion(_jellyfin, alien, Guid.NewGuid().ToString("N"), films, "Theatrical Cut", TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn(FilmsFolder);
        await _factory.WithDbAsync(async db => _theatrical = await db.PhysicalFiles.Where(file => file.ServicePath == TheatricalPath).Select(file => file.Id).SingleAsync(cancellationToken));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Schedule_whose_file_would_land_on_the_same_bytes_already_there_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The release's own bytes are already where it lands — a Keep both send of it goes, checked (MAM-38) —; a file
        // replaced by itself is no replacement.
        _factory.Files.AddContent($"{FilmsFolder}/{TorrentName}", _content);
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        await AssertRefusedAsync(response, AlreadyThereError, cancellationToken);
        // Told by being there: its pieces were never read.
        Assert.Empty(_factory.Files.PiecesHashedPaths);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_whose_gateway_link_turns_out_a_magnet_fails_and_asks_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The listing said nothing of the payload; the feed's link redirects to a magnet when fetched.
        _feed.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal)
            ? VendorResponses.Redirect(Magnet)
            : null;
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        await AssertRefusedAsync(response, MagnetError, cancellationToken);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(ReleasePayloadKind.Magnet, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).PayloadKind));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Schedule_to_a_destination_outside_every_library_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Cartoons saves into /media/cartoons, which no library holds: nothing could verify a new file there.
        var cartoons = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Cartoons", "Cartoons");
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, cartoons);

        //Assert
        await AssertRefusedAsync(response, OutsideLibrariesError, cancellationToken);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_to_a_library_outside_the_media_folders_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A library at /srv/archive and a qBittorrent saving Movies there: inside a library, but where the app may not look
        // for files already there (ADR 0034) — a Keep both send goes unchecked, a replacement doesn't go.
        await _libraries.Create(_jellyfin, "Архів", "/srv/archive");
        var archiveClient = await CreateClientAsync("qBittorrent archive", "/srv/archive");
        var archive = await new DestinationMappingSeeder(_factory.Scope).Create(archiveClient, "Archive", "Movies");
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, archiveClient, archive);

        //Assert
        await AssertRefusedAsync(response, OutsideMediaFoldersError, cancellationToken);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_whose_file_lies_behind_a_link_leading_out_of_the_media_folders_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The folder is inside the Media folders, but the way to the release's one place leads out of them: that place
        // can't be looked into — a Keep both send goes unchecked, a replacement doesn't go.
        _factory.Files.AddProblem(
            $"{FilmsFolder}/{TorrentName}", FileInventoryStatus.OutsideAllowedRoots, $"A link on the way to “{FilmsFolder}/{TorrentName}” leads out of the media folders.");
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        await AssertRefusedAsync(response, OutsideMediaFoldersError, cancellationToken);
        Assert.Empty(_factory.Files.PiecesHashedPaths);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_whose_file_name_holds_a_character_the_app_can_not_look_up_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // DEL: libtorrent keeps it in a name on Linux, and the app's paths refuse every control character.
        _feed.TorrentFile = TestTorrent.V1("Alien\u007F1979.mkv", _content);
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        await AssertRefusedAsync(response, FileNameUncheckedError, cancellationToken);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_of_a_torrent_whose_files_the_app_can_not_read_fails_and_hands_qbittorrent_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Hashable, so qBittorrent would take it, but two pieces' hashes for one byte: no list of files the app trusts.
        _feed.TorrentFile = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L),
            ("name", "Alien.1979.mkv"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", SHA1.HashData(new byte[2])[..20].Concat(new byte[20]).ToArray())))));
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        await AssertRefusedAsync(response, FileListUncheckedError, cancellationToken);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_when_qbittorrent_can_not_list_the_categorys_torrents_fails_and_hands_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The places are free, but a torrent qBittorrent already has in Movies may write one of them: unknown, so refused.
        _qbittorrent.Intercept = request =>
            request.RequestUri!.AbsolutePath == "/api/v2/torrents/info" && request.RequestUri.Query.Contains("category=", StringComparison.Ordinal)
                ? VendorResponses.Text("boom", HttpStatusCode.InternalServerError)
                : null;
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        await AssertRefusedAsync(response, ClientJobsUncheckedError, cancellationToken);
        AssertOnlyReads();
    }

    [Fact]
    public async Task Schedule_whose_add_qbittorrent_refused_after_the_check_fails_its_replacement_as_a_failed_download()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // 5.2's answer when it added nothing.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/add"
            ? VendorResponses.Text("Conflict", HttpStatusCode.Conflict)
            : null;
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _client, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        // Checked, then refused by the client: the download failed, not the check.
        Assert.Equal(
            (DownloadJobStatus.Failed, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked),
            (job.Status, job.OverwriteCheck));
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Failed, 1, DownloadFailedReason, false, 0), job.Replacement);
        await _factory.WithDbAsync(async db =>
        {
            var replacement = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((ReplacementJobState.Failed, (string?)DownloadFailedReason), (replacement.State, replacement.Reason));
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, string appPrefix) =>
        _integrations.Create(
            "qbittorrent",
            name,
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", appPrefix)]);

    /// <summary>The owner's search for Alien (1979) found a release of it, with its IMDb id, whose link the feed answers.</summary>
    private Task<ReleaseEntity> FindAlienAsync() =>
        _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);

    private Task<HttpResponseMessage> ScheduleAsync(ReleaseEntity release, IntegrationConnectionEntity client, DestinationMappingEntity destination) =>
        _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, client.Id, destination.Id, [_theatrical]));

    /// <summary>
    /// 201 with the job Failed with the app's own <paramref name="error"/> and no check recorded, its replacement Failed
    /// as refused before it was sent; the same on their rows; and nothing handed to qBittorrent.
    /// </summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, error, (DownloadOverwriteCheck?)null), (job.Status, job.Error, job.OverwriteCheck));
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Failed, 1, RefusedBeforeSentReason, false, 0), job.Replacement);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (DownloadJobStatus.Failed, error, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App, (DownloadOverwriteCheck?)null),
                (row.Status, row.Error, row.ErrorOrigin, row.OverwriteCheck));
            var replacement = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((ReplacementJobState.Failed, (string?)RefusedBeforeSentReason), (replacement.State, replacement.Reason));
        });
        Assert.Empty(_qbittorrent.AddRequests);
    }

    /// <summary>Every request to qBittorrent is a GET or the sign-in: nothing added, removed, stopped or paused.</summary>
    private void AssertOnlyReads() =>
        Assert.All(
            _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}"),
            call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
}
