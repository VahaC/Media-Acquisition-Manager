using System.Net;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11 — removing a destination: the row and its audit row in one save, nothing sent to the client (its
// category stays), the other destinations and the download jobs sent to it kept with its name. Removing the
// default is DestinationsDefaultHttpTests.
public sealed class DestinationsDeleteHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;

    public DestinationsDeleteHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        new FakeQBittorrentServer().Arm(_factory.QBittorrentHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Delete_is_204_removes_only_that_destination_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client, "Movies", "Movies");
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal([cartoons.Id], await db.DestinationMappings.Select(destination => destination.Id).ToListAsync(cancellationToken)));
        // The category stays in the client: removing a destination only stops the app sending to it.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Delete_records_a_destination_deleted_audit_row_with_its_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client, "Movies", "Movies");

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationDeleted, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.Destination, audit.TargetKind);
            Assert.Equal(movies.Id, audit.TargetId);
            // Copied: it says what was removed after the row is gone.
            Assert.Equal("Movies", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Delete_keeps_the_download_jobs_sent_to_it_with_its_name_and_category()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var job = await new DownloadJobSeeder(_factory.Scope).Create(client);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{job.DestinationMappingId}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.SingleAsync(cancellationToken);
            Assert.Equal(job.Id, row.Id);
            // History outlives the destination: the job still says where it went and what was sent.
            Assert.Null(row.DestinationMappingId);
            Assert.Equal(job.DestinationName, row.DestinationName);
            Assert.Equal(job.ClientDestinationKey, row.ClientDestinationKey);
            Assert.Equal(client.Id, row.DownloadClientConnectionId);
        });
    }

    [Fact]
    public async Task Delete_of_a_destination_that_does_not_exist_is_a_bare_404_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DestinationMappings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Delete_twice_is_204_then_a_bare_404_with_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client);

        //Act
        var first = await _factory.DeleteHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);
        var second = await _factory.DeleteHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Delete_twice_at_once_is_204_and_a_bare_404_with_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client);
        var url = $"{BaseUrl}/{movies.Id}";
        HttpResponseMessage? competing = null;
        // Deterministic double click: this delete read the row and recorded its removal, and the other
        // one, sent just before this save, removes it and records it first.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Deleted),
            async _ => competing = await _factory.DeleteHttpRequest(url, _owner.AccessToken));

        //Act
        var response = await _factory.DeleteHttpRequest(url, _owner.AccessToken);

        //Assert
        // Gone either way: the loser is the same bare 404 as a destination that was never there, not a
        // 500, and only the request that removed it recorded it.
        Assert.Equal(HttpStatusCode.NoContent, competing?.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DestinationMappings.CountAsync(cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    private Task<IntegrationConnectionEntity> CreateClientAsync() =>
        _integrations.Create(
            "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword);
}
