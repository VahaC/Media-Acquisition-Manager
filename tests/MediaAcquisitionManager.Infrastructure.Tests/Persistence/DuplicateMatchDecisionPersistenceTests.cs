using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-21 — the owner's decisions on possible matches, pinned on the migrated schema (ADR 0029): one decision per group
// key, the backstop behind the service's read for two first decisions racing; the decision, and the audit rows of
// confirming, dismissing and undoing with their target kind, stored by name; and a decision outlives the account that
// gave it, its name copied. Deletes go through ExecuteDelete, so what happens to the row is the database's own doing,
// not EF's tracking.
public sealed class DuplicateMatchDecisionPersistenceTests : DatabaseTestBase
{
    private const string GoneKey = "0123456789abcdef0123456789abcdef";
    private const string HeatKey = "fedcba9876543210fedcba9876543210";

    public DuplicateMatchDecisionPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Decisions_and_their_audit_actions_and_target_kind_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db, "owner");
        await Decisions(db).Create(GoneKey, DuplicateMatchDecision.Confirmed, owner.Id);
        await Decisions(db).Create(HeatKey, DuplicateMatchDecision.Dismissed, owner.Id);
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        auditTrail.Record(AuditAction.DuplicateMatchConfirmed, AuditActor.From(owner), AuditTarget.DuplicateMatch(GoneKey, "Gone (2001)"));
        auditTrail.Record(AuditAction.DuplicateMatchDismissed, AuditActor.From(owner), AuditTarget.DuplicateMatch(HeatKey, "Heat (1995)"));
        auditTrail.Record(AuditAction.DuplicateMatchDecisionUndone, AuditActor.From(owner), AuditTarget.DuplicateMatch(GoneKey, "Gone (2001)"));
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var decisions = await Values(db, "SELECT \"Decision\" AS \"Value\" FROM \"DuplicateMatchDecisions\" ORDER BY \"GroupKey\"", cancellationToken);
        var actions = await Values(db, "SELECT \"Action\" AS \"Value\" FROM \"AuditEvents\" ORDER BY \"Action\"", cancellationToken);
        var targetKinds = await Values(db, "SELECT DISTINCT \"TargetKind\" AS \"Value\" FROM \"AuditEvents\"", cancellationToken);
        var targetIds = await db.AuditEvents.AsNoTracking().Select(auditEvent => auditEvent.TargetId!.Value).Distinct().ToListAsync(cancellationToken);

        //Assert
        // By name, like every other enum: a value reordered or added later never changes what a stored row means.
        Assert.Equal(["Confirmed", "Dismissed"], decisions);
        Assert.Equal(["DuplicateMatchConfirmed", "DuplicateMatchDecisionUndone", "DuplicateMatchDismissed"], actions);
        Assert.Equal(["DuplicateMatch"], targetKinds);
        // A group's key is its audit target's id: the 32 hex digits read back as the key, so every decision on a group
        // has one target.
        Assert.Equal([GoneKey, HeatKey], targetIds.Select(id => id.ToString("N")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_second_decision_on_one_group_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Decisions(db).Create(GoneKey, DuplicateMatchDecision.Confirmed);

        //Act
        var exception = await Record.ExceptionAsync(() => Decisions(db).Create(GoneKey, DuplicateMatchDecision.Dismissed));

        //Assert
        // One decision per group: the backstop behind the service's read, for two first decisions racing.
        AssertUniqueViolation(exception, "DuplicateMatchDecisions.GroupKey");
        await using var check = CreateContext();
        Assert.Equal(DuplicateMatchDecision.Confirmed, (await check.DuplicateMatchDecisions.SingleAsync(cancellationToken)).Decision);
    }

    [Fact]
    public async Task Another_groups_decision_is_another_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Decisions(db).Create(GoneKey, DuplicateMatchDecision.Confirmed);

        //Act
        var exception = await Record.ExceptionAsync(() => Decisions(db).Create(HeatKey, DuplicateMatchDecision.Confirmed));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(2, await check.DuplicateMatchDecisions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_their_decisions_with_their_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var decided = await Decisions(db).Create(GoneKey, DuplicateMatchDecision.Dismissed, user.Id, user.UserName);

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // SetNull: the owner's word outlives the account that gave it, like a link's setter; the name says who it was.
        var row = await db.DuplicateMatchDecisions.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (GoneKey, DuplicateMatchDecision.Dismissed, (Guid?)null, "vasyl", decided.DecidedUtc),
            (row.GroupKey, row.Decision, row.DecidedByUserId, row.DecidedByUserName, row.DecidedUtc));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");

    private static IDuplicateMatchDecisionDataFactory Decisions(ApplicationDbContext db) =>
        new DuplicateMatchDecisionDataFactory(db, TimeProvider.System);

    private static Task<List<string>> Values(ApplicationDbContext db, string sql, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);

    /// <summary>That <paramref name="exception"/> is SQLite refusing a row by the unique index over
    /// <paramref name="columns"/> (its error code 19, its message naming them).</summary>
    private static void AssertUniqueViolation(Exception? exception, string columns)
    {
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains($"UNIQUE constraint failed: {columns}", sqlite.Message, StringComparison.Ordinal);
    }
}
