using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-18 — the scan of the media folders' promises, pinned on the migrated schema: a scan's status and the
// FolderScanned trigger are stored by name; at most one scan waits and one runs, however two requests race,
// while finished scans never block one; a scan outlives the user who asked for it; and a name the scan found
// is stored with no version and kept when a library's version or integration goes — a library's name goes
// with its version, as before. Deletes go through ExecuteDelete, so what happens to the dependent rows is the
// database's own doing, not EF's tracking.
public sealed class FolderScanPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    private const string AliensPath = "/media/movies/Alien. Collection/Aliens (1986).mkv";
    private const string InterstellarPath = "/media/downloads/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv";
    private const string RestartedError = "The app restarted before this scan finished, so nothing it found was stored. Scan the folders again.";

    public FolderScanPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Scan_statuses_and_the_folder_scanned_trigger_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Runs(db).Create(FolderScanStatus.Queued);
        await Runs(db).Create(FolderScanStatus.Running);
        await Runs(db).Create(FolderScanStatus.Succeeded, filesFound: 3, filesUnreported: 1);
        await Runs(db).Create(FolderScanStatus.Failed, error: RestartedError);
        await new FileInspectionRunDataFactory(db, TimeProvider.System).Create(FileInspectionRunStatus.Queued, FileInspectionTrigger.FolderScanned);

        //Act
        var statuses = await db.Database.SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM \"FolderScanRuns\"").ToListAsync(cancellationToken);
        var trigger = await db.Database.SqlQueryRaw<string>("SELECT \"Trigger\" AS \"Value\" FROM \"FileInspectionRuns\"").SingleAsync(cancellationToken);

        //Assert
        // By name, like every other enum, and what the one-run-per-active-status index filters on.
        Assert.Equal(["Failed", "Queued", "Running", "Succeeded"], statuses.Order(StringComparer.Ordinal));
        Assert.Equal("FolderScanned", trigger);
    }

    [Theory]
    [InlineData(FolderScanStatus.Queued)]
    [InlineData(FolderScanStatus.Running)]
    public async Task A_second_queued_or_running_scan_is_refused(FolderScanStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var runs = Runs(db);
        await runs.Create(status);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(status));

        //Assert
        // The backstop behind "join the waiting scan", for two clicks at once.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: FolderScanRuns.Status", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.FolderScanRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_queued_scan_waits_beside_a_running_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var runs = Runs(db);
        await runs.Create(FolderScanStatus.Running);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(FolderScanStatus.Queued));

        //Assert
        // The running one may have walked past a folder the owner just filled.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(
            [FolderScanStatus.Queued, FolderScanStatus.Running],
            (await check.FolderScanRuns.Select(run => run.Status).ToListAsync(cancellationToken)).Order());
    }

    [Fact]
    public async Task Finished_scans_never_block_a_new_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var runs = Runs(db);
        await runs.Create(FolderScanStatus.Succeeded, filesFound: 3, filesUnreported: 1);
        await runs.Create(FolderScanStatus.Succeeded, filesFound: 4, filesUnreported: 0);
        await runs.Create(FolderScanStatus.Failed, error: RestartedError);
        await runs.Create(FolderScanStatus.Failed, error: "The scan stopped because of an error in the app. Its log says more.");

        //Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await runs.Create(FolderScanStatus.Running);
            await runs.Create(FolderScanStatus.Queued);
        });

        //Assert
        // Unique over the active rows only: the history grows without limit.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(6, await check.FolderScanRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_the_scans_they_asked_for_with_their_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("vasyl", "P@ssword1");
        var run = await Runs(db).Create(FolderScanStatus.Succeeded, user.Id, user.UserName, filesFound: 3, filesUnreported: 1);

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: a scan is history; RequestedByUserName still says who asked for it.
        var stored = await db.FolderScanRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (run.Id, null, "vasyl", FolderScanStatus.Succeeded, 3, 1),
            (stored.Id, stored.RequestedByUserId, stored.RequestedByUserName, stored.Status, stored.FilesFound, stored.FilesUnreported));
    }

    [Fact]
    public async Task A_name_without_a_version_is_stored_and_kept_when_a_librarys_version_goes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var library = await CreateLibraryFileAsync(db, AliensPath, sourceConnectionId: null);
        var found = await Files(db).CreateUnreported(InterstellarPath);

        //Act
        await db.MediaVersions.Where(x => x.Id == library.MediaVersionId).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade still: a library's name goes with its version. The scan's name has none to go with.
        var stored = await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (found.Id, InterstellarPath, null, ProvenanceSourceKind.FileInspection, null),
            (stored.Id, stored.ServicePath, stored.MediaVersionId, stored.SourceKind, stored.SourceConnectionId));
    }

    [Fact]
    public async Task A_name_without_a_version_is_kept_as_it_is_when_an_integration_goes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integration = await new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System)
            .Create("Living room", "jellyfin", IntegrationKind.MediaLibrary);
        var library = await CreateLibraryFileAsync(db, AliensPath, integration.Id);
        var found = await Files(db).CreateUnreported(InterstellarPath);

        //Act
        await db.IntegrationConnections.Where(x => x.Id == integration.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // The scan's name was never the integration's: nothing about it changes.
        var stored = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        Assert.Equal(
            (InterstellarPath, null, null, found.CreatedUtc, found.ObservedUtc),
            (stored[found.Id].ServicePath, stored[found.Id].MediaVersionId, stored[found.Id].SourceConnectionId, stored[found.Id].CreatedUtc, stored[found.Id].ObservedUtc));
        // SetNull on the library's name, which keeps its version.
        Assert.Equal((library.MediaVersionId, null), (stored[library.Id].MediaVersionId, stored[library.Id].SourceConnectionId));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IFolderScanRunDataFactory Runs(ApplicationDbContext db) => new FolderScanRunDataFactory(db, TimeProvider.System);

    private static IPhysicalFileDataFactory Files(ApplicationDbContext db) => new PhysicalFileDataFactory(db, TimeProvider.System);

    // A name of a file of a new version of a new work, reported by the library sourceConnectionId when given.
    private static async Task<PhysicalFileEntity> CreateLibraryFileAsync(ApplicationDbContext db, string servicePath, Guid? sourceConnectionId)
    {
        var work = await new MediaWorkDataFactory(db, TimeProvider.System).Create();
        var version = await new MediaVersionDataFactory(db, TimeProvider.System).Create(work.Id, sourceItemId: null, sourceConnectionId: sourceConnectionId);
        return await Files(db).Create(version.Id, servicePath, sourceConnectionId: sourceConnectionId, sourceName: sourceConnectionId is null ? null : "Living room");
    }
}
