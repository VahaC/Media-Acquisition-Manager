using System.Net;
using System.Text.Json;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-38, ADR 0034 — the Download dialog's question, before Send, whether a destination's folder can be checked for
// files a download would write over: no reason when the category's folder lies inside the Media folders, else why
// not — outside them, or no path in the app (a mapping that covers none of the client's folders, a client switched
// off, a client that can't list its categories or no longer lists this one). Every signed-in user's, as the dialog
// is; a reason, never a path; the destination's client is asked where its category saves.
public sealed class DownloadsOverwriteCheckHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads/overwrite-check";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsOverwriteCheckHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await CreateClientAsync("qBittorrent", [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task OverwriteCheck_of_a_destination_inside_the_media_folders_gives_no_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(["notCheckedReason"], document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("notCheckedReason").ValueKind);
    }

    [Fact]
    public async Task OverwriteCheck_of_a_destination_outside_the_media_folders_says_so_to_a_member_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media/tv-shows");

        //Act
        var owners = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}", _owner.AccessToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        Assert.Equal("NotCheckedFolderOutsideMediaFolders", await ReasonAsync(owners, cancellationToken));
        Assert.Equal("NotCheckedFolderOutsideMediaFolders", await ReasonAsync(members, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_category_folder_without_a_path_in_the_app_says_it_is_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var client = await CreateClientAsync("qBittorrent elsewhere", [new PathMappingPair("/elsewhere", "/media")]);
        var films = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Films", "Movies");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={films.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotCheckedFolderUnknown", await ReasonAsync(response, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_destination_whose_client_is_switched_off_says_its_folder_is_unknown_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent off",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: false,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        var films = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Films", "Movies");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={films.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotCheckedFolderUnknown", await ReasonAsync(response, cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task OverwriteCheck_when_the_client_can_not_list_its_categories_says_the_folder_is_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/categories"
            ? VendorResponses.Text("boom", HttpStatusCode.InternalServerError)
            : null;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        // A question the client can't answer is still an answer to the dialog: the folder is unknown, never a 5xx.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotCheckedFolderUnknown", await ReasonAsync(response, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_destination_whose_category_the_client_no_longer_lists_says_the_folder_is_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var gone = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Old films", "Films (removed)");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={gone.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotCheckedFolderUnknown", await ReasonAsync(response, cancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?destinationId=")]
    [InlineData("?destinationId=not-a-guid")]
    [InlineData("?destinationId=00000000-0000-0000-0000-000000000000")]
    public async Task OverwriteCheck_without_a_destination_is_400_with_the_rules_sentence(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}{query}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"Pick the destination to send it to."}""", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_destination_that_no_longer_exists_is_404_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={Guid.NewGuid()}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            """{"error":"The destination you picked no longer exists. Reload the page and pick one again."}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_without_signing_in_is_401()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, IReadOnlyList<PathMappingPair> pathMappings) =>
        _integrations.Create(
            "qbittorrent",
            name,
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: pathMappings);

    private static async Task<string?> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("notCheckedReason").GetString();
    }
}
