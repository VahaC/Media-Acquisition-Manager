using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11 — reading the destinations: the list's search, order and paging on the server, and each row's
// live check (ADR 0003) in every status — the qBittorrent samples' categories mapped through /downloads →
// /media into the Jellyfin samples' library folders, and every way the app can't say. Reads answer 200
// whatever the client said (ADR 0005). Only qBittorrent's and Jellyfin's networks are faked; the real
// adapters run.
public sealed class DestinationsListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondClientAddress = "http://qbittorrent-2.test:8080";

    // Spelled out rather than read from the product code: these are what the owner reads.
    private const string Consequence = "so automatic replacement stays off for this destination";
    private const string LibrariesUnknownProblem =
        "The app doesn't know the media library folders yet. Sync a library from the Library page, then look again.";
    private const string LibraryFoldersUnmappedProblem =
        "None of the media library folders maps to a folder of the app through its integration's path mappings. Check those mappings under Integrations, then look again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private FakeQBittorrentServer _secondQBittorrent = null!;

    public DestinationsListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Each way a client couldn't be asked, and the sentence its destinations then show.</summary>
    public static TheoryData<string, string> UnavailableClients => new()
    {
        { "unreachable", "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running." },
        { "refused sign-in", "qBittorrent refused the user name or password." },
        { "old version", "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again." },
        { "type not registered", "This version of the app can't send downloads to “retired-client”." },
        { "secret unreadable", "The stored API key of “qBittorrent” can't be read with this installation's encryption key. Enter the API key again and save." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _qbittorrent = new FakeQBittorrentServer();
        _secondQBittorrent = new FakeQBittorrentServer(SecondClientAddress);
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, _secondQBittorrent);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_checks_every_sample_category_into_the_sample_library_that_holds_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons");
        var documentary = await _destinations.Create(client, "Documentary", "Documentary");
        var shows = await _destinations.Create(client, "TV Shows", "TV Shows");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // ADR 0003's consequence, end to end: /downloads/movies is /media/movies in the app, the folder of
        // the Jellyfin library "Фільми".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Equivalent(
            new DestinationListResponse(
                [
                    Destination(cartoons, client, InLibrary("/downloads/cartoons", "/media/cartoons", "Мульфільми"), now),
                    Destination(documentary, client, InLibrary("/downloads/documentary", "/media/documentary", "Документалки"), now),
                    Destination(movies, client, InLibrary("/downloads/movies", "/media/movies", "Фільми"), now),
                    Destination(shows, client, InLibrary("/downloads/tv-shows", "/media/tv-shows", "Серіали"), now),
                ],
                TotalCount: 4,
                Page: 1,
                PageSize: 25),
            body,
            strict: true);
    }

    [Fact]
    public async Task List_answers_the_statuses_as_names_and_the_instants_in_utc()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The JSON the Destinations page reads, as it arrives.
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"isDefault\":true", raw, StringComparison.Ordinal);
        Assert.Contains("\"clientDestinationKey\":\"Movies\"", raw, StringComparison.Ordinal);
        Assert.Contains(
            "\"check\":{\"status\":\"InLibrary\",\"servicePath\":\"/downloads/movies\",\"appPath\":\"/media/movies\",\"libraryNames\":[\"Фільми\"],\"problem\":null}",
            raw,
            StringComparison.Ordinal);
        Assert.Matches("\"createdUtc\":\"[^\"]+Z\"", raw);
    }

    [Fact]
    public async Task List_orders_by_name_then_by_the_clients_name_in_any_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var beta = await CreateClientAsync("beta");
        var alpha = await CreateClientAsync("Alpha", _secondQBittorrent);
        await _destinations.Create(beta, "movies", "Movies");
        await _destinations.Create(alpha, "Movies", "Movies");
        await _destinations.Create(beta, "Cartoons", "Cartoons");
        await _destinations.Create(alpha, "TV Shows", "TV Shows");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Equal(
            ["Cartoons beta", "Movies Alpha", "movies beta", "TV Shows Alpha"],
            body.Items.Select(item => $"{item.Name} {item.DownloadClientName}"));
    }

    [Theory]
    [InlineData("movies", new[] { "Movies", "Movies 4K" })]
    [InlineData("  4k ", new[] { "Movies 4K" })]
    [InlineData("МУЛЬТ", new[] { "Мультфільми" })]
    [InlineData("%", new string[0])]
    [InlineData("", new[] { "Movies", "Movies 4K", "Мультфільми" })]
    public async Task List_searches_the_name_in_any_letter_case(string search, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies", "Movies");
        await _destinations.Create(client, "Movies 4K", "Movies");
        await _destinations.Create(client, "Мультфільми", "Cartoons");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Equal(expected, body.Items.Select(item => item.Name));
        Assert.Equal(expected.Length, body.TotalCount);
    }

    [Fact]
    public async Task List_pages_on_the_server_and_counts_every_match()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
            await _destinations.Create(client, name, "Movies");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=2", _owner.AccessToken);

        //Assert
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Equal(["C", "D"], body.Items.Select(item => item.Name));
        Assert.Equal(5, body.TotalCount);
        Assert.Equal(2, body.Page);
        Assert.Equal(2, body.PageSize);
    }

    [Fact]
    public async Task List_past_the_last_page_is_empty_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=3&pageSize=25", _owner.AccessToken);

        //Assert
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Empty(body.Items);
        Assert.Equal(1, body.TotalCount);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData("page=0", "Page must be 1 or more.")]
    [InlineData("pageSize=0", "Page size must be between 1 and 100.")]
    [InlineData("pageSize=101", "Page size must be between 1 and 100.")]
    public async Task List_with_a_page_it_does_not_take_is_400_with_the_rule(string query, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_asks_each_client_on_the_page_once_whatever_number_of_destinations_it_holds()
    {
        //Arrange
        var client = await CreateClientAsync();
        var second = await CreateClientAsync("Second", _secondQBittorrent);
        await _destinations.Create(client, "Movies", "Movies");
        await _destinations.Create(client, "Cartoons", "Cartoons");
        await _destinations.Create(client, "TV Shows", "TV Shows");
        await _destinations.Create(second, "Documentary", "Documentary");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Signed in once and read once each: the version (for the gate and the folder rules) and the
        // categories. No default save path, as every sample category has a folder of its own.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories"],
            RequestsTo(_qbittorrent.BaseUrl));
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories"],
            RequestsTo(_secondQBittorrent.BaseUrl));
    }

    [Fact]
    public async Task List_of_a_category_saving_outside_every_library_is_outside_libraries()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.AddCategory("Incoming", "/downloads/incoming");
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Incoming", "Incoming");

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(
                DestinationPathStatus.OutsideLibraries, "/downloads/incoming", "/media/incoming", [], $"“/media/incoming” is outside every media library folder, {Consequence}."),
            check,
            strict: true);
    }

    [Fact]
    public async Task List_of_a_category_no_path_mapping_covers_is_path_unknown_with_the_mappings_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.AddCategory("Elsewhere", "/data/elsewhere");
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Elsewhere", "Elsewhere");

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(
                DestinationPathStatus.PathUnknown, "/data/elsewhere", null, [], $"No path mapping of this integration covers “/data/elsewhere”, {Consequence}."),
            check,
            strict: true);
    }

    [Fact]
    public async Task List_of_a_subcategory_whose_folder_the_client_does_not_report_is_path_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        // 5.1 puts a subcategory without a folder where use_subcategories says, which only its preferences
        // report — and the app never reads those.
        _qbittorrent.Version = "v5.1.4";
        _qbittorrent.AddCategory("Movies/4K", "");
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies 4K", "Movies/4K");

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(
                DestinationPathStatus.PathUnknown, null, null, [], $"The client doesn't say which folder this category saves to, {Consequence}."),
            check,
            strict: true);
    }

    [Fact]
    public async Task List_of_a_category_without_a_folder_of_its_own_asks_for_the_default_save_path_and_checks_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.AddCategory("movies", "");
        _qbittorrent.DefaultSavePath = "/downloads";
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Lower-case movies", "movies");

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        // qBittorrent's own rule: a top-level category without a folder saves to <default>/<name>.
        Assert.Equivalent(InLibrary("/downloads/movies", "/media/movies", "Фільми"), check, strict: true);
        Assert.Contains("GET /api/v2/app/defaultSavePath", RequestsTo(_qbittorrent.BaseUrl));
    }

    [Fact]
    public async Task List_before_any_library_is_synced_is_libraries_unknown_with_both_paths()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client);

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(DestinationPathStatus.LibrariesUnknown, "/downloads/movies", "/media/movies", [], LibrariesUnknownProblem),
            check,
            strict: true);
    }

    [Fact]
    public async Task List_while_no_stored_library_folder_maps_into_the_app_says_to_check_the_mappings_not_to_sync()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin's mappings cover /data only, and its libraries are under /media and /config: every
        // folder is stored, none has an app path.
        await SyncSampleLibrariesAsync([new PathMappingPair("/data", "/srv/data")]);
        var client = await CreateClientAsync();
        await _destinations.Create(client);

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(DestinationPathStatus.LibrariesUnknown, "/downloads/movies", "/media/movies", [], LibraryFoldersUnmappedProblem),
            check,
            strict: true);
    }

    [Fact]
    public async Task List_of_a_category_the_client_no_longer_has_is_category_missing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.Categories.Remove("Movies");
        var client = await CreateClientAsync();
        await _destinations.Create(client);

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(DestinationPathStatus.CategoryMissing, null, null, [], "The client has no category “Movies” any more."),
            check,
            strict: true);
    }

    [Fact]
    public async Task List_of_a_switched_off_client_is_client_disabled_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync(isEnabled: false);
        await _destinations.Create(client);

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(
            new DestinationCheckDto(
                DestinationPathStatus.ClientDisabled, null, null, [], "“qBittorrent” is switched off. Switch it on under Integrations to use it for destinations."),
            check,
            strict: true);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(UnavailableClients))]
    public async Task List_of_a_client_that_could_not_be_asked_is_200_with_client_unavailable_and_the_reason(string unavailable, string expectedProblem)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateUnavailableClientAsync(unavailable);
        await _destinations.Create(client);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // ADR 0005: "it couldn't be asked, because …" is the row's answer, not a failure of the list.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equivalent(new DestinationCheckDto(DestinationPathStatus.ClientUnavailable, null, null, [], expectedProblem), item.Check, strict: true);
    }

    [Fact]
    public async Task List_of_one_client_that_fails_still_checks_the_other_clients_destinations()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        var second = await CreateClientAsync("Second", _secondQBittorrent);
        await _destinations.Create(client, "Movies", "Movies");
        await _destinations.Create(second, "Cartoons", "Cartoons");
        _secondQBittorrent.Unreachable = true;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Equal(
            [("Cartoons", DestinationPathStatus.ClientUnavailable), ("Movies", DestinationPathStatus.InLibrary)],
            body.Items.Select(item => (item.Name, item.Check.Status)));
    }

    [Fact]
    public async Task List_of_a_client_whose_answer_the_app_can_not_read_keeps_the_others_and_logs_no_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        var second = await CreateClientAsync("Second", _secondQBittorrent);
        await _destinations.Create(client, "Movies", "Movies");
        await _destinations.Create(second, "Cartoons", "Cartoons");
        // Not a transport failure the adapter knows: something nobody foresaw, as a parser bug would be.
        _qbittorrent.Intercept = _ => throw new InvalidOperationException($"Unexpected answer quoting {FakeQBittorrentServer.DefaultPassword}");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // It costs that client its answer, never the other's, and never a 500 for the whole page.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadListAsync(response, cancellationToken);
        Assert.Equivalent(
            new DestinationCheckDto(DestinationPathStatus.ClientUnavailable, null, null, [], "The app couldn't read what “qBittorrent” answered."),
            body.Items.Single(item => item.Name == "Movies").Check,
            strict: true);
        Assert.Equal(DestinationPathStatus.InLibrary, body.Items.Single(item => item.Name == "Cartoons").Check.Status);
        // Logged as an error with the exception's type and stack, never its message: that can quote what
        // the client sent.
        var logged = Assert.Single(_factory.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("System.InvalidOperationException", logged.Message, StringComparison.Ordinal);
        Assert.Contains(" at ", logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected answer quoting", logged.Message, StringComparison.Ordinal);
        Assert.Null(logged.Exception);
    }

    [Fact]
    public async Task List_whose_kept_session_qbittorrent_forgot_signs_in_once_more_and_checks_as_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        await _destinations.Create(client);
        await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        // Idle past qBittorrent's timeout, or restarted: the session the adapter keeps is unknown now.
        _qbittorrent.ExpireSessions();

        //Act
        var check = await SingleCheckAsync(cancellationToken);

        //Assert
        Assert.Equivalent(InLibrary("/downloads/movies", "/media/movies", "Фільми"), check, strict: true);
        Assert.Equal(
            [
                "POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories",
                // The second list: the kept session refused, one more sign-in, the same read again.
                "GET /api/v2/app/version", "POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories",
            ],
            RequestsTo(_qbittorrent.BaseUrl));
    }

    [Fact]
    public async Task List_of_a_later_page_keeps_the_session_of_the_first_and_signs_in_no_more()
    {
        //Arrange
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies", "Movies");
        await _destinations.Create(client, "Cartoons", "Cartoons");

        //Act
        await _factory.GetHttpRequest($"{BaseUrl}?page=1&pageSize=1", _owner.AccessToken);
        await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=1", _owner.AccessToken);

        //Assert
        Assert.Single(RequestsTo(_qbittorrent.BaseUrl), request => request == "POST /api/v2/auth/login");
    }

    [Fact]
    public async Task Get_is_200_with_the_destination_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        await _destinations.Create(client, "Cartoons", "Cartoons");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(Destination(movies, client, InLibrary("/downloads/movies", "/media/movies", "Фільми"), now), body, strict: true);
    }

    [Fact]
    public async Task Get_of_a_destination_that_does_not_exist_is_a_bare_404_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task List_and_get_log_neither_the_address_the_password_the_session_nor_a_sentence()
    {
        //Arrange
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client);
        var refused = await CreateClientAsync("Refused", _secondQBittorrent);
        await _destinations.Create(refused, "Cartoons", "Cartoons");
        _secondQBittorrent.Password = "changed-in-qbittorrent";

        //Act
        await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        await _factory.GetHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);

        //Assert
        // Information with ids, type and status only (the connection test's rule): an address or a
        // service's sentence may carry what it said back about a credential.
        var sessions = _factory.QBittorrentHttp.Requests.SelectMany(request => request.HeaderValues("Cookie")).Distinct().ToList();
        Assert.NotEmpty(sessions);
        Assert.Contains(_factory.Logs.Entries, entry => entry.Message.StartsWith("Destination listing of integration", StringComparison.Ordinal));
        string[] unlogged = ["qbittorrent.test", "qbittorrent-2.test", FakeQBittorrentServer.DefaultPassword, "refused the user name", .. sessions];
        Assert.All(_factory.Logs.Entries, entry => Assert.All(unlogged, text =>
        {
            Assert.DoesNotContain(text, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(text, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A qBittorrent at a fake server's address that signs in with the server's credentials and maps its
    // /downloads to the app's /media, as ADR 0003's reference installation does.
    private Task<IntegrationConnectionEntity> CreateClientAsync(string name = "qBittorrent", FakeQBittorrentServer? server = null, bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            name,
            (server ?? _qbittorrent).BaseUrl,
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
            case "type not registered":
                return await _integrations.Create("retired-client", "qBittorrent", kind: IntegrationKind.DownloadClient);
            case "secret unreadable":
                return await _integrations.CreateWithUnreadableApiKey("qbittorrent", "qBittorrent");
            default:
                throw new ArgumentOutOfRangeException(nameof(unavailable), unavailable, null);
        }
    }

    // The libraries of VendorSamples/Jellyfin/virtual-folders.json, stored by a real sync: Фільми at
    // /media/movies, Мульфільми at /media/cartoons, Документалки at /media/documentary, Серіали at
    // /media/tv-shows and Колекції at /config/data/collections.
    private async Task SyncSampleLibrariesAsync(IReadOnlyList<PathMappingPair>? pathMappings = null)
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey, pathMappings: pathMappings);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);
    }

    private async Task<DestinationCheckDto> SingleCheckAsync(CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        return Assert.Single((await ReadListAsync(response, cancellationToken)).Items).Check;
    }

    private List<string> RequestsTo(string baseUrl) =>
        [.. _factory.QBittorrentHttp.Requests
            .Where(request => request.Uri.AbsoluteUri.StartsWith(baseUrl, StringComparison.Ordinal))
            .Select(request => $"{request.Method} {request.Uri.AbsolutePath}")];

    private static async Task<DestinationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static DestinationCheckDto InLibrary(string servicePath, string appPath, string libraryName) =>
        new(DestinationPathStatus.InLibrary, servicePath, appPath, [libraryName], null);

    private static DestinationDto Destination(
        DestinationMappingEntity destination, IntegrationConnectionEntity client, DestinationCheckDto check, DateTime createdUtc) =>
        new(destination.Id, destination.Name, destination.IsDefault, client.Id, client.Name, destination.ClientDestinationKey, check, createdUtc);
}
