using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// Base for tests that need a real SQLite database without the HTTP host (persistence pieces in
/// Infrastructure.Tests). The file is keyed by the concrete test class, inside this process's
/// directory, so classes running in parallel never share one; tests within a class run one after
/// another, and each gets a fresh file because it is deleted after every test.
/// </summary>
public abstract class DatabaseTestBase : IDisposable
{
    // The connection strings this test's contexts were given beside the app's own (the foreign-keys-off one). Each has a
    // pool of its own, which keeps the file open until that pool is cleared.
    private readonly HashSet<string> _otherConnectionStrings = [];

    protected DatabaseTestBase()
    {
        DatabasePath = TestDatabase.PathFor(GetType().Name);
        // Whatever an aborted earlier test left behind would leak its state into this one.
        TestDatabase.DeleteDatabase(DatabasePath);
    }

    /// <summary>This test's database file. It doesn't exist until a context opens it.</summary>
    protected string DatabasePath { get; }

    /// <summary>
    /// A context over <see cref="DatabasePath"/> configured the way the app configures its own —
    /// the same SQLite recipe and <see cref="SqlitePragmaInterceptor"/> — so a test sees the
    /// connection behaviour production has.
    /// </summary>
    protected ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMediaAcquisitionManagerSqlite(DatabasePath)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// <see cref="CreateContext"/> with <c>Foreign Keys=False</c> in the connection string, so
    /// Microsoft.Data.Sqlite switches foreign keys off while it opens the connection, before the
    /// interceptor runs. The bundled SQLite enables them by default, so on a plain context
    /// <c>foreign_keys = 1</c> proves nothing; on this one only the interceptor can turn them back on.
    /// </summary>
    protected ApplicationDbContext CreateContextWithForeignKeysOffAtOpen()
    {
        var db = CreateContext();
        var connectionString = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()) { ForeignKeys = false }.ToString();
        db.Database.SetConnectionString(connectionString);
        _otherConnectionStrings.Add(connectionString);
        return db;
    }

    public void Dispose()
    {
        foreach (var connectionString in _otherConnectionStrings)
            TestDatabase.ClearPool(connectionString);
        TestDatabase.DeleteDatabase(DatabasePath);
        GC.SuppressFinalize(this);
    }
}
