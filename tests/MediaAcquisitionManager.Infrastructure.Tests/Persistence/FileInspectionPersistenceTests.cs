using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-9 — the file inspection's promises, pinned on the migrated schema: every new enum is stored by
// name; one set of bytes (identity, size and modification time, to the tick) is read once, and a change
// in any part of that key is a new reading; at most one inspection run waits and one runs, however two
// triggers race, while finished runs never block one; a reading's tracks go with it, and forgetting a
// reading never forgets a file name while forgetting a version never forgets a reading; a run outlives
// the user who asked for it; a media folder is allowed once; and an unsigned st_dev past long.MaxValue
// is stored bit for bit. Deletes go through ExecuteDelete, so what happens to the dependent rows is the
// database's own doing, not EF's tracking.
public sealed class FileInspectionPersistenceTests : DatabaseTestBase
{
    private const string AliensPath = "/media/movies/Alien. Collection/Aliens (1986).mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux.mkv";
    private const long Inode = 131_073;
    private const long SizeBytes = 3_891_719_630;

    public FileInspectionPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Inspection_statuses_track_kinds_dynamic_ranges_and_run_states_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var file = await CreateFileAsync(db, AliensPath);
        var reading = await Readings(db).Create(
            TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, [Track(MediaTrackKind.Video, DynamicRange.DolbyVision), Track(MediaTrackKind.Audio)]);
        await Files(db).Link(file.Id, reading.Id);
        await Runs(db).Create(FileInspectionRunStatus.Running, FileInspectionTrigger.LibrarySynced);

