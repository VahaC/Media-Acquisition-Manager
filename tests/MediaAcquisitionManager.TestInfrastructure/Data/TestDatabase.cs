using System.Globalization;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// Where test databases live and how they are made: everything under
/// <c>%TEMP%/mam-tests/&lt;process id&gt;/</c>, so two runs side by side (two test projects, two
/// shards) never share a file, and the whole tree goes when the process exits.
/// </summary>
public static class TestDatabase
{
    /// <summary>This process's directory; every test database and app-factory directory is inside it.</summary>
    public static string ProcessDirectory { get; } = CreateProcessDirectory();

    /// <summary>
    /// A database with every migration applied, built once per process and then copied: a copy is a
    /// file copy, a migration run is seconds of schema work that would otherwise repeat for every
    /// test class. The app still calls <c>Migrate()</c> on its copy, finds the history complete and
    /// does nothing.
    /// </summary>
    private static readonly Lazy<string> MigratedTemplate = new(BuildMigratedTemplate);

    /// <summary>The database file for <paramref name="key"/> (a test class name) in this process.</summary>
    public static string PathFor(string key) => Path.Combine(ProcessDirectory, $"{key}.db");

    /// <summary>A new, empty, uniquely named directory for one app factory.</summary>
    public static string CreateFactoryDirectory()
    {
        var directory = Path.Combine(ProcessDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Copies the migrated template to <paramref name="databasePath"/>, replacing any file there.</summary>
    public static void CopyMigratedTemplateTo(string databasePath) =>
        File.Copy(MigratedTemplate.Value, databasePath, overwrite: true);

    /// <summary>Deletes a database file together with its WAL and shared-memory files.</summary>
    public static void DeleteDatabase(string databasePath)
    {
        // A pooled connection keeps the file open, and Windows refuses to delete an open file.
        ClearPoolOf(databasePath);
        foreach (var suffix in (string[])["", "-wal", "-shm", "-journal"])
            File.Delete(databasePath + suffix);
    }

    /// <summary>Closes the pooled connections the app's connection string for <paramref name="databasePath"/> keeps open.</summary>
    public static void ClearPoolOf(string databasePath) =>
        ClearPool(PersistenceInfrastructureExtensions.ConnectionStringFor(databasePath));

    /// <summary>
    /// Closes the pooled connections of <paramref name="connectionString"/>, and only those — never
    /// <see cref="SqliteConnection.ClearAllPools"/> while tests run. That clears the pools of every class running beside
    /// this one too, so a class depends on how the library treats its neighbours' connections: in Microsoft.Data.Sqlite
    /// 10.0.12 a clear that met another thread's open took the connection that open was handing out (dotnet/efcore#39008,
    /// fixed in 10.0.13) — its native handle closed under it, or its collation removed while it ran a statement —, and the
    /// suite failed now and then with ObjectDisposedException 'SQLitePCL.sqlite3', a link left Writing, or "database is
    /// locked" thrown by the clear into the clearing class's cleanup.
    /// </summary>
    public static void ClearPool(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>Best-effort removal of a directory tree (an app factory's, or this process's). The caller first clears
    /// the pools of the databases inside it (<see cref="ClearPoolOf"/>).</summary>
    public static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Something still holds a file open (a connection another test has not returned yet).
            // Failing the test over temp cleanup would hide its real result; whatever is left goes
            // with the process directory at exit.
        }
    }

    private static string CreateProcessDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "mam-tests", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        // A process id is reused eventually; whatever a killed earlier run left under this one is stale.
        DeleteDirectory(directory);
        Directory.CreateDirectory(directory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // The one place every pool may go at once: no test runs any more.
            SqliteConnection.ClearAllPools();
            DeleteDirectory(directory);
        };
        return directory;
    }

    private static string BuildMigratedTemplate()
    {
        var path = Path.Combine(ProcessDirectory, "template.db");

        // Deliberately without SqlitePragmaInterceptor: in WAL mode committed pages can still sit in
        // template.db-wal, and a File.Copy of the main file alone would miss them. The default
        // rollback journal keeps the template one self-contained file; each app switches its own
        // copy to WAL on first open.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMediaAcquisitionManagerSqlite(path)
            .Options;
        using (var db = new ApplicationDbContext(options))
            db.Database.Migrate();

        // Copied, never opened again: release the pooled handle so no copy can meet a lock.
        ClearPoolOf(path);
        return path;
    }
}
