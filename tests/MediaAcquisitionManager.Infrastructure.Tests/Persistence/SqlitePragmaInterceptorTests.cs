using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// Every assertion is on a value the connection would otherwise have differently, so a pass means
// the interceptor ran on that open: SQLite defaults journal_mode to "delete" and busy_timeout to 0,
// and foreign keys (which the bundled SQLite enables by default) are switched off by the
// connection string of CreateContextWithForeignKeysOffAtOpen.
public sealed class SqlitePragmaInterceptorTests : DatabaseTestBase
{
    [Fact]
    public async Task OpenConnection_switches_the_database_to_wal()
    {
        //Arrange
        await using var db = CreateContext();

        //Act
        db.Database.OpenConnection();

        //Assert
        Assert.Equal("wal", await SqlitePragmas.ReadAsync(db, "journal_mode", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenConnection_sets_busy_timeout_and_foreign_keys()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContextWithForeignKeysOffAtOpen();

        //Act
        db.Database.OpenConnection();

        //Assert
        Assert.Equal("5000", await SqlitePragmas.ReadAsync(db, "busy_timeout", cancellationToken));
        Assert.Equal("1", await SqlitePragmas.ReadAsync(db, "foreign_keys", cancellationToken));
    }

    [Fact]
    public async Task OpenConnectionAsync_applies_every_pragma_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContextWithForeignKeysOffAtOpen();

        //Act
        await db.Database.OpenConnectionAsync(cancellationToken);

        //Assert
        Assert.Equal("wal", await SqlitePragmas.ReadAsync(db, "journal_mode", cancellationToken));
        Assert.Equal("5000", await SqlitePragmas.ReadAsync(db, "busy_timeout", cancellationToken));
        Assert.Equal("1", await SqlitePragmas.ReadAsync(db, "foreign_keys", cancellationToken));
    }
}
