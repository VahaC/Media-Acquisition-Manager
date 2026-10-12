using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-40 — the owner's removal of a duplicate copy, pinned on the migrated schema (ADR 0030's quarantine): its names part of
// it, gone with it, one per place in its plan; the removal outliving the account that made it, its name kept; its state, its
// group's class, its names' kinds and states, its audit actions and targets stored by name, a name named by the file's own
// name; a name's identity kept to the bit and the tick; and the index over the open removals only.
public sealed class CopyRemovalPersistenceTests : DatabaseTestBase
{
    private const string VideoPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string QuarantinePath = "/media/.mam-quarantine/x/movies/Alien (1979)/Alien (1979) 2160p.mkv";

    public CopyRemovalPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task A_removals_names_go_with_it_and_keep_their_identity_to_the_bit_and_the_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var removal = await Removals(db).Create(owner.Id, CopyRemovalState.Quarantined, VideoPath, "/data/movies/Alien (1979)/Alien (1979) 2160p.mkv");
        var facts = TestMedia.File(inode: long.MaxValue, deviceId: TestMedia.DeviceId);
        await Removals(db).AddName(removal.Id, 0, ReplacementNameKind.Video, VideoPath, QuarantinePath, "/media", facts, ReplacementNameState.Quarantined);
        await using (var read = CreateContext())
        {
            var name = await read.CopyRemovalNames.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((TestMedia.DeviceId, long.MaxValue, TestMedia.ModifiedUtc.Ticks), (name.DeviceId, name.Inode, name.ModifiedUtc.Ticks));
        }

        //Act
        await db.CopyRemovals.Where(row => row.Id == removal.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the names are part of their removal.
        await using var check = CreateContext();
        Assert.Equal(0, await check.CopyRemovalNames.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_second_name_at_one_place_of_a_removals_plan_is_refused()
    {
        //Arrange
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var removal = await Removals(db).Create(owner.Id, CopyRemovalState.Moving, VideoPath, VideoPath);
        await Removals(db).AddName(removal.Id, 0, ReplacementNameKind.Video, VideoPath, QuarantinePath, "/media", TestMedia.File(inode: 1));

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Removals(db).AddName(removal.Id, 0, ReplacementNameKind.Companion, VideoPath + ".srt", QuarantinePath + ".srt", "/media", TestMedia.File(inode: 2)));

        //Assert
        Assert.Contains(
            "UNIQUE constraint failed: CopyRemovalNames.CopyRemovalId, CopyRemovalNames.Position",
            Assert.IsType<DbUpdateException>(exception).InnerException!.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_removal_outlives_the_account_that_made_it_with_its_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        await Removals(db).Create(owner.Id, CopyRemovalState.Deleted, VideoPath, VideoPath);

        //Act
        await db.Users.Where(user => user.Id == owner.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        await using var check = CreateContext();
        var removal = await check.CopyRemovals.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(((Guid?)null, "owner"), (removal.RequestedByUserId, removal.RequestedByUserName));
    }

    [Fact]
    public async Task The_states_class_and_names_kinds_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var removal = await Removals(db).Create(owner.Id, CopyRemovalState.NeedsReview, VideoPath, VideoPath, groupClass: DuplicateClass.ByteIdentical);
        await Removals(db).AddName(removal.Id, 0, ReplacementNameKind.Companion, VideoPath, QuarantinePath, "/media", TestMedia.File(inode: 1), ReplacementNameState.Restoring);

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT "CopyRemovals"."State" || '/' || "GroupClass" || '/' || "Kind" || '/' || "CopyRemovalNames"."State" AS "Value"
                FROM "CopyRemovals" JOIN "CopyRemovalNames" ON "CopyRemovalNames"."CopyRemovalId" = "CopyRemovals"."Id"
                """)
            .SingleAsync(cancellationToken);

        //Assert
        Assert.Equal("NeedsReview/ByteIdentical/Companion/Restoring", stored);
    }

    [Fact]
    public async Task The_audit_actions_and_targets_of_a_removal_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var removal = await Removals(db).Create(owner.Id, CopyRemovalState.Quarantined, VideoPath, VideoPath, title: "The Gods Must Be Crazy (1980)");
        var name = await Removals(db).AddName(removal.Id, 0, ReplacementNameKind.Video, VideoPath, QuarantinePath, "/media", TestMedia.File(inode: 1));
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        auditTrail.Record(AuditAction.CopyRemovalStarted, AuditActor.From(owner), AuditTarget.CopyRemoval(removal));
        auditTrail.Record(AuditAction.CopyRemovalNameNotRestored, AuditActor.From(owner), AuditTarget.CopyRemovalName(name));
        auditTrail.Record(AuditAction.CopyRemovalDeletedForGood, null, AuditTarget.CopyRemoval(removal));
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var rows = await db.Database
            .SqlQueryRaw<string>("SELECT \"Action\" || '/' || \"TargetKind\" || '/' || \"TargetName\" AS \"Value\" FROM \"AuditEvents\" ORDER BY \"Action\"")
            .ToListAsync(cancellationToken);

        //Assert
        Assert.Equal(
            [
                "CopyRemovalDeletedForGood/CopyRemoval/The Gods Must Be Crazy (1980)",
                "CopyRemovalNameNotRestored/CopyRemovalName/Alien (1979) 2160p.mkv",
                "CopyRemovalStarted/CopyRemoval/The Gods Must Be Crazy (1980)",
            ],
            rows);
    }

    [Fact]
    public async Task The_open_index_holds_only_the_removals_still_open()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT \"sql\" AS \"Value\" FROM \"sqlite_master\" WHERE \"name\" = 'IX_CopyRemovals_Open'")
            .SingleAsync(cancellationToken);

        //Assert
        // Partial: the few still going, never the history. The states by name — exactly CopyRemovalStates.Open.
        Assert.Equal(
            "CREATE INDEX \"IX_CopyRemovals_Open\" ON \"CopyRemovals\" (\"State\") WHERE \"State\" IN ('Moving', 'Quarantined', 'NeedsReview', 'Restoring', 'Deleting')",
            definition);
    }

    private static CopyRemovalDataFactory Removals(ApplicationDbContext db) => new(db, TimeProvider.System);

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1");
}
