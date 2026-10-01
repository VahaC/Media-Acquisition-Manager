using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-11 — the destinations' promises, pinned on the migrated schema: at most one default destination for
// the whole installation however two saves race, any number of others beside it, none at all allowed,
// and every destination stored before the default existed is not the default.
public sealed class DestinationPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public DestinationPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_second_default_destination_is_refused_whichever_client_it_belongs_to(bool sameClient)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var client = await integrations.Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);
        var other = sameClient ? client : await integrations.Create("qBittorrent 2", "qbittorrent", IntegrationKind.DownloadClient);
        var destinations = Destinations(db);
        await destinations.Create(client.Id, "Movies", "Movies", isDefault: true);

        //Act
        var exception = await Record.ExceptionAsync(() => destinations.Create(other.Id, "Movies 4K", "Movies 4K", isDefault: true));

        //Assert
        // The backstop behind the service, which moves the default rather than adding a second one.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: DestinationMappings.IsDefault", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(["Movies"], await check.DestinationMappings.Select(x => x.Name).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Any_number_of_destinations_that_are_not_the_default_sit_beside_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var client = await integrations.Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);
        var other = await integrations.Create("qBittorrent 2", "qbittorrent", IntegrationKind.DownloadClient);
        var destinations = Destinations(db);

        //Act
        await destinations.Create(client.Id, "Movies", "Movies", isDefault: true);
        await destinations.Create(client.Id, "Movies 4K", "Movies 4K");
        await destinations.Create(client.Id, "Cartoons", "Cartoons");
        await destinations.Create(other.Id, "Movies", "Movies");

        //Assert
        await using var check = CreateContext();
        Assert.Equal(
            [("Cartoons", false), ("Movies", false), ("Movies", true), ("Movies 4K", false)],
            (await check.DestinationMappings.Select(x => new { x.Name, x.IsDefault }).ToListAsync(cancellationToken))
                .Select(x => (x.Name, x.IsDefault))
                .Order());
    }

    [Fact]
    public async Task Destinations_without_any_default_are_allowed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var client = await Integrations(db).Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);
        var destinations = Destinations(db);

        //Act
        // A removed or un-marked default is never replaced automatically (MAM-11).
        await destinations.Create(client.Id, "Movies", "Movies");
        await destinations.Create(client.Id, "Movies 4K", "Movies 4K");

        //Assert
        await using var check = CreateContext();
        Assert.Equal(2, await check.DestinationMappings.CountAsync(x => !x.IsDefault, cancellationToken));
    }

    [Fact]
    public async Task Moving_the_default_in_one_save_clears_the_old_before_setting_the_new()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var client = await Integrations(db).Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);
        var destinations = Destinations(db);
        var movies = await destinations.Create(client.Id, "Movies", "Movies", isDefault: true);
        var movies4K = await destinations.Create(client.Id, "Movies 4K", "Movies 4K");
        var tracked = await db.DestinationMappings.AsTracking().ToDictionaryAsync(x => x.Id, cancellationToken);

        //Act
        // Set first and cleared second in code: the order SaveChanges writes them in is what counts, as
        // SQLite checks the index as each row changes.
        tracked[movies4K.Id].IsDefault = true;
        tracked[movies.Id].IsDefault = false;
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));

        //Assert
        Assert.Null(exception);
        await using var check = CreateContext();
        Assert.Equal(movies4K.Id, (await check.DestinationMappings.SingleAsync(x => x.IsDefault, cancellationToken)).Id);
    }

    [Fact]
    public async Task A_destination_stored_without_the_default_column_is_not_the_default()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var client = await Integrations(db).Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);

        //Act
        // A row as an installation from before MAM-11 holds it: the migration's column default fills it in.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "DestinationMappings" ("Id", "DownloadClientConnectionId", "Name", "NormalizedName", "ClientDestinationKey", "CreatedUtc")
            VALUES ({Guid.NewGuid()}, {client.Id}, 'Movies', 'MOVIES', 'Movies', {TimeProvider.System.GetUtcNow().UtcDateTime})
            """,
            cancellationToken);

        //Assert
        Assert.False((await db.DestinationMappings.AsNoTracking().SingleAsync(cancellationToken)).IsDefault);
    }

    [Fact]
    public async Task The_default_index_counts_only_the_default_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT \"sql\" AS \"Value\" FROM \"sqlite_master\" WHERE \"name\" = 'IX_DestinationMappings_OneDefault'")
            .SingleAsync(cancellationToken);

        //Assert
        // Partial: unique among the default rows only, so the others never collide on their shared false.
        Assert.Equal(
            "CREATE UNIQUE INDEX \"IX_DestinationMappings_OneDefault\" ON \"DestinationMappings\" (\"IsDefault\") WHERE \"IsDefault\" = 1",
            definition);
    }

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);

    private static IDestinationMappingDataFactory Destinations(ApplicationDbContext db) => new DestinationMappingDataFactory(db, TimeProvider.System);
}
