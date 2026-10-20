using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-30, ADR 0039 — what a download client whose destinations are folders offers the Add or Edit destination dialog:
// its own download folder and each media library folder mapped back through its path mappings, every folder proposed
// once and confirmed by the client, each checked as a destination of it would be. A library folder the client can't see
// stays listed with its sentence, never pickable (D5), and so does its own download folder when it is too long to store
// (D1); a library folder it has no folder for — no mapping leads back to it, two do from different places, its name
// can't be written under the client's Windows folder, its folder is too long to store, or Jellyfin's own mappings can't
// place it — is listed apart with how to fix it, by library name, never hidden; Jellyfin's Collections library is no
// destination at all (decision 9).
// The download folder equal to a library folder is that library's row, marked as the client's folder too; one inside a
// library folder shows its honest status (D18). The Edit dialog's own destination's stored folder is proposed too and
// listed as itself (D4), one that isn't a full path never proposed and listed with why (D3).
public sealed class DestinationsFolderClientHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string ClientName = "Transmission";
    private const string OutsideCompleteProblem =
        "“/media/complete” is outside every media library folder, so automatic replacement stays off for this destination.";

    private readonly FolderClientApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryLocationSeeder _libraries = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;

    public DestinationsFolderClientHttpTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _libraries = new LibraryLocationSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _factory.FolderClient.Arm();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the folders ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientDestinations_of_a_folder_client_lists_its_download_folder_then_each_library_folder_mapped_back_each_checked()
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
                        "/downloads/complete",
                        "/downloads/complete",
                        new DestinationCheckDto(DestinationPathStatus.OutsideLibraries, "/downloads/complete", "/media/complete", [], OutsideCompleteProblem),
                        IsClientDefaultFolder: true),
                    new ClientDestinationDto("/downloads/tv-shows", "/downloads/tv-shows", InLibrary("/downloads/tv-shows", "/media/tv-shows", "Серіали")),
                    new ClientDestinationDto("/downloads/movies", "/downloads/movies", InLibrary("/downloads/movies", "/media/movies", "Фільми")),
                ],
                Error: null,
                DestinationKind: DownloadDestinationKind.Folder,
                Unplaced: []),
            await ReadAsync(response, cancellationToken),
            strict: true);
        // Each library folder proposed in the client's terms, by library name; nothing else is ever asked.
        Assert.Equal([["/downloads/tv-shows", "/downloads/movies"]], _factory.FolderClient.Proposals);
        Assert.Equal(["ListDestinations"], _factory.FolderClient.Calls);
    }

    [Fact]
    public async Task ClientDestinations_names_on_the_wire_that_an_entry_is_the_clients_own_download_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        var client = await CreateClientAsync();

        //Act
        var response = await ListAsync(client);

        //Assert
        // MAM-30 — a folder client's own download folder reaches the dialog as "isClientDefaultFolder":true, read from the
        // raw JSON: DestinationMapper.ToResponse passes ClientDestination.IsClientDefaultFolder through, and qBittorrent
        // never lists one, so no category client's test can show it.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Folder", document.RootElement.GetProperty("destinationKind").GetString());
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [("/downloads/complete", JsonValueKind.True), ("/downloads/movies", JsonValueKind.False)],
            items.Select(item => (item.GetProperty("key").GetString(), item.GetProperty("isClientDefaultFolder").ValueKind)));
    }

    [Fact]
    public async Task ClientDestinations_lists_a_download_folder_that_is_a_library_folder_once_as_that_librarys_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        _factory.FolderClient.Arm("/downloads/movies/");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D18 — one row, the library's, marked as the client's folder too: in the library, so replacement works there.
        Assert.Equivalent(
            new[] { new ClientDestinationDto("/downloads/movies", "/downloads/movies", InLibrary("/downloads/movies", "/media/movies", "Фільми"), true) },
            body.Items,
            strict: true);
    }

    [Fact]
    public async Task ClientDestinations_shows_a_download_folder_inside_a_library_folder_in_that_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        _factory.FolderClient.Arm("/downloads/movies/new");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D18 — its honest status, never forced "outside the libraries".
        Assert.Equal(
            [
                ("/downloads/movies/new", DestinationPathStatus.InLibrary, true),
                ("/downloads/movies", DestinationPathStatus.InLibrary, false),
            ],
            body.Items.Select(item => (item.Key, item.Check.Status, item.IsClientDefaultFolder)));
        Assert.Equal(["Фільми"], body.Items[0].Check.LibraryNames);
    }

    [Fact]
    public async Task ClientDestinations_proposes_a_folder_two_libraries_list_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        await _libraries.Create(_jellyfin, "Мульфільми", "/media/movies");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equal([["/downloads/movies"]], _factory.FolderClient.Proposals);
        var movies = Assert.Single(body.Items, item => item.Key == "/downloads/movies");
        Assert.Equal(["Мульфільми", "Фільми"], movies.Check.LibraryNames);
    }

    [Fact]
    public async Task ClientDestinations_lists_a_library_folder_the_client_can_not_see_with_its_sentence_and_without_an_app_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Серіали", "/media/tv-shows");
        _factory.FolderClient.CantSee("/downloads/tv-shows");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D5 — never hidden, never pickable: no app path, and the library it would be in for the dialog's label.
        Assert.Equivalent(
            new ClientDestinationDto(
                "/downloads/tv-shows",
                "/downloads/tv-shows",
                new DestinationCheckDto(
                    DestinationPathStatus.FolderNotSeen, "/downloads/tv-shows", null, ["Серіали"], FakeFolderDownloadClient.CantSeeProblem("/downloads/tv-shows"))),
            body.Items[^1],
            strict: true);
    }

    [Fact]
    public async Task ClientDestinations_lists_a_download_folder_too_long_to_store_with_why_and_never_as_one_to_pick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        // 257 characters, one more than a destination holds: the client's own, which the app never proposed.
        var folder = $"/downloads/{new string('c', 257 - "/downloads/".Length)}";
        _factory.FolderClient.Arm(folder);
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D1 — listed with why, as a library folder too long to store is; not pickable, as a folder the client can't see
        // isn't (D5): no app path, so nothing treats it as a place to check or send to.
        Assert.Equivalent(
            new[]
            {
                new ClientDestinationDto(
                    folder,
                    folder,
                    new DestinationCheckDto(
                        DestinationPathStatus.FolderNotSeen,
                        folder,
                        null,
                        [],
                        $"The folder “{folder}” in “Transmission” is longer than the 256 characters a destination can hold."),
                    IsClientDefaultFolder: true),
                new ClientDestinationDto("/downloads/movies", "/downloads/movies", InLibrary("/downloads/movies", "/media/movies", "Фільми")),
            },
            body.Items,
            strict: true);
        Assert.Empty(body.Unplaced);
    }

    // ── the library folders it has no folder for ────────────────────────────────────────────────

    [Fact]
    public async Task ClientDestinations_lists_a_library_folder_no_mapping_of_the_client_leads_to_with_how_to_fix_it()
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
        // Never proposed, never listed: there is no folder of the client's to ask about.
        Assert.Equal([["/downloads/movies"]], _factory.FolderClient.Proposals);
        Assert.Equal(["/downloads/complete", "/downloads/movies"], body.Items.Select(item => item.Key));
    }

    [Fact]
    public async Task ClientDestinations_lists_a_library_folder_the_clients_mappings_lead_to_from_two_places_with_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        // Two mounts of one folder: both lead back to the library, from different places.
        var client = await CreateClientAsync([new PathMappingPair("/downloads", "/media"), new PathMappingPair("/data", "/media")]);

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equivalent(
            new[]
            {
                new UnplacedLibraryFolderDto(
                    "Фільми",
                    "/media/movies",
                    "“Transmission”'s path mappings lead to the library “Фільми” (“/media/movies”) from different places: “/downloads/movies” and “/data/movies”. Keep one of those mappings, then look again."),
            },
            body.Unplaced,
            strict: true);
        Assert.Equal([[]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task ClientDestinations_lists_a_library_folder_whose_folder_in_the_client_is_too_long_to_store()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // 253 characters in the app; 257 once /media becomes /downloads, one more than a destination holds.
        var name = new string('m', 253 - "/media/".Length);
        await _libraries.Create(_jellyfin, "Фільми", $"/media/{name}");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equivalent(
            new[]
            {
                new UnplacedLibraryFolderDto(
                    "Фільми",
                    $"/media/{name}",
                    $"The folder of the library “Фільми” in “Transmission” (“/downloads/{name}”) is longer than the 256 characters a destination can hold."),
            },
            body.Unplaced,
            strict: true);
        Assert.Equal([[]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task ClientDestinations_lists_a_library_folder_jellyfins_own_mappings_can_not_place_naming_that_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin's /data is the app's /media: its /config folder is nowhere in the app.
        var jellyfin = await _integrations.Create("jellyfin", "Bedroom", apiKey: JellyfinApiKey, pathMappings: [new PathMappingPair("/data", "/media")]);
        await _libraries.Create(jellyfin, "Фільми", "/data/movies");
        await _libraries.Create(jellyfin, "Домашнє відео", "/config/home");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equivalent(
            new[]
            {
                new UnplacedLibraryFolderDto(
                    "Домашнє відео",
                    "/config/home",
                    "The library “Домашнє відео” is at “/config/home” in Jellyfin, which none of its integration's path mappings brings into this app, so the app can't find its folder in “Transmission”. Check the path mappings of “Bedroom”."),
            },
            body.Unplaced,
            strict: true);
        Assert.Equal([["/downloads/movies"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task ClientDestinations_lists_a_library_folder_whose_name_the_clients_windows_folder_can_not_hold_with_the_mappers_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        // A legal ext4 name that a Windows path would read as two folders.
        await _libraries.Create(_jellyfin, "Мульфільми", @"/media/a\b");
        _factory.FolderClient.Arm(@"D:\Torrents\complete");
        var client = await CreateClientAsync([new PathMappingPair(@"D:\Torrents", "/media")]);

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equivalent(
            new[]
            {
                new UnplacedLibraryFolderDto(
                    "Мульфільми",
                    @"/media/a\b",
                    @"“Transmission” has no folder for the library “Мульфільми”: “/media/a\b” can't be written under “D:\Torrents”: its folder name “a\b” contains \, which separates folders in a Windows path."),
            },
            body.Unplaced,
            strict: true);
        // Never proposed: the folder it would be is another place in a Windows path.
        Assert.Equal([[@"D:\Torrents\movies"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task ClientDestinations_lists_the_library_folders_it_has_none_for_by_library_name_whichever_way_each_went()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Read in this order: the folders Jellyfin's mappings place first (Мульфільми, no mapping of the client's leads
        // there), then those they can't (Домашнє відео and Серіали, nowhere in the app).
        await _libraries.Create(_jellyfin, "Мульфільми", "/srv/cartoons");
        var jellyfin = await _integrations.Create("jellyfin", "Bedroom", apiKey: JellyfinApiKey, pathMappings: [new PathMappingPair("/data", "/media")]);
        await _libraries.Create(jellyfin, "Домашнє відео", "/config/home");
        await _libraries.Create(jellyfin, "Серіали", "/config/tv-shows");
        var client = await CreateClientAsync();

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // The order the dialog lists libraries in, never the order they were read in.
        Assert.Equal(["Домашнє відео", "Мульфільми", "Серіали"], body.Unplaced.Select(folder => folder.LibraryName));
        Assert.Equal(["/config/home", "/srv/cartoons", "/config/tv-shows"], body.Unplaced.Select(folder => folder.Folder));
    }

    [Fact]
    public async Task ClientDestinations_leaves_jellyfins_collections_library_out_of_the_folders_and_of_those_it_has_none_for()
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
        // Decision 9 — not a candidate, so no "no folder for the library Колекції" either.
        Assert.Empty(body.Unplaced);
        Assert.Equal(
            [["/downloads/documentary", "/downloads/cartoons", "/downloads/tv-shows", "/downloads/movies"]],
            _factory.FolderClient.Proposals);
        Assert.DoesNotContain(body.Items, item => item.Check.LibraryNames.Contains("Колекції"));
    }

    // ── the Edit dialog's own destination ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClientDestinations_for_the_edit_dialog_lists_its_destinations_folder_as_itself_once_no_library_leads_there()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/films");
        var client = await CreateClientAsync();
        // Saved when Фільми was at /media/movies; the library has moved since.
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Фільми", "/downloads/movies");

        //Act
        var withIt = await ReadAsync(await ListAsync(client, $"?destinationId={destination.Id}"), cancellationToken);
        var without = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        // D4 — confirmed by the client and checked as itself, never "missing"; the Add dialog doesn't offer it.
        Assert.Equal(
            [
                ("/downloads/complete", DestinationPathStatus.OutsideLibraries),
                ("/downloads/films", DestinationPathStatus.InLibrary),
                ("/downloads/movies", DestinationPathStatus.OutsideLibraries),
            ],
            withIt.Items.Select(item => (item.Key, item.Check.Status)));
        Assert.Equal(["/downloads/complete", "/downloads/films"], without.Items.Select(item => item.Key));
        Assert.Equal([["/downloads/films", "/downloads/movies"], ["/downloads/films"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task ClientDestinations_for_the_edit_dialog_of_another_clients_destination_proposes_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        var client = await CreateClientAsync();
        var other = await CreateClientAsync(name: "Seedbox");
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(other, "Old", "/downloads/old");

        //Act
        var body = await ReadAsync(await ListAsync(client, $"?destinationId={destination.Id}"), cancellationToken);

        //Assert
        Assert.Equal(["/downloads/complete", "/downloads/movies"], body.Items.Select(item => item.Key));
        Assert.Equal([["/downloads/movies"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task ClientDestinations_for_the_edit_dialog_of_a_stored_folder_that_is_not_a_full_path_lists_it_with_why_and_never_proposes_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Фільми", "/media/movies");
        var client = await CreateClientAsync();
        // A hand-edited row: a folder no client could confirm.
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Фільми", "movies");

        //Act
        var response = await ListAsync(client, $"?destinationId={destination.Id}");

        //Assert
        // D3 — that row's problem alone: the client still lists its folders, and is never handed it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Null(body.Error);
        Assert.Equivalent(
            new ClientDestinationDto(
                "movies",
                "movies",
                new DestinationCheckDto(
                    DestinationPathStatus.FolderNotSeen,
                    null,
                    null,
                    [],
                    "The app can't use the folder “movies” of this destination: it isn't a full path in “Transmission”.")),
            body.Items[^1],
            strict: true);
        Assert.Equal([["/downloads/movies"]], _factory.FolderClient.Proposals);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("12345")]
    public async Task ClientDestinations_with_a_malformed_destination_id_is_400_and_asks_nobody(string destinationId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();

        //Act
        var response = await ListAsync(client, $"?destinationId={destinationId}");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json("The destination being edited isn't a valid id. Reload the page and open it again.", "destinations.edited-invalid"),
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.FolderClient.Calls);
    }

    // ── a client that can't be asked ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientDestinations_of_a_switched_off_folder_client_says_so_with_its_kind_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Мульфільми", "/srv/cartoons");
        var client = await CreateClientAsync(isEnabled: false);

        //Act
        var response = await ListAsync(client);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equivalent(
            new ClientDestinationListResponse(
                [], "“Transmission” is switched off. Switch it on under Integrations to use it for destinations.", DownloadDestinationKind.Folder, []),
            await ReadAsync(response, cancellationToken),
            strict: true);
        Assert.Empty(_factory.FolderClient.Calls);
    }

    [Fact]
    public async Task ClientDestinations_of_a_folder_client_that_fails_is_its_sentence_with_nothing_unplaced()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _libraries.Create(_jellyfin, "Мульфільми", "/srv/cartoons");
        var client = await CreateClientAsync();
        _factory.FolderClient.ListingFailure = IntegrationCallResult<IReadOnlyList<ClientDestination>>.Failed(
            IntegrationCallStatus.Refused, Message.Of("integrations.adapter.credentials-refused", ("service", "Transmission")));

        //Act
        var body = await ReadAsync(await ListAsync(client), cancellationToken);

        //Assert
        Assert.Equivalent(
            new ClientDestinationListResponse([], "Transmission refused the user name or password.", DownloadDestinationKind.Folder, []),
            body,
            strict: true);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A folder client whose <c>/downloads</c> is the app's <c>/media</c>, unless other mappings are given.</summary>
    private Task<IntegrationConnectionEntity> CreateClientAsync(
        IReadOnlyList<PathMappingPair>? pathMappings = null, string name = ClientName, bool isEnabled = true) =>
        _integrations.Create(
            FakeFolderDownloadClient.TypeId,
            name,
            isEnabled: isEnabled,
            pathMappings: pathMappings ?? [new PathMappingPair("/downloads", "/media")]);

    private Task<HttpResponseMessage> ListAsync(IntegrationConnectionEntity client, string query = "") =>
        _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}{query}", _owner.AccessToken);

    private static async Task<ClientDestinationListResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static DestinationCheckDto InLibrary(string servicePath, string appPath, string libraryName) =>
        new(DestinationPathStatus.InLibrary, servicePath, appPath, [libraryName], null);
}
