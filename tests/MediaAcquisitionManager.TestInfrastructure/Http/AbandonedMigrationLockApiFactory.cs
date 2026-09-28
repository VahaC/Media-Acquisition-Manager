using MediaAcquisitionManager.Core.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// The start after a process was killed while it held EF Core's migration lock: the database holds
/// a <c>__EFMigrationsLock</c> row that nobody will ever release.
/// </summary>
public sealed class AbandonedMigrationLockApiFactory : MediaAcquisitionManagerApiFactory
{
    /// <summary>When the abandoned lock was taken, in the text form EF Core writes: long enough ago
    /// that no live migration can be holding it.</summary>
    public const string LockTakenAt = "2026-09-27 08:00:00+00:00";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Copies the migrated template into the data directory first; the lock goes into that copy.
        base.ConfigureWebHost(builder);

        // No pooling, so the file is closed again before the app opens it.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DataDirectory, StorageLayout.DatabaseFileName),
            Pooling = false,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        // The table as EF Core creates it before taking the lock (the template's own migration run
        // normally made it already), then the row a killed process leaves behind.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK___EFMigrationsLock" PRIMARY KEY,
                "Timestamp" TEXT NOT NULL
            );
            INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp") VALUES (1, $takenAt);
            """;
        command.Parameters.AddWithValue("$takenAt", LockTakenAt);
        command.ExecuteNonQuery();
    }
}
