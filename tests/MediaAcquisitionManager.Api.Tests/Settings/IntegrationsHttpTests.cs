using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// The owner's reads: the types of service this build connects to, the integration list and one
// integration. Who may call at all is IntegrationsAccessHttpTests; changes have files of their own.
public sealed class IntegrationsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string KindsSentence = "is not a kind of integration. Use MediaLibrary, ReleaseSearch, DownloadClient or Metadata.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Adapter types ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdapterTypes_is_200_with_jellyfin_torznab_qbittorrent_transmission_and_tmdb_and_what_each_takes_and_the_test_hosts_own_two()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/adapter-types", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var body = JsonSerializer.Deserialize<IntegrationAdapterTypeListResponse>(raw, MediaAcquisitionManagerApiFactory.JsonOptions);
        var types = body!.Items.ToDictionary(type => type.TypeId);
        Assert.Equal(7, types.Count);
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "jellyfin", "Jellyfin", IntegrationKind.MediaLibrary,
                CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: true),
            types["jellyfin"]);
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "torznab", "Torznab", IntegrationKind.ReleaseSearch,
                CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false),
            types["torznab"]);
        // MAM-30 — and what its destinations are: its own categories, asked of its capabilities.
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "qbittorrent", "qBittorrent", IntegrationKind.DownloadClient,
                CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.NotUsed, ReportsFilePaths: true,
                DownloadDestinationKind.Category),
            types["qbittorrent"]);
        // MAM-30 — Transmission in its place: the same credentials, and folders for destinations.
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "transmission", "Transmission", IntegrationKind.DownloadClient,
                CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.NotUsed, ReportsFilePaths: true,
                DownloadDestinationKind.Folder),
            types["transmission"]);
        // MAM-33 — the catalog: its API Read Access Token in the API key field, and no paths of its own; MAM-46 — and the
        // languages it can be asked in, TMDb's recorded primary translations in TMDb's order. No other type has any.
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "tmdb", "TMDb", IntegrationKind.Metadata,
                CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false),
            types["tmdb"] with { CatalogLanguages = null });
        Assert.Equal(JsonSerializer.Deserialize<string[]>(TmdbSamples.ConfigurationPrimaryTranslations()), types["tmdb"].CatalogLanguages);
        // The test host's own types, registered beside the real ones: one with no capability beyond listing, and — MAM-34 —
        // one that identifies items, for the flows above the library adapter.
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "fake", "Fake service", IntegrationKind.MediaLibrary,
                CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.Optional, ReportsFilePaths: true),
            types["fake"]);
        Assert.Equal(
            new IntegrationAdapterTypeDto(
                "fake-library", "Fake library", IntegrationKind.MediaLibrary,
                CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.Optional, ReportsFilePaths: true),
            types["fake-library"]);
        // By name on the wire, which the dialog's type keys on.
        Assert.Contains("\"kind\":\"DownloadClient\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"apiKey\":\"Required\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"destinationKind\":\"Category\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"destinationKind\":\"Folder\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"catalogLanguages\":[\"af-ZA\",\"ar-AE\",", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdapterTypes_are_ordered_by_kind_then_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/adapter-types", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<IntegrationAdapterTypeListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        // Media libraries (Fake library, Fake service, Jellyfin), then release sources, download clients and catalogs.
        Assert.Equal(["fake-library", "fake", "jellyfin", "torznab", "qbittorrent", "transmission", "tmdb"], body!.Items.Select(type => type.TypeId));
    }

    // ── List ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_is_200_with_every_integration_ordered_by_name_whatever_the_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var charlie = await _integrations.Create(name: "charlie");
        var alice = await _integrations.Create(
            "jellyfin", "Alice", apiKey: "alice-api-key-1234", pathMappings: [new PathMappingPair("/data/movies", "/media/movies")]);
        var bob = await _integrations.Create(
            "qbittorrent",
            "bob",
            userName: "admin",
            password: "bob-password",
            isEnabled: false,
            pathMappings: [new PathMappingPair("/downloads", "/media/downloads"), new PathMappingPair("/incomplete", "/media/incomplete")]);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["Alice", "bob", "charlie"], body!.Items.Select(integration => integration.Name));
        Assert.Equivalent(
            new[]
            {
                new IntegrationDto(
                    alice.Id, "Alice", "jellyfin", "Jellyfin", IntegrationKind.MediaLibrary, IsEnabled: true, "http://127.0.0.1:9",
                    UserName: null, HasPassword: false, HasApiKey: true, [new PathMappingDto("/data/movies", "/media/movies")], alice.CreatedUtc),
                new IntegrationDto(
                    bob.Id, "bob", "qbittorrent", "qBittorrent", IntegrationKind.DownloadClient, IsEnabled: false, "http://127.0.0.1:9",
                    "admin", HasPassword: true, HasApiKey: false,
                    [new PathMappingDto("/downloads", "/media/downloads"), new PathMappingDto("/incomplete", "/media/incomplete")],
                    bob.CreatedUtc,
                    // MAM-30 — a download client's only; switched off or not, its type says.
                    DownloadDestinationKind.Category),
                new IntegrationDto(
                    charlie.Id, "charlie", "fake", "Fake service", IntegrationKind.MediaLibrary, IsEnabled: true, "http://127.0.0.1:9",
                    UserName: null, HasPassword: false, HasApiKey: false, [], charlie.CreatedUtc),
            },
            body.Items,
            strict: true);
        Assert.Equal(3, body.TotalCount);
        // The defaults, as the query named no page.
        Assert.Equal(1, body.Page);
        Assert.Equal(25, body.PageSize);
    }

    [Fact]
    public async Task List_sends_every_instant_with_a_trailing_z()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create(name: "Living room");
        await _integrations.Create("qbittorrent", "Downloads");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Read as text: a DateTime deserializer would accept an instant without its zone.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.EndsWith("Z", item.GetProperty("createdUtc").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_pages_on_the_server()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var number = 1; number <= 6; number++)
            await _integrations.Create(name: $"integration-{number}");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=3", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["integration-4", "integration-5", "integration-6"], body!.Items.Select(integration => integration.Name));
        Assert.Equal(6, body.TotalCount);
        Assert.Equal(2, body.Page);
        Assert.Equal(3, body.PageSize);
    }

    [Theory]
    [InlineData(4)]
    // Far enough out that the offset overflows an int: still just an empty page.
    [InlineData(int.MaxValue)]
    public async Task List_past_the_last_page_is_200_with_no_items_and_the_total_count(int page)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var number = 1; number <= 6; number++)
            await _integrations.Create(name: $"integration-{number}");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page={page}&pageSize=3", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Empty(body!.Items);
        Assert.Equal(6, body.TotalCount);
        Assert.Equal(page, body.Page);
    }

    [Theory]
    [InlineData("jelly", "Jellyfin 4K,Living room Jellyfin")]
    [InlineData("JELLY", "Jellyfin 4K,Living room Jellyfin")]
    [InlineData(" Jelly ", "Jellyfin 4K,Living room Jellyfin")]
    // Upper-cased in .NET, not by SQLite's ASCII-only upper(), so Cyrillic ignores letter case too.
    [InlineData("кіно", "Кінотека")]
    [InlineData("КІНО", "Кінотека")]
    public async Task List_searches_on_the_server_for_part_of_the_name_whatever_the_letter_case(string search, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room Jellyfin");
        await _integrations.Create("jellyfin", "Jellyfin 4K");
        await _integrations.Create("torznab", "Prowlarr Toloka");
        await _integrations.Create(name: "Кінотека");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(expected, string.Join(",", body!.Items.Select(integration => integration.Name)));
        Assert.Equal(expected.Split(',').Length, body.TotalCount);
    }

    [Theory]
    // A LIKE pattern would read '_' as any one character and '%' as anything: every row would match.
    [InlineData("_", "a_b")]
    [InlineData("%", "")]
    public async Task List_search_takes_wildcard_characters_literally(string search, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create(name: "a_b");
        await _integrations.Create(name: "axb");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(expected, string.Join(",", body!.Items.Select(integration => integration.Name)));
    }

    [Theory]
    [InlineData("MediaLibrary", "Fake library,Jellyfin")]
    [InlineData("ReleaseSearch", "Torznab")]
    [InlineData("DownloadClient", "qBittorrent")]
    [InlineData("Metadata", "TMDb")]
    // Any letter case, as a person may type it into the address bar.
    [InlineData("downloadclient", "qBittorrent")]
    public async Task List_filters_by_kind_on_the_server(string kind, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Jellyfin");
        await _integrations.Create("torznab", "Torznab");
        await _integrations.Create("qbittorrent", "qBittorrent");
        await _integrations.Create("tmdb", "TMDb");
        await _integrations.Create(name: "Fake library");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?kind={kind}&pageSize=1", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        // One per page, so the count proves the filter ran before paging, on the server.
        Assert.Equal(expected.Split(',')[0], Assert.Single(body!.Items).Name);
        Assert.Equal(expected.Split(',').Length, body.TotalCount);
    }

    [Theory]
    [InlineData("Library", "Library")]
    // A number is refused even where it names a member: the list takes the names the API sends.
    [InlineData("7", "7")]
    [InlineData("1", "1")]
    // Enum.TryParse would OR these into DownloadClient.
    [InlineData("MediaLibrary,ReleaseSearch", "MediaLibrary,ReleaseSearch")]
    public async Task List_with_an_unknown_kind_is_400_naming_the_kinds(string kind, string echoed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?kind={Uri.EscapeDataString(kind)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(($"“{echoed}” {KindsSentence}", "integrations.list.kind-unknown"), await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task List_with_a_page_below_1_is_400(int page)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page={page}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page must be 1 or more.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task List_with_a_page_size_outside_1_to_100_is_400(int pageSize)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?pageSize={pageSize}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page size must be between 1 and 100.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_shows_an_integration_of_an_unregistered_type_by_its_id_without_a_display_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var retired = await _integrations.Create("retired-service", "Old library", kind: IntegrationKind.MediaLibrary);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var item = Assert.Single(body!.Items);
        Assert.Equal(retired.Id, item.Id);
        Assert.Equal("retired-service", item.AdapterType);
        Assert.Null(item.AdapterDisplayName);
        Assert.Equal(IntegrationKind.MediaLibrary, item.Kind);
    }

    // ── Get ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_is_200_with_the_integration_its_path_mappings_in_order_and_its_has_flags()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var downloads = await _integrations.Create(
            "qbittorrent",
            "Downloads",
            baseUrl: "http://127.0.0.1:9/qbittorrent",
            userName: "admin",
            password: "qbittorrent-password",
            // Not in alphabetical order: the answer keeps the order they were entered in.
            pathMappings: [new PathMappingPair("/downloads", "/media"), new PathMappingPair("/complete", "/media/complete")]);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{downloads.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new IntegrationDto(
                downloads.Id, "Downloads", "qbittorrent", "qBittorrent", IntegrationKind.DownloadClient, IsEnabled: true,
                "http://127.0.0.1:9/qbittorrent", "admin", HasPassword: true, HasApiKey: false,
                [new PathMappingDto("/downloads", "/media"), new PathMappingDto("/complete", "/media/complete")],
                downloads.CreatedUtc,
                DownloadDestinationKind.Category),
            body,
            strict: true);
        Assert.Equal(["/downloads", "/complete"], body!.PathMappings.Select(mapping => mapping.ServicePrefix));
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
