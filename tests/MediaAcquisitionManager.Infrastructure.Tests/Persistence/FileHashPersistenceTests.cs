using System.Globalization;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-19 — the hash cache's promises, pinned on the migrated schema (ADR 0022): one set of bytes (identity, size
// and modification time, to the tick) is hashed once, the first hash standing, and a change in any part of that
// key is another hash — how a changed file's cached hash is invalidated; a name finds its hash in SQL by the facts
// the app last read on it, to the tick, as the inspection's forgetting does; an unsigned st_dev past long.MaxValue
// is stored bit for bit; and an inspection run's hashing columns say nothing was listed until its last step lists
// the candidates, then hold its counts and its first problem — a run finished before the upgrade included. The
// step itself (which files are candidates, the cache hits, the forgetting) is pinned over HTTP in Api.Tests; the
// read of the bytes in LocalFileInventoryHashTests.
public sealed class FileHashPersistenceTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261005081305_AddFolderScan";
    private const string AliensPath = "/media/movies/Alien. Collection/Aliens (1986).mkv";
    private const long Inode = 131_073;
    private const long SizeBytes = 3_891_719_630;

    /// <summary>Two digests of 64 lowercase hex characters, as sha256sum prints them.</summary>
    private const string Sha256 = "5994471abb01112afcc18159f6cc74b4f511b99806da59b3caf5a9c173cacfc5";
    private const string OtherSha256 = "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592";

    private const string HashProblem =
        "“/media/movies/Taken (2008)/Taken (2008) BDRemux.mkv” changed while the app was reading it for its hash, so the hash can't be said to belong to either version.";

    public FileHashPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task A_second_hash_of_the_same_identity_size_and_modification_time_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Hashes(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, Sha256);

        //Act
        var exception = await Record.ExceptionAsync(() => Hashes(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, OtherSha256));

        //Assert
        // The cache key of specification §3.3: one set of bytes is read whole once, however many names lead to it;
        // two inspections storing it at once can't keep two answers, and the first stands.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: FileHashes.DeviceId, FileHashes.Inode, FileHashes.SizeBytes, FileHashes.ModifiedUtc",
            sqlite.Message,
            StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(Sha256, (await check.FileHashes.AsNoTracking().SingleAsync(cancellationToken)).Sha256);
    }

    [Theory]
    [InlineData("DeviceId")]
    [InlineData("Inode")]
    [InlineData("SizeBytes")]
    [InlineData("ModifiedUtc")]
    public async Task A_hash_that_differs_in_one_part_of_the_key_is_another_row(string part)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Hashes(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, Sha256);
        // The smallest change of each part: the file on another device, another inode, one byte more, one tick
        // later.
        var (deviceId, inode, sizeBytes, modifiedUtc) = part switch
        {
            "DeviceId" => (TestMedia.DeviceId + 1, Inode, SizeBytes, TestMedia.ModifiedUtc),
            "Inode" => (TestMedia.DeviceId, Inode + 1, SizeBytes, TestMedia.ModifiedUtc),
            "SizeBytes" => (TestMedia.DeviceId, Inode, SizeBytes + 1, TestMedia.ModifiedUtc),
            _ => (TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc.AddTicks(1)),
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Hashes(db).Create(deviceId, inode, sizeBytes, modifiedUtc, OtherSha256));

        //Assert
        // A change in any of the four is a new key, and so a new read: how a changed file's cached hash is
        // invalidated. The old row stays until an inspection forgets it.
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(
            [Sha256, OtherSha256],
            (await check.FileHashes.Select(hash => hash.Sha256).ToListAsync(cancellationToken)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_hash_round_trips_its_key_to_the_tick_and_its_digest_and_time_as_stored()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Both instants carry ticks below a millisecond, which a lossy column would drop.
        var hashedUtc = new DateTime(2026, 10, 5, 11, 8, 56, DateTimeKind.Utc).AddTicks(7_654_321);
        await using (var db = CreateContext())
            await Hashes(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, Sha256, hashedUtc);

        //Act
        await using var check = CreateContext();
        var stored = await check.FileHashes.AsNoTracking().SingleAsync(cancellationToken);
        // The cache's own lookup: by the whole key, the modification time to the tick.
        var found = await IsCachedAsync(check, TestMedia.ModifiedUtc, cancellationToken);
        var oneTickEarlier = await IsCachedAsync(check, TestMedia.ModifiedUtc.AddTicks(-1), cancellationToken);

        //Assert
        Assert.Equal(
            (TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc.Ticks, Sha256, hashedUtc.Ticks),
            (stored.DeviceId, stored.Inode, stored.SizeBytes, stored.ModifiedUtc.Ticks, stored.Sha256, stored.CreatedUtc.Ticks));
        Assert.True(found);
        // A file written again within the same millisecond is another version: its old hash must not answer.
        Assert.False(oneTickEarlier);
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
            await Hashes(db).Create(deviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, Sha256);

        //Act
        await using var check = CreateContext();
        var hash = await check.FileHashes.AsNoTracking()
            .SingleAsync(
                x => x.DeviceId == deviceId && x.Inode == Inode && x.SizeBytes == SizeBytes && x.ModifiedUtc == TestMedia.ModifiedUtc,
                cancellationToken);
        var stored = await check.Database.SqlQueryRaw<long>("SELECT \"DeviceId\" AS \"Value\" FROM \"FileHashes\"").SingleAsync(cancellationToken);

        //Assert
        Assert.Equal(device, unchecked((ulong)hash.DeviceId));
        // An INTEGER, the long's own bits, as on the file names: equality is all the cache asks of it.
        Assert.Equal(deviceId, stored);
    }

    [Fact]
    public async Task A_names_hash_is_the_row_its_read_facts_name_to_the_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var work = await new MediaWorkDataFactory(db, TimeProvider.System).Create();
        var version = await new MediaVersionDataFactory(db, TimeProvider.System).Create(work.Id, sourceItemId: null);
        var file = await new PhysicalFileDataFactory(db, TimeProvider.System).Create(version.Id, AliensPath, SizeBytes, TestMedia.DeviceId, Inode);
        // What an inspection writes on the name when it looks at the file: the size and time it read itself.
        await db.PhysicalFiles
            .Where(x => x.Id == file.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ReadSizeBytes, SizeBytes).SetProperty(x => x.ReadModifiedUtc, TestMedia.ModifiedUtc),
                cancellationToken);
        // The file's hash, and one of the version before it, written a tick earlier.
        await Hashes(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc, Sha256);
        await Hashes(db).Create(TestMedia.DeviceId, Inode, SizeBytes, TestMedia.ModifiedUtc.AddTicks(-1), OtherSha256);

        //Act
        // No foreign key: the inspection matches a name to its hash by the key both carry, in SQL, as it does
        // when it forgets the hashes no name matches.
        var matched = await db.FileHashes
            .Where(hash => db.PhysicalFiles.Any(name => name.DeviceId == hash.DeviceId
                && name.Inode == hash.Inode
                && name.ReadSizeBytes == hash.SizeBytes
                && name.ReadModifiedUtc == hash.ModifiedUtc))
            .Select(hash => hash.Sha256)
            .ToListAsync(cancellationToken);

        //Assert
        // The two columns store an instant the same way, so they compare equal in SQL to the tick, and only then.
        Assert.Equal([Sha256], matched);
    }

    [Fact]
    public async Task An_inspection_run_lists_no_hash_candidates_until_its_last_step_does()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
            await Runs(db).Create(FileInspectionRunStatus.Running, FileInspectionTrigger.LibrarySynced, filesTotal: 2, filesChecked: 1);

        //Act
        await using var check = CreateContext();
        var run = await check.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);

        //Assert
        // Null, not zero: "not listed yet" is not "none needed".
        Assert.Equal((null, 0, 0, 0, null), Hashing(run));
    }

    [Fact]
    public async Task An_inspection_runs_hashing_counts_and_first_problem_are_stored_as_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
        await using (var db = CreateContext())
        {
            var planted = Runs(db);
            // The reference installation: no two files share a size, so none needs a hash.
            await planted.Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.AppStarted, createdUtc: started, hashing: new PlantedHashing(0));
            // Three files to hash: one answered from the cache, one read, one that changed while it was read.
            await planted.Create(
                FileInspectionRunStatus.Succeeded,
                FileInspectionTrigger.Requested,
                createdUtc: started.AddMinutes(1),
                hashing: new PlantedHashing(
                    3, Checked: 3, Read: 1, ProblemCount: 1, Problem: FileInventoryMessages.ChangedWhileHashing("/media/movies/Taken (2008)/Taken (2008) BDRemux.mkv")));
        }

        //Act
        await using var check = CreateContext();
        var runs = await check.FileInspectionRuns.AsNoTracking().OrderBy(run => run.CreatedUtc).ToListAsync(cancellationToken);

        //Assert
        // Zero listed is an answer of its own: the step ran and found nothing that needs a hash.
        (int? Candidates, int Checked, int Read, int ProblemCount, string? Problem)[] expected =
        [
            (0, 0, 0, 0, null),
            (3, 3, 1, 1, HashProblem),
        ];
        Assert.Equal(expected, runs.Select(Hashing));
    }

    [Fact]
    public async Task An_inspection_run_finished_before_the_upgrade_lists_no_hash_candidates_and_counts_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid();
        // How EF Core stores an instant in SQLite: text to the tick.
        var at = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
        // A fresh database instead of the migrated copy, so the upgrade runs on a row the schema before it stored.
        TestDatabase.DeleteDatabase(DatabasePath);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "FileInspectionRuns" ("Id", "Trigger", "Status", "StartedUtc", "FinishedUtc", "FilesTotal", "FilesChecked", "FilesProbed", "CreatedUtc")
                VALUES ({0}, 'AppStarted', 'Succeeded', {1}, {1}, 2, 2, 2, {1})
                """,
                [runId.ToString().ToUpperInvariant(), at],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        // Added columns, no table rebuilt: the run is kept as it was, and says it never listed any candidates —
        // the page shows no hashing for it rather than a step that found nothing.
        await using var check = CreateContext();
        var run = await check.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((runId, FileInspectionRunStatus.Succeeded, 2, 2, 2), (run.Id, run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal((null, 0, 0, 0, null), Hashing(run));
        Assert.Equal(0, await check.FileHashes.CountAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IFileHashDataFactory Hashes(ApplicationDbContext db) => new FileHashDataFactory(db, TimeProvider.System);

    private static IFileInspectionRunDataFactory Runs(ApplicationDbContext db) => new FileInspectionRunDataFactory(db, TimeProvider.System);

    private static (int? Candidates, int Checked, int Read, int ProblemCount, string? Problem) Hashing(FileInspectionRunEntity run) =>
        (run.HashCandidates, run.HashCandidatesChecked, run.HashCandidatesRead, run.HashProblemCount, TestTexts.En(run.HashProblem));

    /// <summary>Whether a hash is stored under the test file's key with <paramref name="modifiedUtc"/>, asked as the
    /// inspection asks before it reads a file.</summary>
    private static Task<bool> IsCachedAsync(ApplicationDbContext db, DateTime modifiedUtc, CancellationToken cancellationToken) =>
        db.FileHashes.AnyAsync(
            stored => stored.DeviceId == TestMedia.DeviceId
                && stored.Inode == Inode
                && stored.SizeBytes == SizeBytes
                && stored.ModifiedUtc == modifiedUtc,
            cancellationToken);
}
