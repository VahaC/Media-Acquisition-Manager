using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the libraries the Library page filters by (GET /api/library/locations): for every signed-in
// user, only those that hold a file (a choice that lists nothing is never offered), by name in Ukrainian
// alphabetical order, then by the integration that reported them.
public sealed class LibraryLocationsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/locations";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";
    private const string CartoonsLocationId = "2e7d31f7c627f72ad0912eff1c00353d";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibrarySnapshotSeeder _snapshots = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeJellyfinServer _secondJellyfin = null!;

    public LibraryLocationsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _snapshots = new LibrarySnapshotSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _secondJellyfin = new FakeJellyfinServer(SecondJellyfinAddress) { ApiKey = ApiKey };
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, _secondJellyfin);
        // A cartoon beside the four sample movies, so two of the five libraries hold a file.
        var cartoon = JellyfinSamples.Renumbered(JellyfinSamples.Movie(JellyfinSamples.District13Id), 1);
        cartoon["Name"] = "Коко";
        cartoon["MediaSources"]![0]!["Path"] = "/media/cartoons/Coco (2017)/Coco.mkv";
        _jellyfin.Movies.Add(cartoon);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Locations_without_a_token_is_401()
    {
        //Arrange
        await SyncAsync("Living room");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Locations_as_a_signed_in_non_owner_is_200_with_the_libraries_that_hold_a_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync("Living room");
        var ids = await LocationIdsAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // Five libraries were read; "Документалки", "Колекції" and "Серіали" hold none of the files.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                new LibraryLocationDto(ids[CartoonsLocationId], "Мульфільми", "Living room"),
                new LibraryLocationDto(ids[JellyfinSamples.MoviesLocationId], "Фільми", "Living room"),
            ],
            (await ReadListAsync(response, cancellationToken)).Items);
    }

    [Fact]
    public async Task Locations_of_two_libraries_with_one_name_are_ordered_by_their_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncAsync("Living room");
        await SyncAsync("Bedroom", SecondJellyfinAddress);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The second server has no cartoon: its "Мульфільми" holds nothing and isn't offered.
        Assert.Equal(
            [("Мульфільми", "Living room"), ("Фільми", "Bedroom"), ("Фільми", "Living room")],
            (await ReadListAsync(response, cancellationToken)).Items.Select(location => (location.Name, location.SourceName)));
    }

    [Fact]
    public async Task Locations_before_any_sync_is_empty()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadListAsync(response, cancellationToken)).Items);
    }

    // A Jellyfin integration of this name at this address, its library synced into the snapshot.
    private async Task SyncAsync(string name, string baseUrl = FakeJellyfinServer.DefaultBaseUrl)
    {
        var jellyfin = await _integrations.Create("jellyfin", name, baseUrl, apiKey: ApiKey);
        await _snapshots.Sync(jellyfin, _owner.User);
    }

    // The stored id of every library, by Jellyfin's own id.
    private async Task<Dictionary<string, Guid>> LocationIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, Guid>();
        await _factory.WithDbAsync(async db =>
            ids = await db.LibraryLocations.ToDictionaryAsync(location => location.SourceItemId!, location => location.Id, cancellationToken));
        return ids;
    }

    private static async Task<LibraryLocationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryLocationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
