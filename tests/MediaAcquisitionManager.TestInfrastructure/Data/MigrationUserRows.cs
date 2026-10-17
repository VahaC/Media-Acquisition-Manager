using System.Globalization;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// MAM-46 — a user written the way every schema since the first stores one, for a migration test that plants an account
/// in a database migrated to an older migration. Raw SQL with the columns every Users table has had: the
/// <see cref="UserDataFactory"/> writes today's model, whose <c>Language</c> column a table from before
/// <c>AddLanguages</c> hasn't.
/// </summary>
public static class MigrationUserRows
{
    /// <summary>Inserts a user named <paramref name="userName"/> who never signed in and whose password hash nothing
    /// checks, and returns their id.</summary>
    public static async Task<Guid> InsertAsync(ApplicationDbContext db, string userName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "UserName", "NormalizedUserName", "PasswordHash", "IsDisabled", "CreatedUtc")
            VALUES ({0}, {1}, {2}, 'not-a-hash', 0, {3})
            """,
            [
                id.ToString().ToUpperInvariant(),
                userName,
                UserNameRules.Normalize(userName),
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            ],
            cancellationToken);
        return id;
    }
}
