using System.Globalization;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-30 — the AddDownloadJobDestinationKind upgrade, the one migration of the ticket: it adds the kind of a job's
// destination to DownloadJobs in place, so no table is rebuilt (ADR 0009's warning 20410 doesn't arise), and every job
// sent before comes through whole with no kind — a category's, as every destination was then. The test migrates a fresh
// database to the migration before with the app's own migrator, writes a job the way that schema stores it (raw SQL:
// today's model has the column the old table hasn't), then upgrades it as the app's start does.
public sealed class AddDownloadJobDestinationKindMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261013070636_AddIgnoredUnreportedFiles";
    private const string ThisMigration = "20261014052258_AddDownloadJobDestinationKind";

    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public async Task Upgrade_keeps_every_job_whole_with_no_destination_kind()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        var sentUtc = new DateTime(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            // The release this upgrade leaves as it is: its factory writes it.
            var release = await new ReleaseDataFactory(db, Encryption, TimeProvider.System).Create(sourceConnectionId: null);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "DownloadJobs" ("Id", "RequestId", "ReleaseId", "DownloadClientName", "DestinationName", "ClientDestinationKey", "Status",
                    "StatusChangedUtc", "InfoHashV1", "PayloadKind", "CreatedUtc")
                VALUES ({0}, {1}, {2}, 'qBittorrent', 'Movies', 'Movies', 'Downloading', {3}, 'c9e15763f722f23e98a29decdfae341b98d53056', 'TorrentFile', {3})
                """,
                [Text(jobId), Text(Guid.NewGuid()), Text(release.Id), Text(sentUtc)],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var job = await check.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (jobId, "Movies", DownloadJobStatus.Downloading, (DateTime?)sentUtc, (ReleasePayloadKind?)ReleasePayloadKind.TorrentFile),
            (job.Id, job.ClientDestinationKey, job.Status, (DateTime?)job.StatusChangedUtc, job.PayloadKind));
        Assert.Null(job.DestinationKind);
    }

    [Fact]
    public void Upgrade_adds_the_one_nullable_column_and_rebuilds_no_table()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ThisMigration);

        //Assert
        // A rebuild is EF's copy into an "ef_temp_" table with foreign keys off between transactions (warning 20410,
        // ADR 0009); this upgrade adds one nullable column in place, and nothing else.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE ", script, StringComparison.Ordinal);
        Assert.Equal(
            ["ALTER TABLE \"DownloadJobs\" ADD \"DestinationKind\" TEXT NULL;"],
            script.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("ALTER TABLE", StringComparison.Ordinal)));
    }

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}
