using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the media libraries the Library page syncs from (GET /api/library/sources): every media
// library integration and nothing else, whether this build can read it, its latest and last successful
// sync, and how many of its files the current path mappings leave without a path in the app. For every
// signed-in user, except a run's error, which can name an address, the path problem, which quotes the
// owner's mappings, and who started a run, the owner's sign-in name: the owner's only (LibraryMapper,
// LibrarySyncService).
public sealed class LibrarySourcesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/sources";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public LibrarySourcesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sources_without_a_token_is_401()
    {
        //Arrange
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sources_as_a_signed_in_non_owner_is_200_with_every_media_library_by_name_and_nothing_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await _integrations.Create("jellyfin", "living room", apiKey: ApiKey);
        var cellar = await _integrations.Create("jellyfin", "Cellar", apiKey: ApiKey, isEnabled: false);
        var attic = await _integrations.Create(FakeIntegrationAdapter.TypeId, "Attic");
        var basement = await _integrations.Create("retired-service", "Basement", kind: IntegrationKind.MediaLibrary);
        await _integrations.Create("qbittorrent", "Downloads", userName: "admin", password: "correct-horse-battery");
        await _integrations.Create("torznab", "Toloka", apiKey: ApiKey);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // By name, letter case ignored. Whether a type can read a library is the registry's answer: the
        // fake is a media library type without the library contract, and the retired type isn't
        // registered at all. A switched-off one is listed, keeping its snapshot, but can't be synced.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                new LibrarySourceDto(attic.Id, "Attic", FakeIntegrationAdapter.DisplayName, true, false, null, null, 0, null),
                new LibrarySourceDto(basement.Id, "Basement", null, true, false, null, null, 0, null),
                new LibrarySourceDto(cellar.Id, "Cellar", "Jellyfin", false, true, null, null, 0, null),
                new LibrarySourceDto(livingRoom.Id, "living room", "Jellyfin", true, true, null, null, 0, null),
            ],
            (await ReadListAsync(response, cancellationToken)).Items);
    }

    [Fact]
    public async Task Sources_gives_each_library_its_latest_run_and_its_last_successful_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var synced = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var neverSynced = await _integrations.Create("jellyfin", "Bedroom", apiKey: ApiKey);
        var runs = new LibrarySyncRunSeeder(_factory.Scope);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Seeded out of order and at explicit times, so "latest" is the clock's answer, not the insert
        // order's or the ids'.
        var lastSucceeded = await runs.Create(synced, LibrarySyncStatus.Succeeded, _owner.User, now.AddMinutes(-2));
        var latest = await runs.Create(synced, LibrarySyncStatus.Failed, _owner.User, now.AddMinutes(-1), "Jellyfin answered 500 Internal Server Error. Its own log says why.");
        await runs.Create(synced, LibrarySyncStatus.Succeeded, _owner.User, now.AddMinutes(-3));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var items = (await ReadListAsync(response, cancellationToken)).Items;
        var source = items.Single(item => item.IntegrationId == synced.Id);
        Assert.Equal(
            new LibrarySyncRunDto(
                latest.Id, synced.Id, "Living room", LibrarySyncStatus.Failed, "owner", now.AddMinutes(-1), now.AddMinutes(-1), now.AddMinutes(-1),
                0, null, null, null, null, "Jellyfin answered 500 Internal Server Error. Its own log says why."),
            source.LatestRun);
        Assert.Equal((lastSucceeded.Id, LibrarySyncStatus.Succeeded), (source.LastSucceededRun!.Id, source.LastSucceededRun.Status));
        var never = items.Single(item => item.IntegrationId == neverSynced.Id);
        Assert.Equal((null, null), (never.LatestRun, never.LastSucceededRun));
    }

    [Fact]
    public async Task Sources_gives_the_error_of_a_failed_sync_and_who_started_each_run_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var succeeded = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        _factory.JellyfinHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError);
        var failed = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The owner reads why; the page still shows the library as the earlier sync stored it.
        var ownersSource = Assert.Single((await ReadListAsync(owners, cancellationToken)).Items);
        Assert.Equal(
            (failed.Id, "Couldn't reach Jellyfin at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that Jellyfin is running."),
            (ownersSource.LatestRun!.Id, ownersSource.LatestRun.Error));
        Assert.Equal(succeeded, ownersSource.LastSucceededRun);
        Assert.Equal(("owner", "owner"), (ownersSource.LatestRun.RequestedByUserName, ownersSource.LastSucceededRun!.RequestedByUserName));
        // Everyone else learns that it failed, never the address, and not who started either run: only
        // the owner starts one, so the name is the owner's sign-in name.
        var membersBody = await members.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("127.0.0.1", membersBody, StringComparison.Ordinal);
        var membersSource = Assert.Single((await ReadListAsync(members, cancellationToken)).Items);
        Assert.Equal((null, null), (membersSource.LatestRun!.RequestedByUserName, membersSource.LastSucceededRun!.RequestedByUserName));
        Assert.Equal(
            ownersSource with
            {
                LatestRun = ownersSource.LatestRun with { RequestedByUserName = null, Error = null },
                LastSucceededRun = ownersSource.LastSucceededRun with { RequestedByUserName = null },
            },
            membersSource);
    }

    [Fact]
    public async Task Sources_counts_the_files_the_current_path_mappings_give_no_path_and_says_why_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping for the shows only: none of the four movie files is covered.
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The first problem in path order, so the warning reads the same on every poll.
        var ownersSource = Assert.Single((await ReadListAsync(owners, cancellationToken)).Items);
        Assert.Equal(
            (4, $"No path mapping of this integration covers “{AliensTheatricalPath}”."),
            (ownersSource.FilesWithoutAppPathCount, ownersSource.PathProblem));
        // The reason quotes a path and the mappings, which are the owner's; the count is everyone's.
        var membersSource = Assert.Single((await ReadListAsync(members, cancellationToken)).Items);
        Assert.Equal((4, null), (membersSource.FilesWithoutAppPathCount, membersSource.PathProblem));
    }

    [Fact]
    public async Task Sources_counts_files_without_an_app_path_with_the_mappings_as_they_are_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        // The owner fixes the mappings; no new sync.
        await _factory.PutHttpRequest(
            $"/api/settings/integrations/{jellyfin.Id}",
            _owner.AccessToken,
            new UpdateIntegrationRequest(
                "Living room",
                jellyfin.BaseUrl,
                PathMappings: [new PathMappingRequest("/media/tv-shows", "/data/tv"), new PathMappingRequest("/media/movies", "/data/movies")]));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Counted when asked, not stored with the run: the warning agrees with the works list at once.
        var source = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((0, null), (source.FilesWithoutAppPathCount, source.PathProblem));
    }

    private static async Task<LibrarySourceListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
