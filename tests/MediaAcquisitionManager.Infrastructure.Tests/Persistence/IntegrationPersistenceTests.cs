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

// On a copy of the migrated template, not EnsureCreated: the indexes and delete behaviours pinned here
// are the ones the migrations give an installation. Deletes go through ExecuteDelete, so nothing is
// tracked and whatever happens to the dependent rows is the database's own doing.
public sealed class IntegrationPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public IntegrationPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task Two_integrations_can_not_share_a_name_in_any_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        await integrations.Create("Jellyfin", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef");

        //Act
        var exception = await Record.ExceptionAsync(() =>
            integrations.Create("JELLYFIN", "jellyfin", IntegrationKind.MediaLibrary, apiKey: "0123456789abcdef"));

        //Assert
        // MAM-7 — the backstop behind the service's 409, for two creates racing each other.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: IntegrationConnections.NormalizedName", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.IntegrationConnections.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_service_path_can_not_be_mapped_twice_for_one_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var exception = await Record.ExceptionAsync(() => Integrations(db).Create(
            "qBittorrent", "qbittorrent", IntegrationKind.DownloadClient,
            pathMappings: [new("/downloads", "/media"), new("/downloads", "/data")]));

        //Assert
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: PathMappings.IntegrationConnectionId, PathMappings.ServicePrefix",
            sqlite.Message,
            StringComparison.Ordinal);
        // One save: the integration went with its refused mappings.
        await using var check = CreateContext();
        Assert.Equal(0, await check.IntegrationConnections.CountAsync(cancellationToken));
        Assert.Equal(0, await check.PathMappings.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_an_integration_removes_its_path_and_destination_mappings()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var integrations = Integrations(db);
        var destinations = new DestinationMappingDataFactory(db, TimeProvider.System);
        var client = await integrations.Create(
            "qBittorrent", "qbittorrent", IntegrationKind.DownloadClient,
            pathMappings: [new("/downloads", "/media"), new("/incomplete", "/media/incomplete")]);
        var other = await integrations.Create(
            "qBittorrent 2", "qbittorrent", IntegrationKind.DownloadClient,
            pathMappings: [new("/downloads", "/media")]);
        await destinations.Create(client.Id, "Movies", "movies");
        await destinations.Create(client.Id, "Movies 4K", "movies-4k");
        var otherDestination = await destinations.Create(other.Id);

        //Act
        await db.IntegrationConnections.Where(x => x.Id == client.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        var mappings = await db.PathMappings.AsNoTracking().ToListAsync(cancellationToken);
        var remainingDestinations = await db.DestinationMappings.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal(other.Id, Assert.Single(mappings).IntegrationConnectionId);
        Assert.Equal(otherDestination.Id, Assert.Single(remainingDestinations).Id);
    }

    [Fact]
    public async Task Kind_is_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await Integrations(db).Create("qBittorrent", "qbittorrent", IntegrationKind.DownloadClient);

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT \"Kind\" AS \"Value\" FROM \"IntegrationConnections\"")
            .SingleAsync(cancellationToken);

        //Assert
        // By name, so a member added or reordered later never changes what a stored row means.
        Assert.Equal("DownloadClient", stored);
    }

    private static IIntegrationConnectionDataFactory Integrations(ApplicationDbContext db) =>
        new IntegrationConnectionDataFactory(db, Encryption, TimeProvider.System);
}
