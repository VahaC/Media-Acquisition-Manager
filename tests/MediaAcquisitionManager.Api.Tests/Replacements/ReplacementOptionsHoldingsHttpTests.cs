using System.Net;
using System.Net.Http.Json;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-24, ADR 0031 — the Download dialog's held marks on GET /api/replacements/options?holdings=true: every download
// client's whole listing (qBittorrent's torrents/info?includeFiles=true), read only on the dialog's word and only while a
// copy can be ticked; a copy a torrent lists is held by it whatever the torrent's state, a hardlink of a torrent's file is
// its second name, and a copy with other names says so even when the torrent's own name lies outside the Media folders,
// where the app can't look; two torrents of one name are two holds, and a torrent file of the copy's size that is another
// file holds nothing. A client the app may not ask or can't use, one that doesn't answer, one whose listing throws and one
// too old to list the files are "not read", and torrents whose files can't be placed make their client's answer partial —
// never an all-clear. Holds never keep a copy from being ticked, and the read only reads. MAM-26 — a torrent that lists a
// copy at its own path holds it whatever size it lists, and the dialog still answers for a client whose mappings reach no
// Media folder, which the read before a move leaves out (ReplacementHoldingReaderTargetsTests).
public sealed class ReplacementOptionsHoldingsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/options";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string AlienImdbId = "tt0078748";
    private const string FilmsFolder = "/media/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string DirectorsCutPath = "/media/movies/Alien (1979)/Alien (1979) Director's Cut.mkv";
    private const long TheatricalSize = 3_891_719_630;
    private const long DirectorsCutSize = 4_000_000_000;
    private const string IncludeFilesCall = "GET /api/v2/torrents/info?includeFiles=true";
    private const string PlantedExceptionMessage = "planted-exception-message-0024";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IMediaWorkSeeder _works = null!;
    private IReplacementSettingsSeeder _replacement = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;
    private IntegrationConnectionEntity _client = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private DestinationMappingEntity _movies = null!;
    private LibraryLocationEntity _films = null!;
    private ReleaseEntity _release = null!;

    public ReplacementOptionsHoldingsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var toloka = await _integrations.Create("torznab", "Toloka", apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        // /downloads is the app's /media; /data, where some torrents keep their own names, is /srv/torrents to the app —
        // outside the Media folders.
        _client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media"), new PathMappingPair("/data", "/srv/torrents")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
        _works = new MediaWorkSeeder(_factory.Scope);
        _replacement = new ReplacementSettingsSeeder(_factory.Scope);
        _films = await new LibraryLocationSeeder(_factory.Scope).Create(_jellyfin, "Фільми", FilmsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _release = await new ReleaseSeeder(_factory.Scope).CreateFoundBySearch(
            _owner.User, toloka, "Alien", 1979, "Alien (1979) BDRemux 2160p", imdbId: AlienImdbId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Options_without_the_dialog_asking_for_holdings_reads_no_torrent(bool? holdings)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));

        //Act
        var response = await OptionsAsync(holdings);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Empty(options.Clients);
        Assert.All(CopiesOf(options), copy => Assert.Empty(copy.Holds));
        Assert.DoesNotContain(IncludeFilesCall, QBittorrentCalls());
    }

    [Fact]
    public async Task Options_with_holdings_marks_the_copies_torrents_hold_whatever_their_state_and_only_reads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // One seeding, one paused: listed means held (ADR 0031). And one of another film, which holds neither.
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.DC", "Alien (1979)/Alien (1979) Director's Cut.mkv", DirectorsCutSize, state: "pausedUP"));
        _qbittorrent.AddTorrent(TorrentHolding("Aliens.1986", "Aliens (1986)/Aliens (1986).mkv", 5_000_000_000));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        Assert.Equal(
            new Dictionary<string, string>
            {
                [DirectorsCutPath] = "qBittorrent/Alien.1979.DC/False",
                [TheatricalPath] = "qBittorrent/Alien.1979.Theatrical/False",
            },
            CopiesOf(options).ToDictionary(copy => copy.File.AppPath!, copy => string.Join(", ", copy.Holds.Select(Describe))));
        // A hold never keeps a copy from being ticked.
        Assert.All(CopiesOf(options), copy => Assert.Equal(((ReplacementCopyUnavailableReason?)null, false), (copy.Unavailable, copy.OtherNames)));
        // One listing of the whole client, and nothing but reads.
        Assert.Single(QBittorrentCalls(), call => call == IncludeFilesCall);
        Assert.All(QBittorrentCalls(), call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Empty(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Options_with_holdings_names_each_of_two_torrents_of_one_name_that_list_a_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // A cross-seed: the same content added again under another hash, with the same name, in another category. Both
        // list the theatrical cut, and removing one would leave the other holding it.
        const string seeding = "1111111111111111111111111111111111111111";
        const string crossSeed = "2222222222222222222222222222222222222222";
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize, hash: seeding));
        _qbittorrent.AddTorrent(
            TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize, hash: crossSeed) with { Category = "cross-seed" });

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = CopiesOf(await ReadAsync(response, cancellationToken)).Single(copy => copy.File.AppPath == TheatricalPath);
        // Two holds, each with its torrent's id: one name never makes them one.
        Assert.Equal(
            [("qBittorrent", seeding, "Alien.1979.Theatrical", false), ("qBittorrent", crossSeed, "Alien.1979.Theatrical", false)],
            copy.Holds.Select(hold => (hold.ClientName, hold.TorrentId, hold.TorrentName, hold.SecondName)).OrderBy(hold => hold.TorrentId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Options_with_holdings_does_not_mark_a_copy_held_by_a_torrent_file_of_its_size_that_is_another_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The torrent's own file, and the theatrical cut a full copy of it the owner put in the library — not a hardlink:
        // one size, two files.
        const string torrentsFile = "/media/movies/Pack/Alien.mkv";
        await PlantAlienAsync(cancellationToken);
        _factory.Files.AddFile(torrentsFile, TestMedia.File(inode: 9, sizeBytes: TheatricalSize));
        await _replacement.SwitchOn(FilmsFolder);
        _qbittorrent.AddTorrent(TorrentHolding("Alien.Pack", "Pack/Alien.mkv", TheatricalSize));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        var copy = CopiesOf(options).Single(copy => copy.File.AppPath == TheatricalPath);
        // A path of the copy's size is where to look, not proof (ADR 0031): the device and inode found there decide.
        Assert.Equal((0, false), (copy.Holds.Count, copy.OtherNames));
        Assert.Contains(torrentsFile, _factory.Files.DescribedPaths);
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
    }

    [Fact]
    public async Task Options_with_holdings_marks_a_copy_a_torrent_lists_at_its_path_whatever_size_it_lists()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // MAM-26 — an errored torrent that lists the theatrical cut at its path with a size the disk doesn't have, and one
        // missing its files that lists the director's cut with no size it can tell: ADR 0031's "an old copy any torrent
        // lists, in any state" — a recheck would write either path back.
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize - 4096, state: "error"));
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.DC", "Alien (1979)/Alien (1979) Director's Cut.mkv", -1, state: "missingFiles"));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [DirectorsCutPath] = "qBittorrent/Alien.1979.DC/False",
                [TheatricalPath] = "qBittorrent/Alien.1979.Theatrical/False",
            },
            CopiesOf(options).ToDictionary(copy => copy.File.AppPath!, copy => string.Join(", ", copy.Holds.Select(Describe))));
        // The path places the file: neither torrent is one the app couldn't place.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
    }

    [Fact]
    public async Task Options_with_holdings_still_answers_for_a_client_whose_mappings_reach_no_media_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // MAM-26 — a seedbox whose one mapping leads outside /media: the read before a move leaves it out; the dialog, which
        // only marks, still says what it gave — one torrent mapped outside the Media folders, one saving where no mapping
        // reaches.
        var seedbox = new FakeQBittorrentServer("http://seedbox.test:8080");
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, seedbox);
        seedbox.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));
        seedbox.AddTorrent(TorrentHolding("Elsewhere", "Alien.mkv", TheatricalSize, savePath: "/elsewhere"));
        await _integrations.Create(
            "qbittorrent",
            "seedbox",
            seedbox.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/srv/seedbox")]);

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0), ("seedbox", ReplacementClientCheck.Partial, (string?)null, 1)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        Assert.All(CopiesOf(options), copy => Assert.Empty(copy.Holds));
        Assert.Contains(_factory.QBittorrentHttp.Requests, request => request.Uri.Host == "seedbox.test" && request.Uri.Query.Contains("includeFiles", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Options_names_the_works_copies_holds_and_clients_parts_as_the_dialog_reads_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var work = document.RootElement.GetProperty("works")[0];
        Assert.Equal(
            ["workId", "kind", "title", "year", "evidence", "conflictingSchemes", "needsConfirmation", "confirmationText", "libraryNames", "unavailable", "copies", "episodes"],
            work.EnumerateObject().Select(property => property.Name));
        var copy = work.GetProperty("copies").EnumerateArray().Single(candidate => candidate.GetProperty("file").GetProperty("appPath").GetString() == TheatricalPath);
        Assert.Equal(
            ["file", "versionId", "editionLabel", "editions", "dynamicRanges", "otherNames", "unavailable", "holds"],
            copy.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["clientName", "torrentId", "torrentName", "secondName"], copy.GetProperty("holds")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["clientName", "status", "error", "unplacedTorrents"],
            document.RootElement.GetProperty("clients")[0].EnumerateObject().Select(property => property.Name));
        // Enums by name, as everywhere.
        Assert.Equal(("Movie", "Identifier", "Read"), (work.GetProperty("kind").GetString(), work.GetProperty("evidence").GetString(),
            document.RootElement.GetProperty("clients")[0].GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Options_with_holdings_marks_a_copy_that_is_a_second_name_of_a_torrents_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The copy and the torrent's own file are two names of one file (a hardlink), both inside the Media folders.
        const string torrentsName = "/media/movies/Pack/Alien.mkv";
        await PlantAlienAsync(cancellationToken, theatricalLinks: 2);
        _factory.Files.AddFile(torrentsName, TestMedia.File(inode: 1, sizeBytes: TheatricalSize, linkCount: 2));
        await _replacement.SwitchOn(FilmsFolder);
        _qbittorrent.AddTorrent(TorrentHolding("Alien.Pack", "Pack/Alien.mkv", TheatricalSize));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = CopiesOf(await ReadAsync(response, cancellationToken)).Single(copy => copy.File.AppPath == TheatricalPath);
        Assert.Equal(["qBittorrent/Alien.Pack/True"], copy.Holds.Select(Describe));
        Assert.True(copy.OtherNames);
        Assert.Null(copy.Unavailable);
    }

    [Fact]
    public async Task Options_with_holdings_says_a_copy_has_other_names_when_the_torrents_name_lies_outside_the_media_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken, theatricalLinks: 2);
        await _replacement.SwitchOn(FilmsFolder);
        // The usual hardlink layout: the torrent keeps its own name under /data, which is /srv/torrents to the app.
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979", "Alien.mkv", TheatricalSize, savePath: "/data/torrents"));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        var copy = CopiesOf(options).Single(copy => copy.File.AppPath == TheatricalPath);
        // No torrent named — the app may not look where it is —, but moving the copy frees no space, and it says so.
        Assert.Equal((true, 0), (copy.OtherNames, copy.Holds.Count));
        // Outside the Media folders is not a place it couldn't map: the client was read.
        Assert.Equal(ReplacementClientCheck.Read, Assert.Single(options.Clients).Status);
        Assert.DoesNotContain(_factory.Files.DescribedPaths, path => path.StartsWith("/srv/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Options_with_holdings_counts_the_torrents_it_can_not_place_and_still_marks_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // One saving where no mapping reaches, one magnet still fetching its metadata, one with no save path, one whose
        // file's size qBittorrent doesn't say, one whose file of the copy's size the disk doesn't show, and one that holds a
        // copy.
        _qbittorrent.AddTorrent(TorrentHolding("Elsewhere", "Alien.mkv", TheatricalSize, savePath: "/elsewhere"));
        _qbittorrent.AddTorrent(TorrentHolding("Fetching", "Alien.mkv", TheatricalSize, hash: "2222222222222222222222222222222222222222") with { HasMetadata = false });
        _qbittorrent.AddTorrent(TorrentHolding("Unsaved", "Alien.mkv", TheatricalSize, savePath: "", hash: "4444444444444444444444444444444444444444"));
        // A negative size is no size to the adapter: unknown.
        _qbittorrent.AddTorrent(TorrentHolding("Unsized", "Unsized/Alien.mkv", -1, hash: "5555555555555555555555555555555555555555"));
        _qbittorrent.AddTorrent(TorrentHolding("Missing", "Missing/Alien.mkv", TheatricalSize, hash: "6666666666666666666666666666666666666666"));
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize, hash: "3333333333333333333333333333333333333333"));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        // Each is a place the app couldn't tell: never an all-clear.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Partial, (string?)null, 5)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        Assert.Equal(
            ["qBittorrent/Alien.1979.Theatrical/False"],
            CopiesOf(options).Single(copy => copy.File.AppPath == TheatricalPath).Holds.Select(Describe));
    }

    [Fact]
    public async Task Options_with_holdings_says_a_switched_off_client_was_not_read_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var seedbox = new FakeQBittorrentServer("http://seedbox.test:8080");
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, seedbox);
        await _integrations.Create(
            "qbittorrent",
            "seedbox",
            seedbox.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: false);

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        // A client switched off in the app may still run and seed: its silence is never an all-clear.
        Assert.Equal(
            [
                ("qBittorrent", ReplacementClientCheck.Read, (string?)null),
                ("seedbox", ReplacementClientCheck.NotRead, "“seedbox” is switched off."),
            ],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error)));
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, request => request.Uri.Host == "seedbox.test");
    }

    [Fact]
    public async Task Options_with_holdings_says_a_client_that_does_not_answer_was_not_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var seedbox = new FakeQBittorrentServer("http://seedbox.test:8080") { Unreachable = true };
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, seedbox);
        await _integrations.Create(
            "qbittorrent",
            "seedbox",
            seedbox.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword);

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seedboxCheck = (await ReadAsync(response, cancellationToken)).Clients.Single(client => client.ClientName == "seedbox");
        Assert.Equal(ReplacementClientCheck.NotRead, seedboxCheck.Status);
        Assert.False(string.IsNullOrWhiteSpace(seedboxCheck.Error));
        Assert.DoesNotContain(FakeQBittorrentServer.DefaultPassword, seedboxCheck.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_with_holdings_says_a_client_of_a_type_this_version_can_not_use_was_not_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        await _integrations.Create("transmission", "Transmission", kind: IntegrationKind.DownloadClient);

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A client this build can't ask may still hold the copies: never an all-clear.
        Assert.Equal(
            [
                ("qBittorrent", ReplacementClientCheck.Read, (string?)null),
                ("Transmission", ReplacementClientCheck.NotRead, "This version of the app can't read “transmission”."),
            ],
            (await ReadAsync(response, cancellationToken)).Clients.Select(client => (client.ClientName, client.Status, client.Error)));
    }

    [Fact]
    public async Task Options_with_holdings_says_a_client_whose_password_can_not_be_read_was_not_read_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var seedbox = new FakeQBittorrentServer("http://seedbox.test:8080");
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, seedbox);
        var seedboxClient = await _integrations.Create(
            "qbittorrent",
            "seedbox",
            seedbox.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword);
        // Another client's password copied onto this row: sealed for the other row, so it opens for neither.
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(row => row.Id == seedboxClient.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordEncrypted, _client.PasswordEncrypted), cancellationToken));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seedboxCheck = (await ReadAsync(response, cancellationToken)).Clients.Single(client => client.ClientName == "seedbox");
        Assert.Equal(
            (ReplacementClientCheck.NotRead, (string?)"The stored password of “seedbox” can't be read with this installation's encryption key. Enter the password again and save."),
            (seedboxCheck.Status, seedboxCheck.Error));
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, request => request.Uri.Host == "seedbox.test");
    }

    [Fact]
    public async Task Options_with_holdings_says_a_client_whose_listing_threw_was_not_read_and_logs_the_type_never_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));
        // Not a transport failure the adapter maps: something nobody foresaw, as a parser bug would be.
        _qbittorrent.Intercept = request => request.RequestUri!.Query.Contains("includeFiles", StringComparison.Ordinal)
            ? throw new InvalidOperationException(PlantedExceptionMessage)
            : null;
        _factory.Logs.Clear();

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        // The dialog still gets its answer — the client not read, never a 5xx — and no hold is claimed.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.NotRead, (string?)"The app couldn't read what “qBittorrent” answered.", 0)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        Assert.All(CopiesOf(options), copy => Assert.Empty(copy.Holds));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.StartsWith(
            $"Holdings listing of integration {_client.Id} (qbittorrent) threw System.InvalidOperationException",
            error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Options_with_holdings_says_a_qbittorrent_too_old_to_list_the_files_was_not_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // 5.1 ignores includeFiles: an answer without files would read as nothing held.
        _qbittorrent.Version = "v5.1.4";
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.NotRead, (string?)"This is qBittorrent v5.1.4; telling which torrents hold a library file needs qBittorrent 5.2 or later.")],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error)));
        Assert.All(CopiesOf(options), copy => Assert.Empty(copy.Holds));
        Assert.DoesNotContain(IncludeFilesCall, QBittorrentCalls());
    }

    [Fact]
    public async Task Options_with_holdings_reads_no_torrent_while_no_copy_can_be_ticked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        // Switched on, but Фільми not ticked: every copy's folder keeps it out of reach.
        await _replacement.SwitchOn();
        _qbittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", "Alien (1979)/Alien (1979) Theatrical Cut.mkv", TheatricalSize));

        //Act
        var response = await OptionsAsync(holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.All(CopiesOf(options), copy => Assert.Equal(ReplacementCopyUnavailableReason.FolderNotTicked, copy.Unavailable));
        Assert.Empty(options.Clients);
        Assert.DoesNotContain(IncludeFilesCall, QBittorrentCalls());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alien (1979) in Фільми: the theatrical cut and the director's cut, each one file on the disk, inspected; the
    /// theatrical cut with <paramref name="theatricalLinks"/> names.</summary>
    private async Task PlantAlienAsync(CancellationToken cancellationToken, int theatricalLinks = 1)
    {
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, Guid.NewGuid().ToString("N"), _films, "Theatrical Cut", TheatricalPath);
        await _works.AddVersion(_jellyfin, alien, Guid.NewGuid().ToString("N"), _films, "Director's Cut", DirectorsCutPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1, sizeBytes: TheatricalSize, linkCount: theatricalLinks), TestMedia.Taken());
        _factory.Files.AddFile(DirectorsCutPath, TestMedia.File(inode: 2, sizeBytes: DirectorsCutSize), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
    }

    /// <summary>A torrent of qBittorrent's Movies category — saving to its /downloads/movies unless told otherwise — with one
    /// file at <paramref name="file"/> (its root folder included) of <paramref name="size"/>.</summary>
    private static FakeQBittorrentTorrent TorrentHolding(
        string name, string file, long size, string state = "stalledUP", string savePath = "/downloads/movies", string? hash = null) =>
        FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes(hash ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(name))), null),
                name,
                "Movies",
                state,
                savePath: savePath)
            with { Files = [new FakeQBittorrentFile(file, size, 1)] };

    private Task<HttpResponseMessage> OptionsAsync(bool? holdings) =>
        _factory.GetHttpRequest(
            $"{BaseUrl}?releaseId={_release.Id}&destinationId={_movies.Id}" + (holdings is { } read ? $"&holdings={(read ? "true" : "false")}" : ""),
            _owner.AccessToken);

    private static async Task<ReplacementOptionsResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static IEnumerable<ReplacementCopyDto> CopiesOf(ReplacementOptionsResponse options) =>
        options.Works.SelectMany(work => work.Copies.Concat(work.Episodes.SelectMany(episode => episode.Copies)));

    private static string Describe(ReplacementHoldDto hold) => $"{hold.ClientName}/{hold.TorrentName}/{hold.SecondName}";

    private string[] QBittorrentCalls() =>
        [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];
}
