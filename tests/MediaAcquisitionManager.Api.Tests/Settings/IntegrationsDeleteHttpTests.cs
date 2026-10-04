using System.Net;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Removing an integration: its own settings go with it, while what it reported and the download jobs
// sent to it stay with its name (ADR 0006), and its audit row keeps the name too — except a media
// library's snapshot, which goes with it (MAM-8), its sync runs staying as history.
public sealed class IntegrationsDeleteHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";

    /// <summary>A second fake Jellyfin, beside the one at <see cref="FakeJellyfinServer.DefaultBaseUrl"/>.</summary>
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";

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
    public async Task Delete_of_a_media_library_purges_its_snapshot_and_keeps_its_sync_runs_with_its_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var removed = await _integrations.Create("jellyfin", "Living room", apiKey: "stored-api-key-1234");
        var kept = await _integrations.Create("jellyfin", "Bedroom", SecondJellyfinAddress, apiKey: "stored-api-key-5678");
        // Two servers with the same four sample movies, so the two snapshots are alike in all but their
        // source: whatever stays after the removal can only be the other library's.
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, new FakeJellyfinServer(), new FakeJellyfinServer(SecondJellyfinAddress));
        var snapshots = new LibrarySnapshotSeeder(_factory.Scope);
        var removedRun = await snapshots.Sync(removed, _owner.User);
        var keptRun = await snapshots.Sync(kept, _owner.User);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{removed.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            // The other library's snapshot, whole: three works (the two "Чужі" items are one), seven
            // titles, seven identifiers, four versions, four files and five libraries — every row its own.
            // The removed library's works went with their facts, as nothing else held them.
            var works = await db.MediaWorks.Select(work => work.Id).ToListAsync(cancellationToken);
            var titles = await db.MediaWorkTitles.ToListAsync(cancellationToken);
            var identifiers = await db.MediaWorkIdentifiers.ToListAsync(cancellationToken);
            var versions = await db.MediaVersions.ToListAsync(cancellationToken);
            var files = await db.PhysicalFiles.ToListAsync(cancellationToken);
            var locations = await db.LibraryLocations.ToListAsync(cancellationToken);
            Assert.Equal(3, works.Count);
            Assert.Equal(works.Order(), titles.Select(title => title.MediaWorkId).Distinct().Order());
            Assert.Equal((7, 7, 4, 4, 5), (titles.Count, identifiers.Count, versions.Count, files.Count, locations.Count));
            Assert.All(
                [
                    .. titles.Select(title => title.SourceConnectionId),
                    .. identifiers.Select(identifier => identifier.SourceConnectionId),
                    .. versions.Select(version => version.SourceConnectionId),
                    .. files.Select(file => file.SourceConnectionId),
                    .. locations.Select(location => location.SourceConnectionId),
                ],
                source => Assert.Equal(kept.Id, source));
            // The runs are history: the removed library's stays, with its name and no link, and still
            // says what it stored before the removal.
            var removedRow = await db.LibrarySyncRuns.SingleAsync(run => run.Id == removedRun.Id, cancellationToken);
            Assert.Null(removedRow.IntegrationConnectionId);
            Assert.Equal("Living room", removedRow.IntegrationName);
            Assert.Equal(LibrarySyncStatus.Succeeded, removedRow.Status);
            Assert.Equal(3, removedRow.WorksCount);
            Assert.Equal(kept.Id, (await db.LibrarySyncRuns.SingleAsync(run => run.Id == keptRun.Id, cancellationToken)).IntegrationConnectionId);
        });
    }

    [Fact]
    public async Task Delete_of_a_media_library_purges_its_series_episodes_and_their_positions()
    {
        //Arrange
        // MAM-32 — two servers with the same sample series and episodes: whatever stays is the other's.
        var cancellationToken = TestContext.Current.CancellationToken;
        var removed = await _integrations.Create("jellyfin", "Living room", apiKey: "stored-api-key-1234");
        var kept = await _integrations.Create("jellyfin", "Bedroom", SecondJellyfinAddress, apiKey: "stored-api-key-5678");
        var servers = new[] { new FakeJellyfinServer(), new FakeJellyfinServer(SecondJellyfinAddress) };
        foreach (var server in servers)
        {
            server.Movies.Clear();
            server.Series.AddRange(JellyfinSamples.Series());
            server.Episodes.AddRange(JellyfinSamples.Episodes());
        }

        FakeJellyfinServer.Arm(_factory.JellyfinHttp, servers);
        var snapshots = new LibrarySnapshotSeeder(_factory.Scope);
        await snapshots.Sync(removed, _owner.User);
        await snapshots.Sync(kept, _owner.User);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{removed.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            // The other library's 5 series and 4 episodes with their 5 positions, and nothing of the removed
            // one's: its series and episodes went with their facts.
            var works = await db.MediaWorks.ToListAsync(cancellationToken);
            var positions = await db.MediaEpisodePositions.ToListAsync(cancellationToken);
            Assert.Equal((5, 4), (works.Count(work => work.Kind == MediaWorkKind.Series), works.Count(work => work.Kind == MediaWorkKind.Episode)));
            Assert.Equal(5, positions.Count);
            Assert.All(positions, position => Assert.Equal(kept.Id, position.SourceConnectionId));
            var workIds = works.Select(work => work.Id).ToHashSet();
            Assert.All(positions, position =>
            {
                Assert.Contains(position.MediaWorkId, workIds);
                Assert.True(position.SeriesWorkId is null || workIds.Contains(position.SeriesWorkId.Value));
            });
            Assert.All(await db.MediaWorkTitles.ToListAsync(cancellationToken), title => Assert.Equal(kept.Id, title.SourceConnectionId));
        });
    }

    [Fact]
    public async Task Delete_of_a_release_source_keeps_its_releases_with_the_source_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await _integrations.Create("torznab", "Toloka", apiKey: "stored-api-key-1234");
        var release = await new ReleaseSeeder(_factory.Scope).Create(search);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{search.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            // ADR 0006 — only a library's snapshot is purged (MAM-8). What a search source listed stays:
            // who said so still reads after the integration is gone; only the link to it is cut.
            var row = await db.Releases.SingleAsync(cancellationToken);
            Assert.Equal(release.Id, row.Id);
            Assert.Null(row.SourceConnectionId);
            Assert.Equal("Toloka", row.SourceName);
            Assert.Equal(ProvenanceSourceKind.ReleaseSearch, row.SourceKind);
            Assert.Equal(release.Title, row.Title);
        });
    }

    [Fact]
    public async Task Delete_of_a_release_source_removes_its_sections_and_keeps_their_destinations()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", apiKey: "stored-api-key-1234");
        var other = await _integrations.Create("torznab", "Other", apiKey: "stored-api-key-5678");
        var client = await _integrations.Create("qbittorrent", "Downloads", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        var cartoons = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Cartoons", "Cartoons", isDefault: true);
        var sections = new DestinationSectionSeeder(_factory.Scope);
        await sections.Map(cartoons, toloka, "Мультсеріали", true, "100125", "100044");
        var kept = await sections.Map(cartoons, other, "Мультсеріали", true, "100125");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{toloka.Id}", _owner.AccessToken);

        //Assert
        // MAM-32 — a removed source lists nothing any more, so its sections say nothing (ADR 0020): they go
        // with it, and the destination they sent releases to stays, with the other source's section.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal([kept[0].Id], await db.DestinationSections.Select(section => section.Id).ToListAsync(cancellationToken));
            Assert.Equal([cartoons.Id], await db.DestinationMappings.Select(destination => destination.Id).ToListAsync(cancellationToken));
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
