using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-11 — the root folders of every library a sync reads, stored for the destination check (ADR 0003): each
// library's folders in the server's order, replaced by every sync that reads the library again, kept as they
// were by a sync that fails (ADR 0008), and gone with their library and with the integration that reported
// it. MAM-30 — but none for Jellyfin's Collections library, whose folder is Jellyfin's own records (decision 9): the
// library is kept, its folder never stored, and one an older build stored goes at the next sync. Syncs and the removal
// run over real HTTP and the real worker; only Jellyfin's network is faked.
public sealed class LibraryLocationFoldersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";

    // The libraries of VendorSamples/Jellyfin/virtual-folders.json beside JellyfinSamples.MoviesLocationId.
    private const string CartoonsLocationId = "2e7d31f7c627f72ad0912eff1c00353d";
    private const string DocumentaryLocationId = "dea595f0747f8862151db0192e565150";
    private const string ShowsLocationId = "f97af1337db58125aaa9a3f3695ab163";
    private const string CollectionsLocationId = "f28915b8cd74d585b0c181975ec68c3a";

    /// <summary>The sample's folders, one per library that holds media files, as <see cref="FoldersAsync"/> describes
    /// them.</summary>
    private static readonly string[] SampleFolders =
    [
        "Документалки 0 /media/documentary",
        "Мульфільми 0 /media/cartoons",
        "Серіали 0 /media/tv-shows",
        "Фільми 0 /media/movies",
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeJellyfinServer _secondJellyfin = null!;

    public LibraryLocationFoldersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _secondJellyfin = new FakeJellyfinServer(SecondJellyfinAddress) { ApiKey = ApiKey };
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, _secondJellyfin);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_of_the_samples_stores_the_root_folder_of_every_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Every library that holds media files, the ones that hold no file yet included: which of them a destination
        // may save into is the destination check's question. MAM-30 — Jellyfin's own collections folder is no such
        // library's, so it is never one.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal(SampleFolders, await FoldersAsync(jellyfin.Id, cancellationToken));
    }

    [Fact]
    public async Task Sync_keeps_jellyfins_collections_library_but_stores_no_folder_for_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Decision 9 — CollectionType boxsets: the library stays (the Library page still names it), its folder doesn't.
        await _factory.WithDbAsync(async db =>
        {
            var collections = await db.LibraryLocations.SingleAsync(location => location.SourceItemId == CollectionsLocationId, cancellationToken);
            Assert.Equal("Колекції", collections.Name);
            Assert.False(await db.LibraryLocationFolders.AnyAsync(folder => folder.LibraryLocationId == collections.Id, cancellationToken));
        });
    }

    [Fact]
    public async Task Resync_drops_the_folder_an_older_build_stored_for_the_collections_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // What a sync before MAM-30 left: Jellyfin's collections folder stored like any library's.
        await _factory.WithDbAsync(async db =>
        {
            var collections = await db.LibraryLocations.SingleAsync(location => location.SourceItemId == CollectionsLocationId, cancellationToken);
            db.LibraryLocationFolders.Add(new LibraryLocationFolderEntity { LibraryLocationId = collections.Id, Position = 0, ServicePath = "/config/data/collections" });
            await db.SaveChangesAsync(cancellationToken);
        });
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(SampleFolders, await FoldersAsync(jellyfin.Id, cancellationToken));
    }

    [Fact]
    public async Task Sync_of_a_library_with_several_folders_stores_them_in_the_servers_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        // Not in alphabetical order: the server's order is kept as it is.
        Library(_jellyfin, JellyfinSamples.MoviesLocationId)["Locations"] = new JsonArray("/mnt/disk2/movies", "/media/movies", "/mnt/disk1/movies");

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(
            ["Фільми 0 /mnt/disk2/movies", "Фільми 1 /media/movies", "Фільми 2 /mnt/disk1/movies"],
            (await FoldersAsync(jellyfin.Id, cancellationToken)).Where(folder => folder.StartsWith("Фільми ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Resync_replaces_every_librarys_folders_with_those_the_server_lists_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // A second folder, a moved folder, a library without folders and a library removed from the server.
        Library(_jellyfin, JellyfinSamples.MoviesLocationId)["Locations"] = new JsonArray("/media/movies", "/media/movies-4k");
        Library(_jellyfin, CartoonsLocationId)["Locations"] = new JsonArray("/media/animation");
        Library(_jellyfin, DocumentaryLocationId)["Locations"] = new JsonArray();
        _jellyfin.VirtualFolders.Remove(Library(_jellyfin, ShowsLocationId));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        // The removed library's folder went with its library; nothing of the old lists is left over.
        Assert.Equal(
            ["Мульфільми 0 /media/animation", "Фільми 0 /media/movies", "Фільми 1 /media/movies-4k"],
            await FoldersAsync(jellyfin.Id, cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(3, await db.LibraryLocationFolders.CountAsync(cancellationToken));
            Assert.False(await db.LibraryLocations.AnyAsync(location => location.SourceItemId == ShowsLocationId, cancellationToken));
        });
    }

    [Fact]
    public async Task Resync_of_the_same_folders_stores_the_same_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal(SampleFolders, await FoldersAsync(jellyfin.Id, cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(4, await db.LibraryLocationFolders.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sync_that_fails_after_reading_the_libraries_keeps_the_folders_the_last_good_sync_stored()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        // The libraries are read with their new folders; the movies after them fail.
        Library(_jellyfin, JellyfinSamples.MoviesLocationId)["Locations"] = new JsonArray("/media/films");
        _jellyfin.Intercept = request => request.RequestUri!.AbsolutePath == "/Items"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // ADR 0008 — nothing is written until the whole library is read.
        Assert.Equal(LibrarySyncStatus.Failed, run.Status);
        Assert.Equal(SampleFolders, await FoldersAsync(jellyfin.Id, cancellationToken));
    }

    [Fact]
    public async Task Sync_of_one_integration_leaves_the_folders_of_another_as_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await CreateJellyfinAsync("Living room");
        var bedroom = await CreateJellyfinAsync("Bedroom", SecondJellyfinAddress);
        await _factory.SyncLibraryAsync(livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(bedroom.Id, _owner.AccessToken, cancellationToken);
        // The same library ids on both servers: each integration's libraries are its own rows.
        Library(_secondJellyfin, JellyfinSamples.MoviesLocationId)["Locations"] = new JsonArray("/data/movies");

        //Act
        await _factory.SyncLibraryAsync(bedroom.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(SampleFolders, await FoldersAsync(livingRoom.Id, cancellationToken));
        Assert.Contains("Фільми 0 /data/movies", await FoldersAsync(bedroom.Id, cancellationToken));
    }

    [Fact]
    public async Task Delete_of_the_library_integration_leaves_none_of_its_folders_and_keeps_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var removed = await CreateJellyfinAsync("Living room");
        var kept = await CreateJellyfinAsync("Bedroom", SecondJellyfinAddress);
        await _factory.SyncLibraryAsync(removed.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(kept.Id, _owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.DeleteHttpRequest($"/api/settings/integrations/{removed.Id}", _owner.AccessToken);

        //Assert
        // The purge deletes the libraries set-based (ExecuteDelete): their folders go by the database's
        // cascade, with nothing tracked to take them along.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await FoldersAsync(removed.Id, cancellationToken));
        Assert.Equal(SampleFolders, await FoldersAsync(kept.Id, cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var keptLocations = await db.LibraryLocations.Select(location => location.Id).ToListAsync(cancellationToken);
            var folderLocations = await db.LibraryLocationFolders.Select(folder => folder.LibraryLocationId).ToListAsync(cancellationToken);
            Assert.Equal(4, folderLocations.Count);
            Assert.All(folderLocations, location => Assert.Contains(location, keptLocations));
        });
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync(string name, string baseUrl = FakeJellyfinServer.DefaultBaseUrl) =>
        _integrations.Create("jellyfin", name, baseUrl, apiKey: ApiKey);

    private static JsonObject Library(FakeJellyfinServer server, string locationId) =>
        server.VirtualFolders.Single(library => (string?)library!["ItemId"] == locationId)!.AsObject();

    // "<library> <position> <path>" for every stored folder of the libraries of integrationId, in ordinal
    // order: equal lists are the same folders in the same places.
    private async Task<IReadOnlyList<string>> FoldersAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> folders = [];
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.LibraryLocationFolders
                .Join(db.LibraryLocations, folder => folder.LibraryLocationId, location => location.Id, (folder, location) => new
                {
                    location.SourceConnectionId,
                    location.Name,
                    folder.Position,
                    folder.ServicePath,
                })
                .Where(row => row.SourceConnectionId == integrationId)
                .ToListAsync(cancellationToken);
            folders = [.. rows.Select(row => $"{row.Name} {row.Position} {row.ServicePath}").Order(StringComparer.Ordinal)];
        });
        return folders;
    }
}
