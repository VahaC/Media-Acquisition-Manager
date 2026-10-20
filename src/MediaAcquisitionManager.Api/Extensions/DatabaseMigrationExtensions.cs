using System.Globalization;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Extensions;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// The table EF Core (9 and later) locks migrations with on SQLite: <c>Migrate()</c> inserts row 1
    /// before it applies anything and deletes it afterwards, and while the row exists it retries the
    /// insert with no timeout.
    /// </summary>
    private const string MigrationLockTable = "__EFMigrationsLock";

    private const string SelectLockSql = "SELECT \"Timestamp\" AS \"Value\" FROM \"" + MigrationLockTable + "\"";

    // Only the row that was read: a lock another process has taken since then is not ours to break.
    private const string DeleteLockSql = "DELETE FROM \"" + MigrationLockTable + "\" WHERE \"Timestamp\" = {0}";

    /// <summary>
    /// How old a migration lock must be to count as abandoned. This app's migrations take seconds on
    /// SQLite, so no live process holds the lock this long; and a container killed mid-upgrade and
    /// restarted at once is serving again within this time without anyone touching the database.
    /// </summary>
    private static readonly TimeSpan AbandonedLockAge = TimeSpan.FromMinutes(2);

    /// <summary>How often a lock that is not old enough yet is read again.</summary>
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Brings the database to this build's schema before the app answers a single request.
    /// </summary>
    /// <remarks>
    /// In-process, with no separate migrator: a fresh data directory becomes a working database on
    /// first start, and an upgraded build brings its schema along. A migration that fails stops the
    /// process here, instead of letting it serve requests against half a schema.
    /// </remarks>
    public static WebApplication ApplyDatabaseMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ReleaseAbandonedMigrationLock(
            db,
            app.Services.GetRequiredService<TimeProvider>(),
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrationExtensions)));
        db.Database.Migrate();

        // MAM-47 (R22) — stored English becomes key + values here, in C#: a migration holds only EF's own operations.
        // Before app.Run(), so the reconcilers — which write their own restart reasons at start — and the links' catch-up,
        // which selects rows by the restart key, only ever see converted rows. The catalogue is loaded first and outside
        // the converter's own catch: a broken embedded locale file is a build defect and stops the boot here, while the
        // converter never does.
        _ = TemplateCatalog.Embedded;
        scope.ServiceProvider.GetRequiredService<StoredTextConverter>().Convert();
        return app;
    }

    /// <summary>
    /// Deletes a migration lock that a process which stopped mid-migration left behind; waits while
    /// the lock is younger than <see cref="AbandonedLockAge"/>.
    /// </summary>
    /// <remarks>
    /// SQLite has no session lock that dies with its connection, so EF Core's lock is a row, and a
    /// process killed while holding it (<c>docker stop</c> or an OOM kill during an upgrade) leaves the
    /// row for good. Every later start then blocks inside <c>Migrate()</c>, before Kestrel listens, so
    /// even liveness gets no answer. EF Core's documented remedy is to delete the row by hand
    /// ("Handling abandoned locks", https://learn.microsoft.com/ef/core/providers/sqlite/limitations#concurrent-migrations-protection).
    /// This app is the only process that owns its database file, so it applies that remedy itself —
    /// but only to a lock old enough that no migration of this app can still be running under it.
    /// </remarks>
    private static void ReleaseAbandonedMigrationLock(ApplicationDbContext db, TimeProvider time, ILogger logger)
    {
        // A database no EF Core 9+ has migrated yet (a fresh one included) has no lock table at all.
        var lockTableExists = db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {MigrationLockTable}")
            .Single() > 0;
        if (!lockTableExists)
            return;

        var waitLogged = false;
        // Read client-side: EF Core only ever writes row 1, and composing First over raw SQL makes EF
        // log a Warning about a missing OrderBy on every start.
        while (db.Database.SqlQueryRaw<string>(SelectLockSql).AsEnumerable().FirstOrDefault() is { } takenAt)
        {
            // A stamp that doesn't parse wasn't written by a lock this EF Core version releases, so
            // nothing will ever release it: treat it as abandoned straight away.
            var age = DateTimeOffset.TryParse(takenAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var takenAtUtc)
                ? time.GetUtcNow() - takenAtUtc
                : AbandonedLockAge;

            if (age >= AbandonedLockAge)
            {
                db.Database.ExecuteSqlRaw(DeleteLockSql, takenAt);
                // Warning, so it ships off the box: the previous process stopped mid-migration.
                logger.LogWarning(
                    "Removed an abandoned database migration lock taken at {TakenAt}: the process that held it stopped before releasing it. Migrations continue now.",
                    takenAt);
                return;
            }

            if (!waitLogged)
            {
                // Warning too: startup is blocked, and whoever watches the log needs to know why.
                logger.LogWarning(
                    "The database migration lock has been held since {TakenAt}; waiting up to {Remaining} for it to be released before treating it as abandoned.",
                    takenAt,
                    AbandonedLockAge - age);
                waitLogged = true;
            }

            Thread.Sleep(AbandonedLockAge - age < LockPollInterval ? AbandonedLockAge - age : LockPollInterval);
        }
    }
}
