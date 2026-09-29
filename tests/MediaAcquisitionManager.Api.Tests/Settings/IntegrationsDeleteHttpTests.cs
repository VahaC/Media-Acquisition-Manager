using System.Net;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Removing an integration: its own settings go with it, while what it reported and the download jobs
// sent to it stay with its name (ADR 0006), and its audit row keeps the name too.
public sealed class IntegrationsDeleteHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsDeleteHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Delete_is_204_and_removes_the_integration_with_its_path_and_destination_mappings()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var downloads = await _integrations.Create(
            "qbittorrent", "Downloads", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        // The job brings a destination mapping of the client with it.
        await new DownloadJobSeeder(_factory.Scope).Create(downloads);
        var other = await _integrations.Create(
            "qbittorrent", "Other downloads", pathMappings: [new PathMappingPair("/downloads", "/data")]);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{downloads.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal([other.Id], await db.IntegrationConnections.Select(x => x.Id).ToListAsync(cancellationToken));
            // Only the removed integration's mappings go.
            Assert.Equal(
                [other.PathMappings[0].Id],
                await db.PathMappings.Select(x => x.Id).ToListAsync(cancellationToken));
            Assert.Equal(0, await db.DestinationMappings.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Delete_records_an_integration_deleted_audit_row_that_keeps_the_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("jellyfin", "Living room", apiKey: "stored-api-key-1234");
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.IntegrationDeleted, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.Integration, audit.TargetKind);
            Assert.Equal(integration.Id, audit.TargetId);
            Assert.Equal("Living room", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Delete_of_a_download_client_keeps_its_download_jobs_with_the_client_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var downloads = await _integrations.Create("qbittorrent", "Downloads");
        var job = await new DownloadJobSeeder(_factory.Scope).Create(downloads);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{downloads.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.SingleAsync(cancellationToken);
            Assert.Equal(job.Id, row.Id);
            Assert.Null(row.DownloadClientConnectionId);
            Assert.Equal("Downloads", row.DownloadClientName);
            // Its destination went with the client; the job still says what it was and what was sent.
            Assert.Null(row.DestinationMappingId);
            Assert.Equal(job.DestinationName, row.DestinationName);
            Assert.Equal(job.ClientDestinationKey, row.ClientDestinationKey);
            Assert.Equal(job.ReleaseId, row.ReleaseId);
        });
    }

    [Fact]
    public async Task Delete_keeps_the_facts_it_reported_with_their_source_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create("jellyfin", "Living room", apiKey: "stored-api-key-1234");
        var work = await new MediaWorkSeeder(_factory.Scope).Create(library);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{library.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(work.Id, (await db.MediaWorks.SingleAsync(cancellationToken)).Id);
            var title = await db.MediaWorkTitles.SingleAsync(cancellationToken);
            var identifier = await db.MediaWorkIdentifiers.SingleAsync(cancellationToken);
            var version = await db.MediaVersions.SingleAsync(cancellationToken);
            var file = await db.PhysicalFiles.SingleAsync(cancellationToken);
            // Who said so still reads after the integration is gone; only the link to it is cut.
            Assert.All(
                [
                    (title.SourceConnectionId, title.SourceName, title.SourceItemId),
                    (identifier.SourceConnectionId, identifier.SourceName, identifier.SourceItemId),
                    (version.SourceConnectionId, version.SourceName, version.SourceItemId),
                ],
                provenance => Assert.Equal((null, "Living room", MediaWorkSeeder.SourceItemId), provenance));
            Assert.Null(file.SourceConnectionId);
            Assert.Equal("Living room", file.SourceName);
            Assert.Equal("Чужі", title.Text);
            Assert.Equal("679", identifier.Value);
        });
    }

    [Fact]
    public async Task Delete_of_an_integration_of_an_unregistered_type_is_204()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var retired = await _integrations.Create("retired-service", "Old library", apiKey: "stored-api-key-1234");

        //Act
        // Removing is all an integration this build can't connect to is good for.
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{retired.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.Action == AuditAction.IntegrationDeleted, cancellationToken));
        });
    }

    [Fact]
    public async Task Delete_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create(name: "Living room");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Delete_twice_at_once_deletes_once_and_records_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room");
        var url = $"{BaseUrl}/{integration.Id}";

        //Act
        // A double click: both requests may read the row, but only one may remove it and record it.
        var responses = await Task.WhenAll(
            _factory.DeleteHttpRequest(url, _owner.AccessToken),
            _factory.DeleteHttpRequest(url, _owner.AccessToken));

        //Assert
        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.NotFound],
            responses.Select(response => response.StatusCode).Order().ToArray());
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }
}
