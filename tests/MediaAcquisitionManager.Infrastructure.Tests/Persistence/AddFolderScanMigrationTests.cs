using System.Globalization;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-18 — the AddFolderScan upgrade on a database that holds a library: SQLite changes a column only by
// rebuilding its table, so making PhysicalFiles.MediaVersionId nullable rebuilds the table of file names
// (EF warning 20410), and the owner's names must come through it whole — every value of every row, its
// version above all — with the table's indexes and foreign keys as they were. Afterwards a name without a
// version, which the scan stores, can be stored; before it, it couldn't. Each test migrates a fresh database
// to the migration before with the app's own migrator, writes rows the way that schema stores them (raw
// SQL: today's model has columns the old table hasn't), then upgrades it as the app's start does.
public sealed class AddFolderScanMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261004175900_AddSeriesAndTvReleases";
    private const string ThisMigration = "20261005081305_AddFolderScan";
    private const string AliensPath = "/media/movies/Чужі. Колекція/[02.1] Aliens (1986) [Theatrical Cut].mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/S01E01.mkv";
    private const string AliensError = "ffprobe couldn't read “/media/movies/Чужі. Колекція/[02.1] Aliens (1986) [Theatrical Cut].mkv” as media.";

    private static readonly Guid IntegrationId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly Guid WorkId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();
    private static readonly Guid ReadingId = Guid.NewGuid();

    /// <summary>A name with every column set: reported by a library, in one of its libraries, looked at and
    /// linked to a reading. Its device id is an unsigned st_dev past long.MaxValue, its times carry ticks.</summary>
    private static readonly FileRow Aliens = new(
        Guid.NewGuid(),
        VersionId,
        AliensPath,
        LibrarySearchText.Normalize(AliensPath),
        LocationId,
        3_891_719_630,
        unchecked((long)0xFFFF_FFFF_FFFF_FF01UL),
        131_073,
        2,
        FileInspectionStatus.MediaUnreadable,
        AliensError,
        new DateTime(2026, 10, 4, 18, 0, 0, DateTimeKind.Utc).AddTicks(7_654_321),
        ReadingId,
        ProvenanceSourceKind.MediaLibrary,
        IntegrationId,
        "Living room",
        "00a49b2db2200514168431dc028f0a0b",
        new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567),
        new DateTime(2026, 10, 4, 17, 59, 0, DateTimeKind.Utc));

    /// <summary>A name with every optional column empty: read by the app itself, never looked at.</summary>
    private static readonly FileRow Continental = new(
        Guid.NewGuid(),
        VersionId,
        ContinentalPath,
        LibrarySearchText.Normalize(ContinentalPath),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        ProvenanceSourceKind.FileInspection,
        null,
        null,
        null,
        new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Upgrading_keeps_every_value_of_every_file_name_its_version_included()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await SeedLibraryAsync(db, cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        Assert.Contains(ThisMigration, await check.Database.GetAppliedMigrationsAsync(cancellationToken));
        var files = await check.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.Id, cancellationToken);
        Assert.Equal(2, files.Count);
        // Every column as it was, and the new ones empty: nothing has been read since.
        Assert.Equal(Aliens, FileRow.Of(files[Aliens.Id]));
        Assert.Equal(Continental, FileRow.Of(files[Continental.Id]));
        Assert.All(files.Values, file => Assert.Equal((null, null), (file.ReadSizeBytes, file.ReadModifiedUtc)));
        // The ticks too, which a lossy copy would drop.
        Assert.Equal(Aliens.InspectedUtc!.Value.Ticks, files[Aliens.Id].InspectedUtc!.Value.Ticks);
        Assert.Equal(Aliens.CreatedUtc.Ticks, files[Aliens.Id].CreatedUtc.Ticks);
        // What the names lead to is untouched, and the scans start with none.
        Assert.Equal(ReadingId, (await check.MediaInspections.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Equal(VersionId, (await check.MediaVersions.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Equal(0, await check.FolderScanRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Upgrading_rebuilds_the_file_names_table_with_its_indexes_and_foreign_keys()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await SeedLibraryAsync(db, cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var indexes = await check.Database
            .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM pragma_index_list('PhysicalFiles')")
            .ToListAsync(cancellationToken);
        Assert.Superset(
            new HashSet<string>
            {
                "IX_PhysicalFiles_DeviceId_Inode",
                "IX_PhysicalFiles_LibraryLocationId",
                "IX_PhysicalFiles_MediaInspectionId",
                "IX_PhysicalFiles_MediaVersionId",
                "IX_PhysicalFiles_SourceConnectionId_SourceItemId",
            },
            indexes.ToHashSet());
        var foreignKeys = await check.Database
            .SqlQueryRaw<string>("SELECT \"table\" || '.' || \"from\" || ' ' || \"on_delete\" AS \"Value\" FROM pragma_foreign_key_list('PhysicalFiles')")
            .ToListAsync(cancellationToken);
        // A library's name still goes with its version; a library or integration that goes leaves its names.
        Assert.Equal(
            ["IntegrationConnections.SourceConnectionId SET NULL", "LibraryLocations.LibraryLocationId SET NULL", "MediaVersions.MediaVersionId CASCADE"],
            foreignKeys.Order(StringComparer.Ordinal));
        // Every row still points at a row that exists: the rebuild ran with the checks off.
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_foreign_key_check").SingleAsync(cancellationToken));
        await check.MediaVersions.Where(version => version.Id == VersionId).ExecuteDeleteAsync(cancellationToken);
        Assert.Equal(0, await check.PhysicalFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_name_without_a_version_is_refused_before_the_upgrade_and_stored_after_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
        var found = new PhysicalFileEntity
        {
            SourceKind = ProvenanceSourceKind.FileInspection,
            ServicePath = ContinentalPath,
            NormalizedServicePath = LibrarySearchText.Normalize(ContinentalPath),
            CreatedUtc = Continental.CreatedUtc,
            ObservedUtc = Continental.ObservedUtc,
        };

        //Act
        Exception? before;
        await using (var db = CreateContext())
        {
            before = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "PhysicalFiles" ("Id", "MediaVersionId", "ServicePath", "NormalizedServicePath", "SourceKind", "CreatedUtc", "ObservedUtc")
                VALUES ({0}, NULL, {1}, {2}, 'FileInspection', {3}, {3})
                """,
                [Text(found.Id), found.ServicePath, found.NormalizedServicePath, Text(found.CreatedUtc)],
                cancellationToken));
            await db.Database.MigrateAsync(cancellationToken);
        }

        Exception? after;
        await using (var db = CreateContext())
        {
            db.PhysicalFiles.Add(found);
            after = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));
        }

        //Assert
        // The scan's names have no version: the column refused them before (ADR 0021).
        var sqlite = Assert.IsType<SqliteException>(before);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("NOT NULL constraint failed: PhysicalFiles.MediaVersionId", sqlite.Message, StringComparison.Ordinal);
        Assert.Null(after);
        await using var check = CreateContext();
        var stored = await check.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((found.Id, null, ContinentalPath), (stored.Id, stored.MediaVersionId, stored.ServicePath));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A library as the schema before AddFolderScan stores one: an integration and one of its libraries, a
    // work with one version, a reading, and the two names.
    private static async Task SeedLibraryAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var at = Text(Aliens.CreatedUtc);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "IntegrationConnections" ("Id", "AdapterType", "BaseUrl", "CreatedUtc", "IsEnabled", "Kind", "Name", "NormalizedName")
            VALUES ({0}, 'jellyfin', 'http://127.0.0.1:9', {1}, 1, 'MediaLibrary', 'Living room', 'LIVING ROOM');
            INSERT INTO "LibraryLocations" ("Id", "CreatedUtc", "Name", "ObservedUtc", "SourceConnectionId", "SourceItemId", "SourceKind", "SourceName")
            VALUES ({2}, {1}, 'Фільми', {1}, {0}, '78ee018b5034640ba414833f0fe9d6b3', 'MediaLibrary', 'Living room');
            INSERT INTO "MediaWorks" ("Id", "CreatedUtc", "Kind") VALUES ({3}, {1}, 'Movie');
            INSERT INTO "MediaVersions" ("Id", "CreatedUtc", "EditionLabel", "MediaWorkId", "ObservedUtc", "SourceConnectionId", "SourceItemId", "SourceKind", "SourceName", "SourceVersionId")
            VALUES ({4}, {1}, 'Theatrical Cut', {3}, {1}, {0}, '00a49b2db2200514168431dc028f0a0b', 'MediaLibrary', 'Living room', '00a49b2db2200514168431dc028f0a0b');
            INSERT INTO "MediaInspections" ("Id", "BitRate", "CreatedUtc", "DeviceId", "DurationMilliseconds", "Error", "Inode", "ModifiedUtc", "SizeBytes")
            VALUES ({5}, NULL, {1}, {6}, NULL, {7}, {8}, {9}, {10});
            """,
            [
                Text(IntegrationId), at, Text(LocationId), Text(WorkId), Text(VersionId), Text(ReadingId),
                Aliens.DeviceId!.Value, AliensError, Aliens.Inode!.Value, Text(Aliens.InspectedUtc!.Value), Aliens.SizeBytes!.Value,
            ],
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "PhysicalFiles" ("Id", "MediaVersionId", "ServicePath", "NormalizedServicePath", "LibraryLocationId", "SizeBytes", "DeviceId", "Inode",
                "LinkCount", "InspectionStatus", "InspectionError", "InspectedUtc", "MediaInspectionId", "SourceKind", "SourceConnectionId", "SourceName",
                "SourceItemId", "CreatedUtc", "ObservedUtc")
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17}, {18});
            INSERT INTO "PhysicalFiles" ("Id", "MediaVersionId", "ServicePath", "NormalizedServicePath", "SourceKind", "CreatedUtc", "ObservedUtc")
            VALUES ({19}, {1}, {20}, {21}, {22}, {23}, {24});
            """,
            [
                Text(Aliens.Id), Text(Aliens.MediaVersionId!.Value), Aliens.ServicePath, Aliens.NormalizedServicePath, Text(Aliens.LibraryLocationId!.Value),
                Aliens.SizeBytes!.Value, Aliens.DeviceId!.Value, Aliens.Inode!.Value, Aliens.LinkCount!.Value, Aliens.InspectionStatus!.Value.ToString(),
                Aliens.InspectionError!, Text(Aliens.InspectedUtc!.Value), Text(Aliens.MediaInspectionId!.Value), Aliens.SourceKind.ToString(),
                Text(Aliens.SourceConnectionId!.Value), Aliens.SourceName!, Aliens.SourceItemId!, Text(Aliens.CreatedUtc), Text(Aliens.ObservedUtc),
                Text(Continental.Id), Continental.ServicePath, Continental.NormalizedServicePath, Continental.SourceKind.ToString(),
                Text(Continental.CreatedUtc), Text(Continental.ObservedUtc),
            ],
            cancellationToken);
    }

    // How EF Core stores a Guid and an instant in SQLite: upper-case text, and text to the tick.
    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

    /// <summary>Every column a file name had before AddFolderScan, so one comparison covers them all.</summary>
    private sealed record FileRow(
        Guid Id,
        Guid? MediaVersionId,
        string ServicePath,
        string NormalizedServicePath,
        Guid? LibraryLocationId,
        long? SizeBytes,
        long? DeviceId,
        long? Inode,
        int? LinkCount,
        FileInspectionStatus? InspectionStatus,
        string? InspectionError,
        DateTime? InspectedUtc,
        Guid? MediaInspectionId,
        ProvenanceSourceKind SourceKind,
        Guid? SourceConnectionId,
        string? SourceName,
        string? SourceItemId,
        DateTime CreatedUtc,
        DateTime ObservedUtc)
    {
        public static FileRow Of(PhysicalFileEntity file) =>
            new(
                file.Id,
                file.MediaVersionId,
                file.ServicePath,
                file.NormalizedServicePath,
                file.LibraryLocationId,
                file.SizeBytes,
                file.DeviceId,
                file.Inode,
                file.LinkCount,
                file.InspectionStatus,
                // MAM-47 — English an older build stored reads back as its words (the column's Tolerant Reader).
                TestTexts.En(file.InspectionError),
                file.InspectedUtc,
                file.MediaInspectionId,
                file.SourceKind,
                file.SourceConnectionId,
                file.SourceName,
                file.SourceItemId,
                file.CreatedUtc,
                file.ObservedUtc);
    }
}
