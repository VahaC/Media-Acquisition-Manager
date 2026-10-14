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

// MAM-40 — the AddCopyRemovals upgrade: two new tables, the owner's removals of a duplicate copy and their names, with their
// indexes, and nothing else — the name columns a removal's names share with a replacement's are an unmapped base class's
// (QuarantinedNameEntity), so ReplacementJobNames is neither altered nor rebuilt, and a replacement's names written before
// the upgrade read back whole through the extracted base.
public sealed class AddCopyRemovalsMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261011090512_AddReplacementCopyDrops";
    private const string ThisMigration = "20261012150356_AddCopyRemovals";

    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Upgrade_creates_the_two_removal_tables_and_their_indexes_and_touches_no_other_table()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ThisMigration);

        //Assert
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplacementJobNames", script, StringComparison.Ordinal);
        Assert.Equal(
            ["CopyRemovals", "CopyRemovalNames"],
            script.Split('\n').Where(line => line.StartsWith("CREATE TABLE", StringComparison.Ordinal)).Select(line => line.Split('"')[1]));
        Assert.Equal(
            ["IX_CopyRemovalNames_CopyRemovalId_Position", "IX_CopyRemovals_Open", "IX_CopyRemovals_RequestedByUserId"],
            script.Split('\n').Where(line => line.Contains("CREATE ", StringComparison.Ordinal) && line.Contains("INDEX", StringComparison.Ordinal))
                .Select(line => line.Split('"')[1]));
    }

    [Fact]
    public async Task Upgrade_keeps_every_replacement_name_whole()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        var copyId = Guid.NewGuid();
        var nameId = Guid.NewGuid();
        var movedUtc = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            var owner = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1");
            var release = await new ReleaseDataFactory(db, Encryption, TimeProvider.System).Create(sourceConnectionId: null);
            var download = await InsertDownloadJobAsync(db, release.Id, owner.Id, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobs" ("Id", "DownloadJobId", "RequestedByUserId", "RequestedByUserName", "State", "StateChangedUtc", "Reason",
                    "ReleaseEditions", "CreatedUtc", "InspectionStartedUtc", "LibraryAskedUtc", "QuarantineCheckedUtc", "QuarantinedUtc", "LibraryNoticeDueUtc")
                VALUES ({0}, {1}, {2}, 'owner', 'Quarantined', {3}, NULL, '', {3}, {3}, {3}, {3}, {3}, NULL)
                """,
                [Text(jobId), Text(download), Text(owner.Id), Text(movedUtc)],
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ReplacementJobNames" ("Id", "ReplacementJobId", "Position", "ReplacementJobFileId", "Kind", "AppPath", "QuarantinePath",
                    "AllowedRoot", "DeviceId", "Inode", "SizeBytes", "ModifiedUtc", "LinkCount", "State", "Problem", "MovedUtc", "RestoredUtc", "DeletedUtc")
                VALUES ({0}, {1}, 3, {2}, 'Companion', '/media/movies/Alien (1979)/Alien.uk.srt', {3}, '/media', {4}, 9223372036854775807, 52345, {5},
                    2, 'Quarantined', 'Held.', {6}, NULL, NULL)
                """,
                [Text(nameId), Text(jobId), Text(copyId), $"/media/.mam-quarantine/{jobId}/movies/Alien (1979)/Alien.uk.srt", TestMedia.DeviceId,
                    Text(TestMedia.ModifiedUtc), Text(movedUtc)],
                cancellationToken);
        }

        //Act
        await using (var db = CreateContext())
            await db.Database.MigrateAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var name = await check.ReplacementJobNames.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (nameId, jobId, copyId, 3, ReplacementNameKind.Companion, "/media", TestMedia.DeviceId, long.MaxValue, 52_345L),
            (name.Id, name.ReplacementJobId, name.ReplacementJobFileId, name.Position, name.Kind, name.AllowedRoot, name.DeviceId, name.Inode, name.SizeBytes));
        Assert.Equal(
            (TestMedia.ModifiedUtc.Ticks, (int?)2, ReplacementNameState.Quarantined, (string?)"Held.", (long?)movedUtc.Ticks),
            (name.ModifiedUtc.Ticks, name.LinkCount, name.State, name.Problem, name.MovedUtc?.Ticks));
        Assert.Equal(0, await check.CopyRemovals.CountAsync(cancellationToken));
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
