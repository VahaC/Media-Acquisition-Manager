using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// Every assertion is on a value the connection would otherwise have differently, so a pass means
// the interceptor ran on that open: SQLite defaults journal_mode to "delete" and busy_timeout to 0,
// and foreign keys (which the bundled SQLite enables by default) are switched off by the
// connection string of CreateContextWithForeignKeysOffAtOpen.
public sealed class SqlitePragmaInterceptorTests : DatabaseTestBase
{
    // MAM-8 — seven titles out of order, sorted by the collation the interceptor registers.
    private const string TitlesByLibraryTitle =
        "SELECT \"column1\" AS \"Value\" FROM (VALUES ('Яга'), ('Alien'), ('Їжа'), ('Інтерстеллар'), ('Ґава'), ('Гнів'), ('Аватар')) " +
        "ORDER BY \"column1\" COLLATE " + LibraryTitleOrder.CollationName;

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

    // MAM-8 — without the interceptor SQLite has no such collation and the query fails; with it, the
    // titles come back in Ukrainian alphabetical order rather than by code point (І and Ї before А,
    // Ґ after Я) or with Ґ sorted as a variant of Г.
    [Fact]
    public async Task OpenConnection_registers_the_library_title_collation()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        db.Database.OpenConnection();

        //Assert
        Assert.Equal(["Аватар", "Гнів", "Ґава", "Інтерстеллар", "Їжа", "Яга", "Alien"], await OrderByLibraryTitleAsync(db, cancellationToken));
    }

    [Fact]
    public async Task OpenConnectionAsync_registers_the_library_title_collation_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        await db.Database.OpenConnectionAsync(cancellationToken);

        //Assert
        Assert.Equal(["Аватар", "Гнів", "Ґава", "Інтерстеллар", "Їжа", "Яга", "Alien"], await OrderByLibraryTitleAsync(db, cancellationToken));
    }

    private static Task<List<string>> OrderByLibraryTitleAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<string>(TitlesByLibraryTitle).ToListAsync(cancellationToken);
}
