using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-30, ADR 0039 — the Destinations of a real Transmission, end to end: the Add and Edit dialogs' folders through the
// real adapter over HTTP to a FakeTransmissionServer, which answers as Transmission 4.1.3 does. Transmission's own
// download folder and each media library folder mapped back through its path mappings, each confirmed by free-space; a
// library folder Transmission can't see listed with Transmission's sentence and never savable; a library no mapping leads
// back to listed apart with how to fix it; the download folder that is a library folder listed once, as that library's;
// Jellyfin's Collections library never a candidate; the Edit dialog's own folder proposed too; a name with a comma, which
// Transmission's labels can't hold, refused before Transmission is asked. Nothing but session-get and free-space is ever
// sent from these pages. The Api's own rules over a stand-in client are DestinationsFolderClientHttpTests' and
// DestinationsFolderSaveHttpTests'.
public sealed class DestinationsTransmissionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string ShowsFolder = "/downloads/tv-shows";

    // Sentences the dialog shows, spelled out rather than read from product code.
    private const string ShowsNotSeenProblem =
        "Transmission can't see “/downloads/tv-shows” (No such file or directory). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again.";
    private const string DownloadsOutsideProblem =
        "“/media” is outside every media library folder, so automatic replacement stays off for this destination.";
    private const string TemporaryFolderError =
        "Transmission keeps unfinished downloads in its temporary folder “/downloads/incomplete”, where the app can't follow them. In Transmission's web interface open Edit Preferences, Torrents, untick “Use temporary folder”, then test again.";
    private const string CommaError =
        "“Transmission” shows this destination's name as the torrent's label, and a label can't contain a comma. Pick a name without one.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryLocationSeeder _libraries = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;
    private FakeTransmissionServer _transmission = null!;

    public DestinationsTransmissionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _libraries = new LibraryLocationSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        // Transmission sees its own /downloads and the films' folder, not the series' one: that one isn't mounted into it.
        _transmission = new FakeTransmissionServer();
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_factory.TransmissionHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the folders ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientDestinations_of_transmission_lists_its_download_folder_and_each_library_folder_confirmed_by_free_space_or_not()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        await _libraries.Create(_jellyfin, "Серіали", "/media/tv-shows");
        var client = await CreateClientAsync();

        //Act
        var response = await ListAsync(client);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equivalent(
            new ClientDestinationListResponse(
                [
                    new ClientDestinationDto(
                        "/downloads",
                        "/downloads",
                        new DestinationCheckDto(DestinationPathStatus.OutsideLibraries, "/downloads", "/media", [], DownloadsOutsideProblem),
                        IsClientDefaultFolder: true),
                    new ClientDestinationDto(
                        ShowsFolder,
                        ShowsFolder,
                        new DestinationCheckDto(DestinationPathStatus.FolderNotSeen, ShowsFolder, null, ["Серіали"], ShowsNotSeenProblem)),
                    new ClientDestinationDto(
                        MoviesFolder,
                        MoviesFolder,
                        new DestinationCheckDto(DestinationPathStatus.InLibrary, MoviesFolder, "/media/movies", ["Фільми"], null)),
                ],
                Error: null,
                DestinationKind: DownloadDestinationKind.Folder,
                Unplaced: []),
            await ReadAsync(response, cancellationToken),
            strict: true);
        // One session, then one free-space of each distinct folder; nothing that could change Transmission.
        Assert.Equal(["session-get", "free-space", "free-space", "free-space"], _transmission.Calls.Select(call => call.Method));
        Assert.Equal(["/downloads", ShowsFolder, MoviesFolder], FreeSpacePaths());
        Assert.Empty(_transmission.Writes);
    }

    [Fact]
    public async Task ClientDestinations_names_on_the_wire_that_transmissions_own_download_folder_is_its_default()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        var client = await CreateClientAsync();

        //Act
        var response = await ListAsync(client);

        //Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Folder", document.RootElement.GetProperty("destinationKind").GetString());
        Assert.Equal(
            [("/downloads", JsonValueKind.True), (MoviesFolder, JsonValueKind.False)],
            document.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => (item.GetProperty("key").GetString(), item.GetProperty("isClientDefaultFolder").ValueKind)));
    }

    [Fact]
    public async Task ClientDestinations_lists_transmissions_download_folder_that_is_a_library_folder_once_as_that_librarys_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        // Its Download to, in another spelling of the library's folder.
        _transmission.DownloadDir = MoviesFolder + "/";
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D18 — one row, the library's, marked as Transmission's folder too; asked about once.
        Assert.Equivalent(
            new[]
            {
                new ClientDestinationDto(
                    MoviesFolder,
                    MoviesFolder,
                    new DestinationCheckDto(DestinationPathStatus.InLibrary, MoviesFolder, "/media/movies", ["Фільми"], null),
                    IsClientDefaultFolder: true),
            },
            body.Items,
            strict: true);
        Assert.Equal([MoviesFolder], FreeSpacePaths());
    }

    [Fact]
    public async Task ClientDestinations_lists_a_library_no_mapping_of_transmission_leads_to_apart_with_how_to_fix_it_and_never_asks_about_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        await _libraries.Create(_jellyfin, "Мульфільми", "/srv/cartoons");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equivalent(
            new[]
            {
                new UnplacedLibraryFolderDto(
                    "Мульфільми",
                    "/srv/cartoons",
                    "“Transmission” has no folder for the library “Мульфільми”: none of its path mappings leads to “/srv/cartoons”. Add one under Integrations, “Transmission”, Path mappings (for example /downloads → /media), then look again."),
            },
            body.Unplaced,
            strict: true);
        Assert.Equal(["/downloads", MoviesFolder], body.Items.Select(item => item.Key));
        Assert.Equal(["/downloads", MoviesFolder], FreeSpacePaths());
    }

    [Fact]
    public async Task ClientDestinations_never_asks_transmission_about_jellyfins_collections_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The recorded libraries, stored by a real sync: Колекції is CollectionType boxsets, at /config/data/collections.
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_jellyfin);
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // Decision 9 — no candidate, so neither a folder asked about nor one listed as unplaced.
        Assert.Empty(body.Unplaced);
        Assert.Equal(
            ["/downloads", "/downloads/documentary", "/downloads/cartoons", ShowsFolder, MoviesFolder],
            FreeSpacePaths());
        Assert.DoesNotContain(body.Items, item => item.Check.LibraryNames.Contains("Колекції"));
    }

    [Fact]
    public async Task ClientDestinations_for_the_edit_dialog_proposes_the_destinations_own_folder_to_transmission()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        _transmission.AddFolder("/downloads/archive");
        var client = await CreateClientAsync();
        // Saved before the archive's library went: no library leads there any more.
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Архів", "/downloads/archive");

        //Act
        var withIt = await ReadAsync(await ListAsync(client, $"?destinationId={destination.Id}"), cancellationToken);

        //Assert
        // D4 — confirmed by Transmission and listed as itself, never "missing".
        Assert.Equal(
            [
                ("/downloads", DestinationPathStatus.OutsideLibraries),
                (MoviesFolder, DestinationPathStatus.InLibrary),
                ("/downloads/archive", DestinationPathStatus.OutsideLibraries),
            ],
            withIt.Items.Select(item => (item.Key, item.Check.Status)));
        Assert.Equal(["/downloads", MoviesFolder, "/downloads/archive"], FreeSpacePaths());
    }

    [Fact]
    public async Task ClientDestinations_of_a_transmission_keeping_unfinished_downloads_in_its_temporary_folder_says_how_to_turn_it_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        _transmission.IncompleteDirEnabled = true;
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D6 — no folder at all: the app couldn't follow a download there.
        Assert.Equivalent(new ClientDestinationListResponse([], TemporaryFolderError, DownloadDestinationKind.Folder, []), body, strict: true);
        Assert.Equal(["session-get"], _transmission.Calls.Select(call => call.Method));
    }

    // ── saved ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_of_a_library_folder_transmission_sees_is_201_and_stores_it_as_transmission_lists_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        var client = await CreateClientAsync();

        //Act
        // The same folder, spelt with a trailing slash.
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Фільми", client.Id, MoviesFolder + "/"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            (MoviesFolder, (DownloadDestinationKind?)DownloadDestinationKind.Folder, DestinationPathStatus.InLibrary),
            (body!.ClientDestinationKey, body.DestinationKind, body.Check.Status));
        Assert.Equal([MoviesFolder], await StoredKeysAsync(cancellationToken));
        // D4 — the request's own spelling was never proposed, and nothing was written to Transmission.
        Assert.Equal(["/downloads", MoviesFolder], FreeSpacePaths());
        Assert.Empty(_transmission.Writes);
    }

    [Fact]
    public async Task Create_of_a_library_folder_transmission_can_not_see_is_409_with_its_sentence_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Серіали", "/media/tv-shows");
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Серіали", client.Id, ShowsFolder));

        //Assert
        // D5 — Transmission would create the folder in its own filesystem.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ShowsNotSeenProblem, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(await StoredKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_with_a_comma_in_the_name_is_400_and_asks_transmission_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies, 4K", client.Id, MoviesFolder));

        //Assert
        // Transmission keeps labels apart by commas, and refuses one that holds one.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CommaError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TransmissionHttp.Requests);
        Assert.Empty(await StoredKeysAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Transmission at the fake's address with its user name and password, its <c>/downloads</c> the app's
    /// <c>/media</c>.</summary>
    private Task<IntegrationConnectionEntity> CreateClientAsync() =>
        _integrations.Create(
            "transmission",
            "Transmission",
            _transmission.BaseUrl,
            userName: FakeTransmissionServer.DefaultUserName,
            password: FakeTransmissionServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    private Task<HttpResponseMessage> ListAsync(IntegrationConnectionEntity client, string query = "") =>
        _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}{query}", _owner.AccessToken);

    private static async Task<ClientDestinationListResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>Every folder free-space was asked about, in the order asked.</summary>
    private string[] FreeSpacePaths() =>
        [.. _transmission.Calls.Where(call => call.Method == "free-space").Select(call => call.Arguments["path"]!.GetValue<string>())];

    private async Task<IReadOnlyList<string>> StoredKeysAsync(CancellationToken cancellationToken)
    {
        List<string> keys = [];
        await _factory.WithDbAsync(async db => keys = await db.DestinationMappings.Select(destination => destination.ClientDestinationKey).ToListAsync(cancellationToken));
        return keys;
    }
}