        //Act
        string[] stored =
        [
            await db.Database.SqlQueryRaw<string>("SELECT \"InspectionStatus\" AS \"Value\" FROM \"PhysicalFiles\"").SingleAsync(cancellationToken),
            .. await db.Database.SqlQueryRaw<string>("SELECT \"Kind\" AS \"Value\" FROM \"MediaInspectionTracks\" ORDER BY \"Position\"").ToListAsync(cancellationToken),
            await db.Database.SqlQueryRaw<string>("SELECT \"DynamicRange\" AS \"Value\" FROM \"MediaInspectionTracks\" WHERE \"DynamicRange\" IS NOT NULL").SingleAsync(cancellationToken),
            await db.Database.SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM \"FileInspectionRuns\"").SingleAsync(cancellationToken),
            await db.Database.SqlQueryRaw<string>("SELECT \"Trigger\" AS \"Value\" FROM \"FileInspectionRuns\"").SingleAsync(cancellationToken),
        ];
        var unknownRanges = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"MediaInspectionTracks\" WHERE \"DynamicRange\" IS NULL")
            .SingleAsync(cancellationToken);

        //Assert
        // By name, like every other enum, and what the one-run-per-active-status index filters on.
        Assert.Equal(["Inspected", "Video", "Audio", "DolbyVision", "Running", "LibrarySynced"], stored);
        // A dynamic range the stream didn't give stays unknown, never a guessed SDR.
        Assert.Equal(1, unknownRanges);
    }

    [Fact]
    public async Task A_second_reading_of_the_same_identity_size_and_modification_time_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Readings(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, [Track(MediaTrackKind.Video)]);

        //Act
        var exception = await Record.ExceptionAsync(() => Readings(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc));

        //Assert
        // The cache key of specification §3.3: one set of bytes is read once, however many names lead to it.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: MediaInspections.DeviceId, MediaInspections.Inode, MediaInspections.SizeBytes, MediaInspections.ModifiedUtc",
            sqlite.Message,
            StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.MediaInspections.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("DeviceId")]
    [InlineData("Inode")]
    [InlineData("SizeBytes")]
    [InlineData("ModifiedUtc")]
    public async Task A_reading_that_differs_in_one_part_of_the_key_is_another_reading(string part)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Readings(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc);
        // The smallest change of each part: the file on another device, another inode, one byte more, one
        // tick later.
        var (deviceId, inode, sizeBytes, modifiedUtc) = part switch
        {
            "DeviceId" => (TestMedia.DeviceId + 1, Inode, SizeBytes, TestMedia.ModifiedUtc),
            "Inode" => (TestMedia.DeviceId, Inode + 1, SizeBytes, TestMedia.ModifiedUtc),
            "SizeBytes" => (TestMedia.DeviceId, Inode, SizeBytes + 1, TestMedia.ModifiedUtc),
            _ => (TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc.AddTicks(1)),
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Readings(db).Create(deviceId, inode, sizeBytes, modifiedUtc));

        //Assert
        // A change in any of the four is a new key, and so a new read: how a changed file's cache entry
        // is invalidated.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(2, await check.MediaInspections.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData(FileInspectionRunStatus.Queued)]
    [InlineData(FileInspectionRunStatus.Running)]
    public async Task A_second_queued_or_running_inspection_run_is_refused(FileInspectionRunStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var runs = Runs(db);
        await runs.Create(status, FileInspectionTrigger.LibrarySynced);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(status, FileInspectionTrigger.Requested));

        //Assert
        // The backstop behind "join the waiting run", for a sync finishing as the owner clicks.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: FileInspectionRuns.Status", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.FileInspectionRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_queued_inspection_run_waits_beside_a_running_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var runs = Runs(db);
        await runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.AppStarted);

        //Act
        var exception = await Record.ExceptionAsync(() => runs.Create(FileInspectionRunStatus.Queued, FileInspectionTrigger.LibrarySynced));

        //Assert
        // A sync that finished during a run may have named files the running one listed before it.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(
            [FileInspectionRunStatus.Queued, FileInspectionRunStatus.Running],
            (await check.FileInspectionRuns.Select(run => run.Status).ToListAsync(cancellationToken)).Order());
    }

    [Fact]
    public async Task Finished_inspection_runs_never_block_a_new_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var runs = Runs(db);
        await runs.Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.AppStarted);
        await runs.Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.LibrarySynced);
        await runs.Create(FileInspectionRunStatus.Failed, FileInspectionTrigger.Requested, error: Message.Of("files.inspection.restarted"));
        await runs.Create(FileInspectionRunStatus.Failed, FileInspectionTrigger.LibrarySynced, error: Message.Of("files.inspection.app-error"));
        await runs.Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.Requested);

        //Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.AppStarted);
            await runs.Create(FileInspectionRunStatus.Queued, FileInspectionTrigger.LibrarySynced);
        });

        //Assert
        // Unique over the active rows only: the history grows without limit.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(7, await check.FileInspectionRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_reading_deletes_its_tracks_and_keeps_its_file_names_as_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var aliens = await CreateFileAsync(db, AliensPath);
        var taken = await CreateFileAsync(db, TakenPath);
        var forgotten = await Readings(db).Create(
            TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, [Track(MediaTrackKind.Video), Track(MediaTrackKind.Audio)]);
        var kept = await Readings(db).Create(
            TestMedia.DeviceId, Inode + 1, SizeBytes, TestMedia.ModifiedUtc, [Track(MediaTrackKind.Subtitle)]);
        await Files(db).Link(aliens.Id, forgotten.Id);
        await Files(db).Link(taken.Id, kept.Id);

        //Act
        await db.MediaInspections.Where(x => x.Id == forgotten.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a track is part of its reading, and goes with it.
        var tracks = await db.MediaInspectionTracks.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal((kept.Id, MediaTrackKind.Subtitle), Assert.Single(tracks.Select(x => (x.MediaInspectionId, x.Kind))));
        // No foreign key on purpose (ApplicationDbContext, ADR 0009): forgetting a reading never forgets a
        // file name, and the name's link stays as it was — only a run deletes readings, and only those no
        // name points at; a link to a missing reading reads as no facts (LibraryMapper).
        var files = await db.PhysicalFiles.AsNoTracking().OrderBy(x => x.ServicePath).ToListAsync(cancellationToken);
        Assert.Equal(
            [(aliens.Id, AliensPath, aliens.MediaVersionId, (Guid?)forgotten.Id), (taken.Id, TakenPath, taken.MediaVersionId, kept.Id)],
            files.Select(x => (x.Id, x.ServicePath, x.MediaVersionId, x.MediaInspectionId)));
    }

    [Fact]
    public async Task Deleting_a_version_deletes_its_file_names_but_keeps_their_readings()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var file = await CreateFileAsync(db, AliensPath);
        var reading = await Readings(db).Create(
            TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, [Track(MediaTrackKind.Video), Track(MediaTrackKind.Audio)]);
        await Files(db).Link(file.Id, reading.Id);

        //Act
        await db.MediaVersions.Where(x => x.Id == file.MediaVersionId).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // A reading is of bytes, not of a name: a name a sync re-creates finds it again without a second
        // read, and the next inspection forgets it once no name leads to it.
        Assert.Equal(0, await db.PhysicalFiles.CountAsync(cancellationToken));
        Assert.Equal(reading.Id, (await db.MediaInspections.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Equal(2, await db.MediaInspectionTracks.CountAsync(x => x.MediaInspectionId == reading.Id, cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_the_inspection_runs_they_asked_for_with_their_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("vasyl", "P@ssword1");
        var run = await Runs(db).Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.Requested, user.Id, user.UserName);

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: a run is history; RequestedByUserName still says who asked for it.
        var stored = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((run.Id, null, "vasyl", FileInspectionRunStatus.Succeeded), (stored.Id, stored.RequestedByUserId, stored.RequestedByUserName, stored.Status));
    }

    [Fact]
    public async Task A_media_folder_is_allowed_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var roots = new AllowedRootDataFactory(db);
        await roots.Create("/media", 0);

        //Act
        var exception = await Record.ExceptionAsync(() => roots.Create("/media", 1));

        //Assert
        // The backstop behind the rules' "listed twice", for two saves racing.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: AllowedRoots.Path", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.AllowedRoots.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Media_folders_that_differ_only_in_letter_case_are_two()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var roots = new AllowedRootDataFactory(db);
        await roots.Create("/media", 0);

        //Act
        var exception = await Record.ExceptionAsync(() => roots.Create("/Media", 1));

        //Assert
        // Case-sensitive: on Linux these are two folders. The rules, not the index, refuse the Windows
        // spelling of one folder twice.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(["/Media", "/media"], (await check.AllowedRoots.Select(x => x.Path).ToListAsync(cancellationToken)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_unsigned_device_id_past_long_MaxValue_round_trips_bit_for_bit()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // st_dev is unsigned; the top bit set makes it negative as a long.
        const ulong device = 0xFFFF_FFFF_FFFF_FF01UL;
        var deviceId = unchecked((long)device);
        await using (var db = CreateContext())
        {
            await Readings(db).Create(deviceId, Inode, SizeBytes, TestMedia.ModifiedUtc);
            var version = await new MediaVersionDataFactory(db, TimeProvider.System).Create((await new MediaWorkDataFactory(db, TimeProvider.System).Create()).Id, sourceItemId: null);
            await new PhysicalFileDataFactory(db, TimeProvider.System).Create(version.Id, AliensPath, SizeBytes, deviceId, Inode);
        }

        //Act
        await using var check = CreateContext();
        // The cache's own lookup: by the whole key, the modification time to the tick.
        var reading = await check.MediaInspections.AsNoTracking()
            .SingleAsync(
                x => x.DeviceId == deviceId && x.Inode == Inode && x.SizeBytes == SizeBytes && x.ModifiedUtc == TestMedia.ModifiedUtc,
                cancellationToken);
        var file = await check.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
        var stored = await check.Database.SqlQueryRaw<long>("SELECT \"DeviceId\" AS \"Value\" FROM \"MediaInspections\"").SingleAsync(cancellationToken);

        //Assert
        Assert.Equal(device, unchecked((ulong)reading.DeviceId));
        Assert.Equal(device, unchecked((ulong)file.DeviceId!.Value));
        Assert.Equal(TestMedia.ModifiedUtc.Ticks, reading.ModifiedUtc.Ticks);
        // An INTEGER, the long's own bits: equality is all the cache and Phase B ask of it.
        Assert.Equal(deviceId, stored);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IFileInspectionRunDataFactory Runs(ApplicationDbContext db) => new FileInspectionRunDataFactory(db, TimeProvider.System);

    private static IMediaInspectionDataFactory Readings(ApplicationDbContext db) => new MediaInspectionDataFactory(db, TimeProvider.System);

    private static IPhysicalFileDataFactory Files(ApplicationDbContext db) => new PhysicalFileDataFactory(db, TimeProvider.System);

    // A name of a file of a new version of a new work, read by the app itself.
    private static async Task<PhysicalFileEntity> CreateFileAsync(ApplicationDbContext db, string servicePath)
    {
        var work = await new MediaWorkDataFactory(db, TimeProvider.System).Create();
        var version = await new MediaVersionDataFactory(db, TimeProvider.System).Create(work.Id, sourceItemId: null);
        return await new PhysicalFileDataFactory(db, TimeProvider.System).Create(version.Id, servicePath);
    }

    private static MediaInspectionTrackEntity Track(MediaTrackKind kind, DynamicRange? dynamicRange = null) =>
        new()
        {
            Kind = kind,
            Codec = kind switch
            {
                MediaTrackKind.Video => "hevc",
                MediaTrackKind.Audio => "ac3",
                _ => "subrip",
            },
            DynamicRange = dynamicRange,
            Language = "ukr",
        };
}
