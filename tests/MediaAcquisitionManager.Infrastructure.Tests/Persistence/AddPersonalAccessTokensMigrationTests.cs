using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-5 — the AddPersonalAccessTokens upgrade, the one migration of the ticket: a new table of personal access tokens with
// its two indexes — the unique hash every token request looks up, and the user's tokens newest first — and its cascade to
// Users, and nothing else: no table is rebuilt (ADR 0009's warning 20410 doesn't arise) and every account comes through
// whole, able to hold tokens at once. The test migrates a fresh database to the migration before with the app's own
// migrator, writes an account, then upgrades it as the app's start does.
public sealed class AddPersonalAccessTokensMigrationTests : DatabaseTestBase
{
    private const string PreviousMigration = "20261014185448_AddCatalogWorkOverviews";
    private const string ThisMigration = "20261016112131_AddPersonalAccessTokens";

    [Fact]
    public void Upgrade_creates_the_tokens_table_and_its_two_indexes_and_touches_no_other_table()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ThisMigration);

        //Assert
        // A rebuild is EF's copy into an "ef_temp_" table with foreign keys off between transactions (warning 20410,
        // ADR 0009); this upgrade only adds.
        Assert.DoesNotContain("ef_temp_", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRAGMA foreign_keys = 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.Ordinal);
        Assert.Equal(
            ["PersonalAccessTokens"],
            script.Split('\n').Where(line => line.StartsWith("CREATE TABLE", StringComparison.Ordinal)).Select(line => line.Split('"')[1]));
        Assert.Equal(
            [
                "CREATE UNIQUE INDEX \"IX_PersonalAccessTokens_TokenHash\" ON \"PersonalAccessTokens\" (\"TokenHash\");",
                "CREATE INDEX \"IX_PersonalAccessTokens_UserId_CreatedUtc\" ON \"PersonalAccessTokens\" (\"UserId\", \"CreatedUtc\");",
            ],
            script.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("CREATE ", StringComparison.Ordinal) && line.Contains(" INDEX ", StringComparison.Ordinal)));
        // Deleting a user ends their tokens with them (ADR 0040).
        Assert.Contains(
            "CONSTRAINT \"FK_PersonalAccessTokens_Users_UserId\" FOREIGN KEY (\"UserId\") REFERENCES \"Users\" (\"Id\") ON DELETE CASCADE",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upgrade_keeps_every_account_whole_and_lets_it_hold_tokens_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid userId;
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            // The Users table this upgrade leaves as it is: its factory writes it.
            userId = (await new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1")).Id;
        }

        //Act
        int tokensAfterTheUpgrade;
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync(cancellationToken);
            tokensAfterTheUpgrade = await db.PersonalAccessTokens.CountAsync(cancellationToken);
            // The first token the account makes after the upgrade.
            await new PersonalAccessTokenDataFactory(db, TimeProvider.System).Create(userId, PersonalAccessTokenAccess.ReadOnly, "Script", expiresUtc: null);
        }

        //Assert
        await using var check = CreateContext();
        Assert.Equal("owner", (await check.Users.AsNoTracking().SingleAsync(user => user.Id == userId, cancellationToken)).UserName);
        Assert.Equal(0, tokensAfterTheUpgrade);
        Assert.Equal(userId, (await check.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).UserId);
    }
}
