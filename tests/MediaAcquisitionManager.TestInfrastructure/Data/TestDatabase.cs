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
        SqliteConnection.ClearAllPools();
        foreach (var suffix in (string[])["", "-wal", "-shm", "-journal"])
            File.Delete(databasePath + suffix);
    }

    /// <summary>Best-effort removal of a directory tree (an app factory's, or this process's).</summary>
    public static void DeleteDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
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
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteDirectory(directory);
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
        SqliteConnection.ClearAllPools();
        return path;
    }
}
