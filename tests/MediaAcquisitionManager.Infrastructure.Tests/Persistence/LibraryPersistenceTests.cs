using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-8 — the library inventory's promises, pinned on the migrated schema: one active sync per
// integration however two starts race, sync history that outlives the integration and the user, and a
// file that outlives its library location — whose root folders (MAM-11) don't. Deletes go through
// ExecuteDelete, so what happens to the dependent rows is the database's own doing, not EF's tracking.
public sealed class LibraryPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public LibraryPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Theory]
    [InlineData(LibrarySyncStatus.Queued, LibrarySyncStatus.Queued)]
    [InlineData(LibrarySyncStatus.Queued, LibrarySyncStatus.Running)]
    [InlineData(LibrarySyncStatus.Running, LibrarySyncStatus.Queued)]
    public async Task A_second_active_run_of_one_integration_is_refused(LibrarySyncStatus first, LibrarySyncStatus second)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var runs = Runs(db);
        await runs.Create(library.Id, library.Name, first);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(library.Id, library.Name, second));

        //Assert
        // The backstop behind the service's 409, for two starts racing each other.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: LibrarySyncRuns.IntegrationConnectionId", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.LibrarySyncRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Finished_runs_never_block_a_new_active_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var runs = Runs(db);
        await runs.Create(library.Id, library.Name, LibrarySyncStatus.Succeeded);
        await runs.Create(library.Id, library.Name, LibrarySyncStatus.Failed, error: "Jellyfin refused the API key.");
        await runs.Create(library.Id, library.Name, LibrarySyncStatus.Succeeded);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(library.Id, library.Name, LibrarySyncStatus.Queued));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(4, await check.LibrarySyncRuns.CountAsync(x => x.IntegrationConnectionId == library.Id, cancellationToken));
    }

    [Fact]
    public async Task Two_integrations_each_have_an_active_run_of_their_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var library = await integrations.Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var library4K = await integrations.Create("Jellyfin 4K", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "fedcba9876543210");
        var runs = Runs(db);
        await runs.Create(library.Id, library.Name, LibrarySyncStatus.Running);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(library4K.Id, library4K.Name, LibrarySyncStatus.Queued));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(2, await check.LibrarySyncRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task The_sync_run_indexes_have_their_own_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var indexes = await db.Database
            .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM pragma_index_list('LibrarySyncRuns') WHERE \"origin\" = 'c'")
            .ToListAsync(cancellationToken);

        //Assert
        // Two indexes lead with IntegrationConnectionId; each is named, so neither can silently replace
        // the other in a later migration.
        Assert.Equal(
            ["IX_LibrarySyncRuns_IntegrationConnectionId_CreatedUtc", "IX_LibrarySyncRuns_OneActiveRunPerIntegration", "IX_LibrarySyncRuns_RequestedByUserId"],
            indexes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Deleting_an_integration_keeps_its_sync_runs_with_its_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var run = await Runs(db).Create(library.Id, library.Name, LibrarySyncStatus.Succeeded);

        //Act
        await db.IntegrationConnections.Where(x => x.Id == library.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: a run is history; IntegrationName still says which integration it synced.
        var stored = await db.LibrarySyncRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(run.Id, stored.Id);
        Assert.Null(stored.IntegrationConnectionId);
        Assert.Equal("Jellyfin", stored.IntegrationName);
    }

    [Fact]
    public async Task Deleting_a_user_keeps_the_sync_runs_they_started_with_their_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var user = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("vasyl", "P@ssword1");
        await Runs(db).Create(library.Id, library.Name, LibrarySyncStatus.Failed, user.Id, user.UserName, error: "Jellyfin refused the API key.");

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        var stored = await db.LibrarySyncRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Null(stored.RequestedByUserId);
        Assert.Equal("vasyl", stored.RequestedByUserName);
        Assert.Equal(library.Id, stored.IntegrationConnectionId);
    }

    [Fact]
    public async Task Deleting_a_library_location_keeps_its_files_without_a_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var movies = await new LibraryLocationDataFactory(db, TimeProvider.System).Create(library.Id, library.Name, "78ee018b5034640ba414833f0fe9d6b3", "Фільми");
        var work = await new MediaWorkDataFactory(db, TimeProvider.System).Create();
        var version = await new MediaVersionDataFactory(db, TimeProvider.System)
            .Create(work.Id, "00a49b2db2200514168431dc028f0a0b", sourceConnectionId: library.Id, sourceName: library.Name);
        var file = await new PhysicalFileDataFactory(db, TimeProvider.System).Create(
            version.Id,
            "/media/movies/Alien. Collection/Aliens (1986).mkv",
            sourceConnectionId: library.Id,
            sourceName: library.Name,
            libraryLocationId: movies.Id);

        //Act
        await db.LibraryLocations.Where(x => x.Id == movies.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: a location that disappears leaves the file known, just without a library.
        var stored = await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(file.Id, stored.Id);
        Assert.Null(stored.LibraryLocationId);
        Assert.Equal(version.Id, stored.MediaVersionId);
    }

    [Fact]
    public async Task Deleting_a_library_location_removes_its_folders_and_no_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var locations = new LibraryLocationDataFactory(db, TimeProvider.System);
        var movies = await locations.Create(library.Id, library.Name, "78ee018b5034640ba414833f0fe9d6b3", "Фільми");
        var cartoons = await locations.Create(library.Id, library.Name, "2e7d31f7c627f72ad0912eff1c00353d", "Мульфільми");
        var folders = new LibraryLocationFolderDataFactory(db);
        await folders.Create(movies.Id, "/media/movies");
        await folders.Create(movies.Id, "/media/movies-4k", position: 1);
        var cartoonsFolder = await folders.Create(cartoons.Id, "/media/cartoons");

        //Act
        // ExecuteDelete, as the integration delete purges a library (LibrarySnapshotStore.PurgeAsync).
        await db.LibraryLocations.Where(x => x.Id == movies.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // MAM-11 — Cascade in the database itself: a folder says something only about its library.
        var stored = await db.LibraryLocationFolders.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((cartoonsFolder.Id, cartoons.Id, "/media/cartoons"), (stored.Id, stored.LibraryLocationId, stored.ServicePath));
    }

    [Fact]
    public async Task Deleting_a_library_integration_with_its_libraries_leaves_no_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var movies = await new LibraryLocationDataFactory(db, TimeProvider.System).Create(library.Id, library.Name, "78ee018b5034640ba414833f0fe9d6b3", "Фільми");
        await new LibraryLocationFolderDataFactory(db).Create(movies.Id, "/media/movies");

        //Act
        // The libraries first, as the integration delete does: removing the integration alone would only set
        // their source null (ADR 0006), and their folders would stay with them.
        await db.LibraryLocations.Where(x => x.SourceConnectionId == library.Id).ExecuteDeleteAsync(cancellationToken);
        await db.IntegrationConnections.Where(x => x.Id == library.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        Assert.Equal(0, await db.LibraryLocationFolders.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_library_can_not_list_two_folders_at_one_position()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        var locations = new LibraryLocationDataFactory(db, TimeProvider.System);
        var movies = await locations.Create(library.Id, library.Name, "78ee018b5034640ba414833f0fe9d6b3", "Фільми");
        var cartoons = await locations.Create(library.Id, library.Name, "2e7d31f7c627f72ad0912eff1c00353d", "Мульфільми");
        var folders = new LibraryLocationFolderDataFactory(db);
        await folders.Create(movies.Id, "/media/movies");
        // Another library's first folder is a place of its own.
        await folders.Create(cartoons.Id, "/media/cartoons");

        //Act
        var exception = await Record.ExceptionAsync(() => folders.Create(movies.Id, "/media/movies-4k"));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: LibraryLocationFolders.LibraryLocationId, LibraryLocationFolders.Position",
            sqlite.Message,
            StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(2, await check.LibraryLocationFolders.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_folder_can_not_name_a_library_location_that_does_not_exist()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var exception = await Record.ExceptionAsync(() => new LibraryLocationFolderDataFactory(db).Create(Guid.NewGuid(), "/media/movies"));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("FOREIGN KEY constraint failed", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(0, await check.LibraryLocationFolders.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_file_can_not_name_a_library_location_that_does_not_exist()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await new MediaWorkDataFactory(db, TimeProvider.System).Create();
        var version = await new MediaVersionDataFactory(db, TimeProvider.System).Create(work.Id, sourceItemId: null);

        //Act
        var exception = await Record.ExceptionAsync(() => new PhysicalFileDataFactory(db, TimeProvider.System)
            .Create(version.Id, "/media/movies/Aliens (1986).mkv", libraryLocationId: Guid.NewGuid()));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("FOREIGN KEY constraint failed", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(0, await check.PhysicalFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Titles_and_file_paths_keep_their_search_form_beside_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var works = new MediaWorkDataFactory(db, TimeProvider.System);
        var work = await works.Create();
        var version = await new MediaVersionDataFactory(db, TimeProvider.System).Create(work.Id, sourceItemId: null);

        //Act
        await works.AddTitle(work.Id, "Чужі", MediaTitleKind.Display, 1986, null, null, null);
        await new PhysicalFileDataFactory(db, TimeProvider.System).Create(version.Id, "/media/movies/Alien. Collection/Aliens (1986).mkv");

        //Assert
        var title = await db.MediaWorkTitles.AsNoTracking().SingleAsync(cancellationToken);
        var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(("Чужі", "ЧУЖІ"), (title.Text, title.NormalizedText));
        Assert.Equal(
            ("/media/movies/Alien. Collection/Aliens (1986).mkv", "/MEDIA/MOVIES/ALIEN. COLLECTION/ALIENS (1986).MKV"),
            (file.ServicePath, file.NormalizedServicePath));
    }

    [Fact]
    public async Task Sync_run_status_and_location_provenance_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await Integrations(db).Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");
        await Runs(db).Create(library.Id, library.Name, LibrarySyncStatus.Running);
        await new LibraryLocationDataFactory(db, TimeProvider.System).Create(library.Id, library.Name, "78ee018b5034640ba414833f0fe9d6b3", "Фільми");

        //Act
        string[] stored =
        [
            await db.Database.SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM \"LibrarySyncRuns\"").SingleAsync(cancellationToken),
            await db.Database.SqlQueryRaw<string>("SELECT \"SourceKind\" AS \"Value\" FROM \"LibraryLocations\"").SingleAsync(cancellationToken),
        ];

        //Assert
        // By name, like every other enum, and what the active-run index filters on.
        Assert.Equal(["Running", "MediaLibrary"], stored);
    }

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static ILibrarySyncRunDataFactory Runs(ApplicationDbContext db) => new LibrarySyncRunDataFactory(db, TimeProvider.System);
}
