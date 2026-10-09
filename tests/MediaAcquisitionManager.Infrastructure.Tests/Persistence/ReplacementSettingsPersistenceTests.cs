using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-24 — the owner's replacement switch and per-folder ticks, pinned on the migrated schema (ADR 0033): the switch is
// one row at most, under its fixed key, so two first saves can't both insert one; a folder is ticked once, the
// backstop behind the rules for two saves racing; and the audit rows of switching and ticking are stored by name.
public sealed class ReplacementSettingsPersistenceTests : DatabaseTestBase
{
    public ReplacementSettingsPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task The_audit_actions_of_the_switch_and_the_ticks_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1");
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        auditTrail.Record(AuditAction.ReplacementSwitchedOn, AuditActor.From(owner), target: null);
        auditTrail.Record(AuditAction.ReplacementSwitchedOff, AuditActor.From(owner), target: null);
        auditTrail.Record(AuditAction.ReplacementFoldersChanged, AuditActor.From(owner), target: null);
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var actions = await db.Database
            .SqlQueryRaw<string>("SELECT \"Action\" AS \"Value\" FROM \"AuditEvents\" ORDER BY \"Action\"")
            .ToListAsync(cancellationToken);

        //Assert
        // By name, like every other enum: a value added later never changes what a stored row means.
        Assert.Equal(["ReplacementFoldersChanged", "ReplacementSwitchedOff", "ReplacementSwitchedOn"], actions);
    }

    [Fact]
    public async Task A_second_switch_row_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await new ReplacementSettingsDataFactory(db).Create(isEnabled: true);

        //Act
        var exception = await Record.ExceptionAsync(() => new ReplacementSettingsDataFactory(db).Create(isEnabled: false));

        //Assert
        // One key for the one row: the backstop behind the save's read under the write lock.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: ReplacementSettings.Id", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        var settings = await check.ReplacementSettings.SingleAsync(cancellationToken);
        Assert.Equal((new Guid("c0000000-0000-0000-0000-000000000002"), true), (settings.Id, settings.IsEnabled));
    }

    [Fact]
    public async Task A_second_tick_on_one_folder_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Folders(db).Create("/media/movies");

        //Act
        var exception = await Record.ExceptionAsync(() => Folders(db).Create("/media/movies", position: 1));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: ReplacementFolders.Path", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.ReplacementFolders.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Another_folder_is_another_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Folders(db).Create("/media/movies");

        //Act
        var exception = await Record.ExceptionAsync(() => Folders(db).Create("/media/tv-shows", position: 1));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(
            [("/media/movies", 0), ("/media/tv-shows", 1)],
            (await check.ReplacementFolders.OrderBy(folder => folder.Position).ToListAsync(cancellationToken)).Select(folder => (folder.Path, folder.Position)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static IReplacementFolderDataFactory Folders(ApplicationDbContext db) => new ReplacementFolderDataFactory(db);
}
