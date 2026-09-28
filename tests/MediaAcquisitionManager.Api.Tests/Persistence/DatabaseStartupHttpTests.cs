using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Persistence;

// On FirstRunApiFactory, so the app starts from an empty data directory: the database, the schema
// and the pragmas below are all the app's own doing, not the test template's.
public sealed class DatabaseStartupHttpTests : IClassFixture<FirstRunApiFactory>, IAsyncLifetime
{
    private readonly FirstRunApiFactory _factory;

    public DatabaseStartupHttpTests(FirstRunApiFactory factory) => _factory = factory;

    // Resetting starts the host, which is the startup under test.
    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Startup_creates_the_database_file_in_the_configured_data_directory()
    {
        //Arrange
        var expectedPath = Path.Combine(_factory.DataDirectory, "mam.db");
        string? dataSource = null;

        //Act
        await _factory.WithDbAsync(db =>
        {
            dataSource = db.Database.GetDbConnection().DataSource;
            return Task.CompletedTask;
        });

        //Assert
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(expectedPath, dataSource);
        Assert.Equal(expectedPath, _factory.Resolve<StorageLayout>().DatabasePath);
    }

    [Fact]
    public async Task Startup_applies_every_migration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] known = [];
        string[] applied = [];

        //Act
        await _factory.WithDbAsync(async db =>
        {
            known = [.. db.Database.GetMigrations()];
            applied = [.. await db.Database.GetAppliedMigrationsAsync(cancellationToken)];
        });

        //Assert
        Assert.Contains(applied, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Equal(known, applied);
    }

    [Fact]
    public async Task App_connections_run_in_wal_mode_with_foreign_keys_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var pragmas = new Dictionary<string, string?>();

        //Act
        await _factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            foreach (var pragma in (string[])["journal_mode", "foreign_keys", "busy_timeout"])
                pragmas[pragma] = await SqlitePragmas.ReadAsync(db, pragma, cancellationToken);
        });

        //Assert
        Assert.Equal("wal", pragmas["journal_mode"]);
        // The outcome the app needs, not proof of who produced it: the bundled SQLite enables foreign
        // keys by default. busy_timeout is what proves the interceptor ran on the app's connections;
        // SqlitePragmaInterceptorTests pins the foreign_keys pragma itself.
        Assert.Equal("1", pragmas["foreign_keys"]);
        Assert.Equal("5000", pragmas["busy_timeout"]);
    }
}
