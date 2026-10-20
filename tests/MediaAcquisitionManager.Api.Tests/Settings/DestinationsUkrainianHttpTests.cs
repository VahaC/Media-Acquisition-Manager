using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-47 WP14 (ADR 0042, design §8.6) — the Destinations page in Ukrainian, as the owner reads it: each destination's
// path check says why automatic replacement stays off in the reader's language — a folder outside every library, the
// libraries not known yet, and the path mapping's own sentence nested as a clause of the consequence —, and a refused
// name quotes the client and the name in «…». The same request without the header answers English. Each answer spelled
// here, never read from the locale files.
public sealed class DestinationsUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;

    public DestinationsUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_asked_in_ukrainian_says_a_folder_outside_every_library_keeps_replacement_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.AddCategory("Incoming", "/downloads/incoming");
        await _destinations.Create(await CreateClientAsync(), "Incoming", "Incoming");

        //Act
        var ukrainian = await SingleCheckAsync("uk", cancellationToken);
        var english = await SingleCheckAsync(null, cancellationToken);

        //Assert
        Assert.Equal(
            "Тека «/media/incoming» лежить поза всіма теками медіатек, тому автоматична заміна для цього місця збереження залишається вимкненою.",
            ukrainian.Problem);
        Assert.Equal(
            "“/media/incoming” is outside every media library folder, so automatic replacement stays off for this destination.",
            english.Problem);
    }

    [Fact]
    public async Task List_asked_in_ukrainian_nests_the_path_mappings_own_sentence_as_a_clause_of_the_consequence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();
        _qbittorrent.AddCategory("Elsewhere", "/data/elsewhere");
        await _destinations.Create(await CreateClientAsync(), "Elsewhere", "Elsewhere");

        //Act
        var check = await SingleCheckAsync("uk", cancellationToken);

        //Assert
        Assert.Equal(
            "Жодне зіставлення шляхів цієї інтеграції не охоплює шлях «/data/elsewhere»; через це автоматична заміна для цього місця збереження залишається вимкненою.",
            check.Problem);
    }

    [Fact]
    public async Task List_asked_in_ukrainian_before_any_library_sync_points_to_the_library_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(await CreateClientAsync(), "Movies", "Movies");

        //Act
        var check = await SingleCheckAsync("uk", cancellationToken);

        //Assert
        Assert.Equal(
            "Застосунок ще не знає тек медіатек. Синхронізуйте медіатеку на сторінці «Медіатека», а потім перевірте знову.",
            check.Problem);
    }

    [Fact]
    public async Task Create_with_a_name_the_client_has_asked_in_ukrainian_is_409_quoting_the_client_and_the_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies", "Movies");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Cartoons"), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            """{"error":"У клієнті «qBittorrent» уже є місце збереження з назвою «Movies».","code":"destinations.name.taken"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_without_a_name_asked_in_ukrainian_is_400_with_its_code()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest(" ", client.Id, "Movies"), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(("Місцю збереження потрібна назва.", "destinations.name.missing"), await response.ReadErrorBodyAsync(cancellationToken));
    }

    private Task<IntegrationConnectionEntity> CreateClientAsync() =>
        _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    private async Task SyncSampleLibrariesAsync()
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);
    }

    private async Task<DestinationCheckDto> SingleCheckAsync(string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A list asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return Assert.Single((await response.Content.ReadFromJsonAsync<DestinationListResponse>(options, cancellationToken))!.Items).Check;
    }
}
