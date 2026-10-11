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

// MAM-27 — the AddReplacementCopyDrops upgrade on a database holding MAM-26's replacements: it only adds two columns to the
// ticked copies in place — when the owner dropped one, and whether its problem is one only a drop settles, NOT NULL with a
// default —, so no table is rebuilt (ADR 0009's warning 20410 doesn't arise), and every copy comes through whole, not
// dropped and as one a Retry may settle: what each was before the upgrade. The test migrates a fresh database to the
// migration before with the app's own migrator, writes the rows the way that schema stores them (raw SQL: today's model has
// columns the old table hasn't), then upgrades it as the app's start does.
public sealed class AddReplacementCopyDropsMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261010174323_AddReplacementQuarantine";
    private const string ThisMigration = "20261011090512_AddReplacementCopyDrops";
    private const string HeldReason =
        "Still held by the torrent “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there without its files, then press Retry.";

    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public async Task Upgrade_keeps_every_ticked_copy_whole_not_dropped_and_retryable()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        var heldId = Guid.NewGuid();
        var pairedId = Guid.NewGuid();
        var newFileId = Guid.NewGuid();
        var nameId = Guid.NewGuid();
        var scheduledUtc = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var checkedUtc = scheduledUtc.AddMinutes(70).AddTicks(1);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            // The users, releases and download jobs this upgrade leaves as they are: their factories write them.
            var owner = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1");
            var release = await new ReleaseDataFactory(db, Encryption, TimeProvider.System).Create(sourceConnectionId: null);
            var download = await new DownloadJobDataFactory(db, TimeProvider.System).Create(release.Id, null, "qBittorrent", requestedByUserId: owner.Id);
            // A review the mover stopped: one copy a torrent holds, the other paired and planned to move.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobs" ("Id", "DownloadJobId", "RequestedByUserId", "RequestedByUserName", "State", "StateChangedUtc", "Reason",
                    "ReleaseEditions", "CreatedUtc", "InspectionStartedUtc", "LibraryAskedUtc", "QuarantineCheckedUtc", "QuarantinedUtc", "LibraryNoticeDueUtc")
                VALUES ({0}, {1}, {2}, 'owner', 'NeedsReview', {3}, {4}, 'DirectorsCut', {5}, {5}, {5}, {3}, NULL, NULL)
                """,
                [Text(jobId), Text(download.Id), Text(owner.Id), Text(checkedUtc), HeldReason, Text(scheduledUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobFiles" ("Id", "ReplacementJobId", "Position", "PhysicalFileId", "MediaVersionId", "MediaWorkId", "MatchedWorkId",
                    "SeasonNumber", "EpisodeNumber", "ServicePath", "AppPath", "DeviceId", "Inode", "SizeBytes", "ModifiedUtc", "LinkCount", "EditionLabel",
                    "PairedNewFileId", "EpisodeNumberEnd", "DurationMilliseconds", "Problem")
                VALUES ({0}, {1}, 0, {2}, {3}, {4}, {4}, NULL, NULL, '/data/movies/Alien (1979)/Alien.mkv', '/media/movies/Alien (1979)/Alien.mkv',
                    {5}, 42, 5000000000, {6}, 1, 'Theatrical Cut', {7}, NULL, 5604672, {8}),
                    ({9}, {1}, 1, {2}, {3}, {4}, {4}, NULL, NULL, '/data/movies/Alien (1979)/Alien 4K.mkv', '/media/movies/Alien (1979)/Alien 4K.mkv',
                    {5}, 43, 7000000000, {6}, 1, NULL, {7}, NULL, NULL, NULL)
                """,
                [Text(heldId), Text(jobId), Text(Guid.NewGuid()), Text(Guid.NewGuid()), Text(Guid.NewGuid()), TestMedia.DeviceId,
                    Text(TestMedia.ModifiedUtc), Text(newFileId), HeldReason, Text(pairedId)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobNewFiles" ("Id", "ReplacementJobId", "Position", "RelativePath", "AppPath", "Refreshed")
                VALUES ({0}, {1}, 0, 'Alien.1979.Directors.Cut.2160p.mkv', '/media/movies/Alien.1979.Directors.Cut.2160p.mkv', 1)
                """,
                [Text(newFileId), Text(jobId)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobNames" ("Id", "ReplacementJobId", "Position", "ReplacementJobFileId", "Kind", "AppPath", "QuarantinePath",
                    "AllowedRoot", "DeviceId", "Inode", "SizeBytes", "ModifiedUtc", "LinkCount", "State", "Problem", "MovedUtc", "RestoredUtc", "DeletedUtc")
                VALUES ({0}, {1}, 0, {2}, 'Video', '/media/movies/Alien (1979)/Alien 4K.mkv', {3}, '/media', {4}, 43, 7000000000, {5}, 1, 'Planned',
                    NULL, NULL, NULL, NULL)
                """,
                [Text(nameId), Text(jobId), Text(pairedId), $"/media/.mam-quarantine/{jobId}/movies/Alien (1979)/Alien 4K.mkv", TestMedia.DeviceId,
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
            (jobId, ReplacementJobState.NeedsReview, (string?)HeldReason, checkedUtc, (DateTime?)checkedUtc),
            (job.Id, job.State, job.Reason, job.StateChangedUtc, job.QuarantineCheckedUtc));
        var copies = await check.ReplacementJobFiles.AsNoTracking().OrderBy(copy => copy.Position).ToListAsync(cancellationToken);
        // Each copy as it was — its identity, its pair, its problem — and in the job: none dropped, and a Retry may settle
        // the stop as before.
        Assert.Equal(
            [
                (heldId, 42L, 5_000_000_000L, TestMedia.ModifiedUtc.Ticks, (Guid?)newFileId, (string?)HeldReason, (DateTime?)null, false),
                (pairedId, 43L, 7_000_000_000L, TestMedia.ModifiedUtc.Ticks, (Guid?)newFileId, (string?)null, (DateTime?)null, false),
            ],
            copies.Select(copy => (copy.Id, copy.Inode, copy.SizeBytes, copy.ModifiedUtc.Ticks, copy.PairedNewFileId, copy.Problem, copy.DroppedUtc,
                copy.OnlyDropSettles)));
        Assert.Equal(newFileId, (await check.ReplacementJobNewFiles.AsNoTracking().SingleAsync(cancellationToken)).Id);
        var name = await check.ReplacementJobNames.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((nameId, pairedId, ReplacementNameState.Planned), (name.Id, name.ReplacementJobFileId, name.State));
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
        // ADR 0009); this upgrade adds a nullable column and a NOT NULL one with its default in place — with no key, which
        // would rebuild —, and touches no index.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE ", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobFiles\" ADD \"DroppedUtc\" TEXT NULL;", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE \"ReplacementJobFiles\" ADD \"OnlyDropSettles\" INTEGER NOT NULL DEFAULT 0;", script, StringComparison.Ordinal);
    }

    private static string Text(Guid value) => value.ToString().ToUpperInvariant();

    private static string Text(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
}
