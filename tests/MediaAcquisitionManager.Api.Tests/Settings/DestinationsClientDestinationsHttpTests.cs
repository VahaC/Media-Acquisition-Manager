using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11 — what a download client offers the Add or Edit destination dialog: its categories in its own
// order, each checked as a destination of it would be, and 200 whatever the client said (ADR 0005) — the
// sentence in "error" when it couldn't be asked. And what the app ever sends a client: the sign-in and
// GETs, never a save path (specification §3.2).
public sealed class DestinationsClientDestinationsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string LibrariesUnknownProblem =
        "The app doesn't know the media library folders yet. Sync a library from the Library page, then look again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;

    public DestinationsClientDestinationsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Each way a client couldn't be asked, the sentence the dialog then shows, and — MAM-30 — what its
    /// destinations are by its type, which a type this build doesn't register can't say.</summary>
    public static TheoryData<string, string, DownloadDestinationKind?> UnavailableClients => new()
    {
        { "unreachable", "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.", DownloadDestinationKind.Category },
        { "refused sign-in", "qBittorrent refused the user name or password.", DownloadDestinationKind.Category },
        { "old version", "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", DownloadDestinationKind.Category },
        { "switched off", "“qBittorrent” is switched off. Switch it on under Integrations to use it for destinations.", DownloadDestinationKind.Category },
        { "type not registered", "This version of the app can't send downloads to “retired-client”.", null },
        { "secret unreadable", "The stored API key of “qBittorrent” can't be read with this installation's encryption key. Enter the API key again and save.", DownloadDestinationKind.Category },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ClientDestinations_lists_the_sample_categories_in_the_clients_order_each_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new ClientDestinationListResponse(
                [
                    new ClientDestinationDto("Cartoons", "Cartoons", InLibrary("/downloads/cartoons", "/media/cartoons", "Мульфільми")),
                    new ClientDestinationDto("Documentary", "Documentary", InLibrary("/downloads/documentary", "/media/documentary", "Документалки")),
                    new ClientDestinationDto("Movies", "Movies", InLibrary("/downloads/movies", "/media/movies", "Фільми")),
                    new ClientDestinationDto("TV Shows", "TV Shows", InLibrary("/downloads/tv-shows", "/media/tv-shows", "Серіали")),
                ],
                Error: null,
                DestinationKind: DownloadDestinationKind.Category,
                Unplaced: []),
            body,
            strict: true);
    }

    [Fact]
    public async Task ClientDestinations_names_on_the_wire_each_field_the_dialog_reads_for_a_category_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);

        //Assert
        // MAM-30 — the dialog reads these by name: what the destinations are, the library folders the client has no
        // folder for (never null), and whether each entry is the client's own download folder — never one for a
        // category client. A DTO default would hide a field the server left out, so the text is read as it came.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        Assert.Equal(["items", "error", "destinationKind", "unplaced"], PropertyNames(root));
        Assert.Equal("Category", root.GetProperty("destinationKind").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("unplaced").ValueKind);
        Assert.Equal(0, root.GetProperty("unplaced").GetArrayLength());
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(["key", "displayName", "check", "isClientDefaultFolder"], PropertyNames(item));
            Assert.Equal(JsonValueKind.False, item.GetProperty("isClientDefaultFolder").ValueKind);
        });
    }

    [Fact]
    public async Task ClientDestinations_before_any_library_is_synced_checks_each_as_libraries_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(4, body!.Items.Count);
        Assert.All(body.Items, item =>
        {
            Assert.Equal(DestinationPathStatus.LibrariesUnknown, item.Check.Status);
            Assert.Equal(LibrariesUnknownProblem, item.Check.Problem);
        });
        Assert.Equal("/media/movies", body.Items.Single(item => item.Key == "Movies").Check.AppPath);
    }

    [Fact]
    public async Task ClientDestinations_of_a_category_saving_outside_the_libraries_shows_why_before_it_is_picked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.AddCategory("Incoming", "/downloads/incoming");
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new ClientDestinationDto(
                "Incoming",
                "Incoming",
                new DestinationCheckDto(
                    DestinationPathStatus.OutsideLibraries,
                    "/downloads/incoming",
                    "/media/incoming",
                    [],
                    "“/media/incoming” is outside every media library folder, so automatic replacement stays off for this destination.")),
            body!.Items[^1],
            strict: true);
    }

    [Theory]
    [MemberData(nameof(UnavailableClients))]
    public async Task ClientDestinations_of_a_client_that_could_not_be_asked_is_200_with_its_sentence_and_no_items(
        string unavailable, string expectedError, DownloadDestinationKind? expectedKind)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateUnavailableClientAsync(unavailable);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);

        //Assert
        // ADR 0005: "it couldn't be asked, because …" is the answer, not a failure of this API.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(new ClientDestinationListResponse([], expectedError, expectedKind, []), body, strict: true);
    }

    [Theory]
    [InlineData("no integration")]
    [InlineData("a media library")]
    public async Task ClientDestinations_of_an_id_that_is_not_a_download_client_is_a_bare_404(string target)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = target == "no integration" ? Guid.NewGuid() : (await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey)).Id;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task ClientDestinations_masks_the_password_and_the_session_in_the_clients_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        // A redirect whose target repeats what the app sent: the password, and the session it has just
        // been given.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/version", StringComparison.Ordinal)
            ? VendorResponses.Redirect(
                $"http://sso.test/{FakeQBittorrentServer.DefaultPassword}/{SessionOf(request)}/")
            : null;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);

        //Assert
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var session = SessionOf(_factory.QBittorrentHttp.Requests[^1]);
        Assert.Contains("sso.test", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeQBittorrentServer.DefaultPassword, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(session, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_call_the_destinations_make_to_a_client_is_the_sign_in_or_a_get_and_none_names_a_save_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        // A category without a folder of its own, so the default save path is read too.
        _qbittorrent.AddCategory("Unsorted", "");

        //Act
        // Every endpoint that asks a client, the way the page and the dialog use them.
        var answers = new List<HttpResponseMessage>
        {
            await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken),
            await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies")),
        };
        var created = await answers[^1].Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        answers.Add(await _factory.PutHttpRequest($"{BaseUrl}/{created!.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", client.Id, "Cartoons")));
        answers.Add(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken));
        answers.Add(await _factory.GetHttpRequest($"{BaseUrl}/{created.Id}", _owner.AccessToken));
        answers.Add(await _factory.DeleteHttpRequest($"{BaseUrl}/{created.Id}", _owner.AccessToken));

        //Assert
        // Guards the guard: every step ran, and the client was asked by each that asks.
        Assert.All(answers, answer => Assert.True(answer.IsSuccessStatusCode, $"{answer.RequestMessage?.RequestUri} answered {answer.StatusCode}."));
        var requests = _factory.QBittorrentHttp.Requests;
        Assert.Equal(5, requests.Count(request => request.Uri.AbsolutePath == "/api/v2/torrents/categories"));
        Assert.Contains(requests, request => request.Uri.AbsolutePath == "/api/v2/app/defaultSavePath");
        Assert.All(requests, request =>
        {
            Assert.True(
                request.Method == HttpMethod.Get || (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/v2/auth/login"),
                $"{request.Method} {request.Uri.AbsolutePath} is neither the sign-in nor a read.");
            Assert.DoesNotContain("savepath", request.Uri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("savepath", request.Body ?? "", StringComparison.OrdinalIgnoreCase);
            // Never preferences: they carry the Web UI's API key and the proxy and mail passwords.
            Assert.DoesNotContain("preferences", request.Uri.AbsolutePath, StringComparison.Ordinal);
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateClientAsync(bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: isEnabled,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    // A download client the app can't ask in the way named.
    private async Task<IntegrationConnectionEntity> CreateUnavailableClientAsync(string unavailable)
    {
        switch (unavailable)
        {
            case "unreachable":
                _qbittorrent.Unreachable = true;
                return await CreateClientAsync();
            case "refused sign-in":
                _qbittorrent.Password = "changed-in-qbittorrent";
                return await CreateClientAsync();
            case "old version":
                _qbittorrent.Version = "v4.3.9";
                return await CreateClientAsync();
            case "switched off":
                return await CreateClientAsync(isEnabled: false);
            case "type not registered":
                return await _integrations.Create("retired-client", "qBittorrent", kind: IntegrationKind.DownloadClient);
            case "secret unreadable":
                return await _integrations.CreateWithUnreadableApiKey("qbittorrent", "qBittorrent");
            default:
                throw new ArgumentOutOfRangeException(nameof(unavailable), unavailable, null);
        }
    }

    // The libraries of VendorSamples/Jellyfin/virtual-folders.json, stored by a real sync.
    private async Task SyncSampleLibrariesAsync()
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);
    }

    private static string SessionOf(HttpRequestMessage request) =>
        request.Headers.NonValidated["Cookie"].Single().Split('=', 2)[1];

    private static string SessionOf(RecordedHttpRequest request) =>
        request.HeaderValues("Cookie").Single().Split('=', 2)[1];

    private static DestinationCheckDto InLibrary(string servicePath, string appPath, string libraryName) =>
        new(DestinationPathStatus.InLibrary, servicePath, appPath, [libraryName], null);

    private static List<string> PropertyNames(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];
}
