using System.Net;
using System.Net.Http.Json;
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

// MAM-30, ADR 0039 — saving a destination of a client whose destinations are folders (D4): only a folder the client
// lists — its download folder or a library folder mapped back, never the request's own folder proposed — and one it can
// see; stored in the client's canonical spelling, never the request's. An update whose folder is the same folder in any
// spelling asks nobody and keeps the stored one (decision 3). The destination's name is the torrent's label in the
// client, so it can't hold a comma — on create, on update, and on a rename that asks no client — for a folder client
// only. And the Destinations page checks each stored folder as itself (D3): a moved library or an edited mapping shows
// where it is now, a folder the client can't see says so, and one that isn't a full path is that row's problem alone.
// MAM-31 — a missing folder is asked for as the client's folder, never as a category.
public sealed class DestinationsFolderSaveHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string CommaError =
        "“Transmission” shows this destination's name as the torrent's label, and a label can't contain a comma. Pick a name without one.";
    private const string FolderMissingError = "Pick the folder of “Transmission” this destination saves to.";

    private readonly FolderClientApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _client = null!;

    public DestinationsFolderSaveHttpTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        var libraries = new LibraryLocationSeeder(_factory.Scope);
        await libraries.Create(jellyfin, "Фільми", "/media/movies");
        await libraries.Create(jellyfin, "Серіали", "/media/tv-shows");
        _client = await _integrations.Create(FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _factory.FolderClient.Arm();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── create ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_of_a_library_folder_the_client_lists_is_201_and_stores_it_as_the_client_spells_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The same folder, spelt with a trailing slash.
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Фільми", _client.Id, "/downloads/movies/"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            ("/downloads/movies", DownloadDestinationKind.Folder, DestinationPathStatus.InLibrary),
            (body!.ClientDestinationKey, body.DestinationKind, body.Check.Status));
        Assert.Equal("/downloads/movies", await StoredKeyAsync(cancellationToken));
        // D4 — the request's folder was never proposed: only the library folders were.
        Assert.Equal([["/downloads/tv-shows", "/downloads/movies"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task Create_of_the_clients_download_folder_is_201()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Unsorted", _client.Id, FakeFolderDownloadClient.DefaultDownloadFolder));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(DestinationPathStatus.OutsideLibraries, body!.Check.Status);
        Assert.Equal(FakeFolderDownloadClient.DefaultDownloadFolder, await StoredKeyAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/downloads/elsewhere")]
    [InlineData("/downloads")]
    [InlineData("/Downloads/movies")]
    public async Task Create_of_a_folder_the_client_does_not_list_is_409_and_saves_nothing(string folder)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Elsewhere", _client.Id, folder));

        //Assert
        // The owner can't type a folder: the client is never asked about it, so it can't be confirmed.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $$"""{"error":"“Transmission” doesn't offer the folder “{{folder}}”. Pick one of the folders it lists."}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.DoesNotContain(_factory.FolderClient.Proposals.SelectMany(proposed => proposed), proposed => proposed == folder);
        Assert.Null(await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_of_a_folder_the_client_can_not_see_is_409_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FolderClient.CantSee("/downloads/tv-shows");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Серіали", _client.Id, "/downloads/tv-shows"));

        //Assert
        // D5 — it would create the folder in its own filesystem.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(FakeFolderDownloadClient.CantSeeProblem("/downloads/tv-shows"), body!.Error);
        Assert.Null(await StoredKeyAsync(cancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_without_a_folder_is_400_asking_for_the_clients_folder_and_asks_no_client(string? folder)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-31 — the live check got "Pick the category or folder of the download client…" with Transmission alone.
        object body = folder is null
            ? new { name = "Фільми", downloadClientId = _client.Id }
            : new CreateDestinationRequest("Фільми", _client.Id, folder);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{FolderMissingError}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.FolderClient.Calls);
        Assert.Null(await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_without_a_folder_and_with_a_comma_in_the_name_asks_for_the_folder_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies, 4K", _client.Id, ""));

        //Assert
        // The destination's own fields before what the client makes of its name, as before MAM-31.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{FolderMissingError}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("Movies, 4K")]
    [InlineData(",")]
    public async Task Create_with_a_comma_in_the_name_is_400_and_asks_no_client(string name)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest(name, _client.Id, "/downloads/movies"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{CommaError}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.FolderClient.Calls);
        Assert.Null(await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_with_a_comma_in_the_name_for_a_category_client_is_saved_as_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var qbittorrent = new FakeQBittorrentServer();
        qbittorrent.Arm(_factory.QBittorrentHttp);
        var client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies, 4K", client.Id, "Movies"));

        //Assert
        // qBittorrent sends a category, never the name: the rule is a folder client's.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Movies", await StoredKeyAsync(cancellationToken));
    }

    // ── update ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_renaming_alone_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var destination = await _destinations.Create(_client, "Фільми", "/downloads/movies");
        // Not even answering: a rename doesn't depend on what the client has.
        _factory.FolderClient.Reset();

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Кіно", _client.Id, "/downloads/movies"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_factory.FolderClient.Calls);
        Assert.Equal("/downloads/movies", await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Update_of_the_same_folder_in_another_spelling_asks_no_client_and_keeps_the_stored_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var destination = await _destinations.Create(_client, "Фільми", "/downloads/movies");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Фільми", _client.Id, "/downloads//movies/"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_factory.FolderClient.Calls);
        Assert.Equal("/downloads/movies", await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Update_to_another_folder_the_client_lists_is_204_and_stores_it_as_the_client_spells_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var destination = await _destinations.Create(_client, "Фільми", "/downloads/movies");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Фільми", _client.Id, "/downloads/tv-shows/"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("/downloads/tv-shows", await StoredKeyAsync(cancellationToken));
        Assert.Equal([["/downloads/tv-shows", "/downloads/movies"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task Update_to_a_folder_the_client_does_not_list_is_409_and_keeps_the_stored_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var destination = await _destinations.Create(_client, "Фільми", "/downloads/movies");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Фільми", _client.Id, "/downloads/typed"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            """{"error":"“Transmission” doesn't offer the folder “/downloads/typed”. Pick one of the folders it lists."}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("/downloads/movies", await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Update_without_a_folder_is_400_asking_for_the_clients_folder_and_keeps_the_stored_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var destination = await _destinations.Create(_client, "Фільми", "/downloads/movies");
        _factory.FolderClient.Reset();

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Фільми", _client.Id, ""));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{FolderMissingError}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.FolderClient.Calls);
        Assert.Equal("/downloads/movies", await StoredKeyAsync(cancellationToken));
    }

    [Fact]
    public async Task Update_renaming_alone_to_a_name_with_a_comma_is_400_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var destination = await _destinations.Create(_client, "Фільми", "/downloads/movies");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Фільми, 4K", _client.Id, "/downloads/movies"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{CommaError}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.FolderClient.Calls);
        await _factory.WithDbAsync(async db =>
            Assert.Equal("Фільми", (await db.DestinationMappings.AsNoTracking().SingleAsync(cancellationToken)).Name));
    }

    [Fact]
    public async Task Update_moving_a_destination_whose_name_has_a_comma_to_a_folder_client_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var qbittorrent = await _integrations.Create("qbittorrent", "qBittorrent");
        var destination = await _destinations.Create(qbittorrent, "Movies, 4K", "Movies");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies, 4K", _client.Id, "/downloads/movies"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"{{CommaError}}"}""", await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Movies", await StoredKeyAsync(cancellationToken));
    }

    // ── the Destinations page ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_checks_each_stored_folder_as_itself_and_names_it_a_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Фільми", "/downloads/movies");
        // A library moved since: no library folder leads to this one any more, and it is still the client's.
        await _destinations.Create(_client, "Старе", "/downloads/old");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [
                ("Старе", "/downloads/old", DestinationPathStatus.OutsideLibraries, (DownloadDestinationKind?)DownloadDestinationKind.Folder),
                ("Фільми", "/downloads/movies", DestinationPathStatus.InLibrary, DownloadDestinationKind.Folder),
            ],
            body!.Items.Select(item => (item.Name, item.ClientDestinationKey, item.Check.Status, item.DestinationKind)));
        // D3 — one listing of the client for the page, every stored folder proposed beside the library folders.
        Assert.Equal([["/downloads/tv-shows", "/downloads/movies", "/downloads/old"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task List_of_a_stored_folder_the_client_can_not_see_shows_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Серіали", "/downloads/tv-shows");
        _factory.FolderClient.CantSee("/downloads/tv-shows");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<DestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new DestinationCheckDto(
                DestinationPathStatus.FolderNotSeen, "/downloads/tv-shows", null, ["Серіали"], FakeFolderDownloadClient.CantSeeProblem("/downloads/tv-shows")),
            Assert.Single(body!.Items).Check,
            strict: true);
    }

    [Fact]
    public async Task List_of_a_stored_folder_that_is_not_a_full_path_is_that_rows_problem_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Фільми", "/downloads/movies");
        await _destinations.Create(_client, "Зламане", "movies");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // D3 — never proposed, so the client still answers for the other row.
        var body = await response.Content.ReadFromJsonAsync<DestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [
                ("Зламане", DestinationPathStatus.FolderNotSeen, (string?)"The app can't use the folder “movies” of this destination: it isn't a full path in “Transmission”."),
                ("Фільми", DestinationPathStatus.InLibrary, null),
            ],
            body!.Items.Select(item => (item.Name, item.Check.Status, item.Check.Problem)));
        Assert.Equal([["/downloads/tv-shows", "/downloads/movies"]], _factory.FolderClient.Proposals);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The one destination's stored key; <c>null</c> when none is stored.</summary>
    private async Task<string?> StoredKeyAsync(CancellationToken cancellationToken)
    {
        string? key = null;
        await _factory.WithDbAsync(async db =>
            key = await db.DestinationMappings.AsNoTracking().Select(destination => destination.ClientDestinationKey).SingleOrDefaultAsync(cancellationToken));
        return key;
    }

    /// <summary>The <c>{ error }</c> body.</summary>
    private sealed record ErrorBody(string Error);
}
