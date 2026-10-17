using System.Globalization;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-26 (ADR 0030) — the AddReplacementQuarantine upgrade on a database holding MAM-25's verified replacements: it only
// creates the names' table and the mover's index and adds nullable columns, so no table is rebuilt (ADR 0009's warning
// 20410 doesn't arise), and every replacement comes through whole, with nothing quarantined, no stop of the mover's and no
// notice owed — a Verified one is then taken up by the mover's first pass like any other. The test migrates a fresh
// database to the migration before with the app's own migrator, writes the rows the way that schema stores them (raw
// SQL: today's model has columns the old tables haven't), then upgrades it as the app's start does.
public sealed class AddReplacementQuarantineMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261010125322_AddReplacementVerification";
    private const string ThisMigration = "20261010174323_AddReplacementQuarantine";

    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public async Task Upgrade_keeps_every_verified_replacement_and_its_evidence_whole_with_nothing_quarantined()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var newFileId = Guid.NewGuid();
        var scheduledUtc = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var verifiedUtc = scheduledUtc.AddMinutes(5).AddTicks(1);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            // The users and releases this upgrade leaves as they are: their factories write them. The download job by hand, in
            // the columns it had then (InsertDownloadJobAsync).
            // MAM-46 — raw SQL: today's user has a Language column the old Users table hasn't.
            var ownerId = await MigrationUserRows.InsertAsync(db, "owner", cancellationToken);
            var release = await new ReleaseDataFactory(db, Encryption, TimeProvider.System).Create(sourceConnectionId: null);
            var download = await InsertDownloadJobAsync(db, release.Id, ownerId, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobs" ("Id", "DownloadJobId", "RequestedByUserId", "RequestedByUserName", "State", "StateChangedUtc", "Reason",
                    "ReleaseEditions", "CreatedUtc", "InspectionStartedUtc", "LibraryAskedUtc")
                VALUES ({0}, {1}, {2}, 'owner', 'Verified', {3}, NULL, 'DirectorsCut', {4}, {4}, {4})
                """,
                [Text(jobId), Text(download), Text(ownerId), Text(verifiedUtc), Text(scheduledUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobFiles" ("Id", "ReplacementJobId", "Position", "PhysicalFileId", "MediaVersionId", "MediaWorkId", "MatchedWorkId",
                    "SeasonNumber", "EpisodeNumber", "ServicePath", "AppPath", "DeviceId", "Inode", "SizeBytes", "ModifiedUtc", "LinkCount", "EditionLabel",
                    "PairedNewFileId", "EpisodeNumberEnd", "DurationMilliseconds", "Problem")
                VALUES ({0}, {1}, 0, {2}, {3}, {4}, {4}, NULL, NULL, '/data/movies/Alien (1979)/Alien.mkv', '/media/movies/Alien (1979)/Alien.mkv',
                    {5}, 42, 5000000000, {6}, 1, 'Theatrical Cut', {7}, NULL, 5604672, NULL)
                """,
                [Text(fileId), Text(jobId), Text(Guid.NewGuid()), Text(Guid.NewGuid()), Text(Guid.NewGuid()), TestMedia.DeviceId,
                    Text(TestMedia.ModifiedUtc), Text(newFileId)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobNewFiles" ("Id", "ReplacementJobId", "Position", "RelativePath", "AppPath", "Refreshed")
                VALUES ({0}, {1}, 0, 'Alien.1979.Directors.Cut.2160p.mkv', '/media/movies/Alien.1979.Directors.Cut.2160p.mkv', 1)
                """,
                [Text(newFileId), Text(jobId)],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var job = await check.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (jobId, ReplacementJobState.Verified, "DirectorsCut", verifiedUtc, (DateTime?)scheduledUtc, (DateTime?)scheduledUtc),
            (job.Id, job.State, job.ReleaseEditions, job.StateChangedUtc, job.InspectionStartedUtc, job.LibraryAskedUtc));
        // Not checked by the mover, not quarantined, no notice owed: the mover's first pass takes it up as it is.
        Assert.Equal(
            ((DateTime?)null, (DateTime?)null, (DateTime?)null),
            (job.QuarantineCheckedUtc, job.QuarantinedUtc, job.LibraryNoticeDueUtc));
        var file = await check.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (fileId, TestMedia.DeviceId, 42L, 5_000_000_000L, TestMedia.ModifiedUtc.Ticks, (Guid?)newFileId, (long?)5_604_672),
            (file.Id, file.DeviceId, file.Inode, file.SizeBytes, file.ModifiedUtc.Ticks, file.PairedNewFileId, file.DurationMilliseconds));
        Assert.Equal(newFileId, (await check.ReplacementJobNewFiles.AsNoTracking().SingleAsync(cancellationToken)).Id);
        Assert.Empty(await check.ReplacementJobNames.ToListAsync(cancellationToken));
    }

    [Fact]
    public void Upgrade_rebuilds_no_table()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ThisMigration);

        //Assert
        // A rebuild is EF's copy into an "ef_temp_" table with foreign keys off between transactions (warning 20410,
        // ADR 0009); this upgrade adds nullable columns in place — with no key, which would rebuild — and creates its
        // table and the mover's partial index, leaving the following and the verifying indexes as they are.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobs\" ADD \"QuarantineCheckedUtc\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobs\" ADD \"QuarantinedUtc\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobs\" ADD \"LibraryNoticeDueUtc\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE \"ReplacementJobNames\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE UNIQUE INDEX \"IX_ReplacementJobNames_ReplacementJobId_Position\" ON \"ReplacementJobNames\" (\"ReplacementJobId\", \"Position\");",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX \"IX_ReplacementJobs_Quarantine\" ON \"ReplacementJobs\" (\"State\") WHERE \"State\" IN ('Verified', 'Quarantining');",
            script,
            StringComparison.Ordinal);
    }

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

    /// <summary>
    /// MAM-30 — a download job written by hand in the columns it had before this upgrade: its factory writes today's model,
    /// whose later columns (DestinationKind) this schema hasn't got yet. Its id.
    /// </summary>
    private static async Task<Guid> InsertDownloadJobAsync(ApplicationDbContext db, Guid releaseId, Guid ownerId, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var sentUtc = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "DownloadJobs" ("Id", "RequestId", "RequestedByUserId", "RequestedByUserName", "ReleaseId", "DownloadClientName", "Status",
                "StatusChangedUtc", "CreatedUtc")
            VALUES ({0}, {1}, {2}, 'owner', {3}, 'qBittorrent', 'Submitted', {4}, {4})
            """,
            [Text(id), Text(Guid.NewGuid()), Text(ownerId), Text(releaseId), Text(sentUtc)],
            cancellationToken);
        return id;
    }
}
