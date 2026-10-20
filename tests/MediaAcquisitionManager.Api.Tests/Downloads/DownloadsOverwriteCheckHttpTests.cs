using System.Net;
using System.Text.Json;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-38, ADR 0034 — the Download dialog's question, before Send, whether a destination's folder can be checked for
// files a download would write over: no reason when the category's folder lies inside the Media folders, else why
// not — outside them, or no path in the app (a mapping that covers none of the client's folders, a client switched
// off, a client that can't list its categories or no longer lists this one). Every signed-in user's, as the dialog
// is; a reason, never a path; the destination's client is asked where its category saves. MAM-31 — a folder that is
// unknown says why, with the client's name: it couldn't be asked (with its own sentence, for the owner alone), it has
// no such category, or no path mapping brings its folder in.
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
        Assert.Equal(
            ["notCheckedReason", "folderUnknownCause", "clientName", "clientError"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.All(document.RootElement.EnumerateObject(), property => Assert.Equal(JsonValueKind.Null, property.Value.ValueKind));
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
        // MAM-31 — a cause and a client only for a folder that is unknown.
        Assert.Equal(((string?)null, (string?)null, (string?)null), await CauseAsync(owners, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_category_folder_without_a_path_in_the_app_says_no_path_mapping_of_its_client_brings_it_in()
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
        // MAM-31 — qBittorrent saves Movies in /downloads/movies, which /elsewhere doesn't cover: the dialog names the
        // client whose mappings to fix, and no path.
        Assert.Equal(("NoPathMapping", "qBittorrent elsewhere", (string?)null), await CauseAsync(response, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_of_a_category_folder_two_path_mappings_send_to_different_places_gives_no_cause()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        // What an older build or a hand-edited database can hold: /downloads/movies lands in /media/movies and in
        // /elsewhere/movies.
        var client = await CreateClientAsync(
            "qBittorrent twice",
            [new PathMappingPair("/downloads", "/media"), new PathMappingPair("/downloads/movies", "/elsewhere/movies")]);
        var films = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Films", "Movies");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={films.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotCheckedFolderUnknown", await ReasonAsync(response, cancellationToken));
        // MAM-31 — adding a mapping wouldn't fix it, so the dialog says nothing of adding one: no cause, its general line.
        Assert.Equal(((string?)null, (string?)null, (string?)null), await CauseAsync(response, cancellationToken));
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
        // MAM-31 — not asked, and the owner reads why, with what to do.
        Assert.Equal(
            ("ClientNotAsked", "qBittorrent off", "“qBittorrent off” is switched off. Switch it on under Integrations to use it for destinations."),
            await CauseAsync(response, cancellationToken));
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
        // MAM-31 — a member reads which client couldn't be asked, never its own sentence.
        Assert.Equal(("ClientNotAsked", "qBittorrent", (string?)null), await CauseAsync(response, cancellationToken));
    }

    [Fact]
    public async Task OverwriteCheck_when_qbittorrent_is_unreachable_gives_the_owner_its_sentence_and_a_member_its_name_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _qbittorrent.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var owners = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}", _owner.AccessToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        // MAM-31 — the live check's case with qBittorrent: the sentence names the client, its address and the fix.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        Assert.Equal("NotCheckedFolderUnknown", await ReasonAsync(owners, cancellationToken));
        Assert.Equal(
            (
                "ClientNotAsked",
                "qBittorrent",
                $"Couldn't reach qBittorrent at {_qbittorrent.BaseUrl}: nothing accepted the connection. Check the address and port, and that qBittorrent is running."
            ),
            await CauseAsync(owners, cancellationToken));
        Assert.Equal(("ClientNotAsked", "qBittorrent", (string?)null), await CauseAsync(members, cancellationToken));
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
        // MAM-31 — qBittorrent keeps "category".
        Assert.Equal(("CategoryMissing", "qBittorrent", (string?)null), await CauseAsync(response, cancellationToken));
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
        Assert.Equal(ErrorBody.Json("Pick the destination to send it to.", "downloads.request.destination-missing"), await response.Content.ReadAsStringAsync(cancellationToken));
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
            ErrorBody.Json("The destination you picked no longer exists. Reload the page and pick one again.", "downloads.destination-gone"),
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

    /// <summary>MAM-31 — why a folder is unknown, the client named, and the client's own sentence.</summary>
    private static async Task<(string? Cause, string? Client, string? Error)> CauseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        // MAM-47 — the client's sentence read as written: in the pseudo pass it must be one wrapped message, which proves it
        // came through the renderer, and is compared as its English.
        var error = root.GetProperty("clientError").GetString();
        return (
            root.GetProperty("folderUnknownCause").GetString(),
            root.GetProperty("clientName").GetString(),
            error is not null && AcceptLanguageHandler.AskedForPseudo(response.RequestMessage) ? PseudoText.Unwrap(error, "the client's error") : error);
    }
}
