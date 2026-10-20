using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — what queues a computation of the recommendations besides the owner's Recompute now: a library sync that
// succeeded (after the inspection it queues, and only with a metadata catalog connected), and a metadata integration
// created enabled or switched back on (POST and PUT /api/settings/integrations). A trigger finding a run queued joins
// it; a full queue never fails the save it follows; the queuing asks the catalog nothing and writes no audit row.
public sealed class RecommendationTriggerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string IntegrationsUrl = "/api/settings/integrations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string QueueFullError = "Too many recommendation runs are waiting. Try again in a minute.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public RecommendationTriggerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        FakeTmdbServer.Arm(_factory.TmdbHttp);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_that_succeeds_with_a_catalog_connected_queues_a_library_synced_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        // A media folder allowed, so the sync queues its inspection too.
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        var sync = await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Assert
        // What the library holds may have changed: a run is queued by the app, with nobody named as having asked.
        Assert.Equal(LibrarySyncStatus.Succeeded, sync.Status);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced, (Guid?)null, (string?)null),
                (run.Status, run.Trigger, run.RequestedByUserId, run.RequestedByUserName));
            Assert.Equal([run.Id], DrainQueue());
            // Beside the inspection the sync queues, never instead of it.
            var inspection = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((FileInspectionRunStatus.Queued, FileInspectionTrigger.LibrarySynced), (inspection.Status, inspection.Trigger));
        });
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Sync_that_succeeds_without_a_catalog_queues_no_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        var sync = await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);

        //Assert
        // The default is no catalog: nothing could be computed, so nothing is queued to fail.
        Assert.Equal(LibrarySyncStatus.Succeeded, sync.Status);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task Sync_that_fails_queues_no_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: "a-key-jellyfin-refuses-0000000000");

        //Act
        var sync = await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);

        //Assert
        Assert.Equal(LibrarySyncStatus.Failed, sync.Status);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sync_while_a_run_waits_joins_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var waiting = await new RecommendationRunSeeder(_factory.Scope).Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.Requested, _owner.User);
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);

        //Assert
        // The waiting run reads the library when it starts, the synced one included.
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, RecommendationRunTrigger.Requested), (run.Id, run.Trigger));
        });
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task Create_of_an_enabled_catalog_queues_a_catalog_connected_run_asks_it_nothing_and_audits_only_the_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, CreateCatalogRequest(isEnabled: true));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (RecommendationRunStatus.Queued, RecommendationRunTrigger.CatalogConnected, (string?)null),
                (run.Status, run.Trigger, run.RequestedByUserName));
            Assert.Equal([run.Id], DrainQueue());
            // The integration's own audit row, and none for the queuing: the run row records it.
            var audit = Assert.Single(await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken));
            Assert.Equal(AuditAction.IntegrationCreated, audit.Action);
        });
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Create_of_a_switched_off_catalog_queues_no_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, CreateCatalogRequest(isEnabled: false));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task Create_of_an_integration_of_another_kind_queues_no_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "jellyfin", "Living room", "http://127.0.0.1:9", ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, JellyfinApiKey)));

        //Assert
        // A library added says nothing until it is synced, and its sync queues a run then.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Create_of_an_enabled_catalog_with_the_queue_full_is_still_201_and_keeps_the_run_as_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = _factory.Resolve<RecommendationRunQueue>();
        for (var index = 0; index < RecommendationRunQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());

        //Act
        var response = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, CreateCatalogRequest(isEnabled: true));

        //Assert
        // The save is what the owner asked for, and it happened; a full queue is the computation's problem, not the save's.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.IntegrationConnections.CountAsync(cancellationToken));
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, QueueFullError), (run.Status, TestTexts.En(run.Error)));
        });
    }

    [Fact]
    public async Task Update_that_switches_a_catalog_back_on_queues_a_catalog_connected_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync(isEnabled: false);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{IntegrationsUrl}/{catalog.Id}", _owner.AccessToken, new UpdateIntegrationRequest("TMDb", FakeTmdbServer.BaseUrl, IsEnabled: true));

        //Assert
        Assert.True(response.IsSuccessStatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((RecommendationRunStatus.Queued, RecommendationRunTrigger.CatalogConnected), (run.Status, run.Trigger));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_that_leaves_the_switch_as_it_was_queues_no_run(bool isEnabled)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync(isEnabled);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{IntegrationsUrl}/{catalog.Id}", _owner.AccessToken, new UpdateIntegrationRequest("TMDb renamed", FakeTmdbServer.BaseUrl, IsEnabled: isEnabled));

        //Assert
        // A rename of a catalog already on changes nothing it would answer; one left off has nothing to answer.
        Assert.True(response.IsSuccessStatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync(bool isEnabled = true) =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token, isEnabled: isEnabled);

    private static CreateIntegrationRequest CreateCatalogRequest(bool isEnabled) =>
        new("tmdb", "TMDb", FakeTmdbServer.BaseUrl, ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, FakeTmdbServer.Token), IsEnabled: isEnabled);

    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<RecommendationRunQueue>();
        var ids = new List<Guid>();
        while (queue.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }
}
