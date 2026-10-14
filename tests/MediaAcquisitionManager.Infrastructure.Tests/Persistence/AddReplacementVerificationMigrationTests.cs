using System.Globalization;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
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

// MAM-25 (ADR 0035) — the AddReplacementVerification upgrade on a database holding MAM-24's replacements: it only creates
// the evidence tables and the verifier's index and adds nullable columns, so no table is rebuilt (ADR 0009's warning 20410
// doesn't arise), and every replacement comes through whole, with no check started and no evidence yet — a Downloaded one
// is then checked by the first pass like any other. The test migrates a fresh database to the migration before with the
// app's own migrator, writes the rows the way that schema stores them (raw SQL: today's model has columns the old tables
// haven't), then upgrades it as the app's start does.
public sealed class AddReplacementVerificationMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261009093133_AddReplacementJobs";
    private const string ThisMigration = "20261010125322_AddReplacementVerification";

    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public async Task Upgrade_keeps_every_replacement_and_old_copy_whole_with_no_check_started()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var scheduledUtc = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            // The users and releases this upgrade leaves as they are: their factories write them. The download job by hand, in
            // the columns it had then (InsertDownloadJobAsync).
            var owner = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1");
            var release = await new ReleaseDataFactory(db, Encryption, TimeProvider.System).Create(sourceConnectionId: null);
            var download = await InsertDownloadJobAsync(db, release.Id, owner.Id, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobs" ("Id", "DownloadJobId", "RequestedByUserId", "RequestedByUserName", "State", "StateChangedUtc", "Reason",
                    "ReleaseEditions", "CreatedUtc")
                VALUES ({0}, {1}, {2}, 'owner', 'Downloaded', {3}, NULL, 'DirectorsCut', {3})
                """,
                [Text(jobId), Text(download), Text(owner.Id), Text(scheduledUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobFiles" ("Id", "ReplacementJobId", "Position", "PhysicalFileId", "MediaVersionId", "MediaWorkId", "MatchedWorkId",
                    "SeasonNumber", "EpisodeNumber", "ServicePath", "AppPath", "DeviceId", "Inode", "SizeBytes", "ModifiedUtc", "LinkCount", "EditionLabel")
                VALUES ({0}, {1}, 0, {2}, {3}, {4}, {5}, 2, 5, '/media/tv-shows/Penguins/S02E05.mkv', '/media/tv-shows/Penguins/S02E05.mkv', {6}, 42,
                    5000000000, {7}, 1, NULL)
                """,
                [Text(fileId), Text(jobId), Text(Guid.NewGuid()), Text(Guid.NewGuid()), Text(Guid.NewGuid()), Text(Guid.NewGuid()), TestMedia.DeviceId,
                    Text(TestMedia.ModifiedUtc)],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var job = await check.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (jobId, ReplacementJobState.Downloaded, "DirectorsCut", scheduledUtc, (DateTime?)null, (DateTime?)null),
            (job.Id, job.State, job.ReleaseEditions, job.StateChangedUtc, job.InspectionStartedUtc, job.LibraryAskedUtc));
        var file = await check.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (fileId, (int?)2, (int?)5, TestMedia.DeviceId, 42L, 5_000_000_000L, TestMedia.ModifiedUtc.Ticks),
            (file.Id, file.SeasonNumber, file.EpisodeNumber, file.DeviceId, file.Inode, file.SizeBytes, file.ModifiedUtc.Ticks));
        Assert.Equal(
            ((Guid?)null, (int?)null, (long?)null, (string?)null),
            (file.PairedNewFileId, file.EpisodeNumberEnd, file.DurationMilliseconds, file.Problem));
        Assert.Empty(await check.ReplacementJobNewFiles.ToListAsync(cancellationToken));
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
        // tables and the verifier's partial index.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobFiles\" ADD \"PairedNewFileId\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobs\" ADD \"LibraryAskedUtc\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE \"ReplacementJobNewFiles\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX \"IX_ReplacementJobs_Verifying\" ON \"ReplacementJobs\" (\"State\") WHERE \"State\" IN ('Downloaded', 'Inspecting');",
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
