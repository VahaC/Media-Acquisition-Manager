using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 — what queues a preparation of the TMDb suggestions (a successful library sync, a catalog connected or switched
// back on, the owner's "Prepare again") and what the TMDb page reads of it (GET runs/latest). The RecommendationRun shape:
// nothing is queued without a catalog, queuing asks the catalog nothing, a trigger that finds a run waiting joins it, and
// the owner joining a run a sync queued makes it search every work again (R21).
public sealed class LibraryLinkSuggestionRunHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string RunsUrl = "/api/library/tmdb/runs";
    private const string IntegrationsUrl = "/api/settings/integrations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string NoCatalogError = "Connect a metadata catalog under Integrations first.";
    private const string QueueFullError = "Too many TMDb suggestion runs are waiting. Try again in a minute.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryLinkSuggestionRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;

    public LibraryLinkSuggestionRunHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _runs = new LibraryLinkSuggestionRunSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        FakeTmdbServer.Arm(_factory.TmdbHttp);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_with_a_catalog_queues_a_library_synced_run_and_asks_the_catalog_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Guid runId = default;
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.LibrarySynced, (string?)null, (Guid?)null, now),
                (run.Status, run.Trigger, run.RequestedByUserName, run.RequestedByUserId, run.CreatedUtc));
            runId = run.Id;
            // Queuing touches no suggestion.
            Assert.Equal(0, await db.LibraryLinkSearches.CountAsync(cancellationToken));
        });
        Assert.Equal([runId], DrainQueue());
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Sync_without_a_catalog_queues_no_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken)));
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task Sync_joins_the_run_already_waiting()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var waiting = await _runs.Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.CatalogConnected);
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);

        //Assert
        // The waiting run reads the library when it starts, the synced works included.
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, LibraryLinkSuggestionRunTrigger.CatalogConnected), (run.Id, run.Trigger));
        });
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task Create_of_an_enabled_catalog_queues_a_catalog_connected_run_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, CreateCatalogRequest(isEnabled: true));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.CatalogConnected), (run.Status, run.Trigger));
        });
        Assert.Single(DrainQueue());
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Create_of_a_catalog_switched_off_queues_no_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(IntegrationsUrl, _owner.AccessToken, CreateCatalogRequest(isEnabled: false));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken)));
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
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(LibraryLinkSuggestionRunTrigger.CatalogConnected, run.Trigger);
        });
    }

    [Fact]
    public async Task Start_as_the_owner_is_202_with_a_requested_run_and_asks_the_catalog_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(RunsUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<LibraryLinkSuggestionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            new LibraryLinkSuggestionRunDto(run!.Id, LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.Requested, "owner", now, null, null, null, 0, null),
            run);
        Assert.Equal([run.Id], DrainQueue());
        Assert.Empty(_factory.TmdbHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(((Guid?)_owner.User.Id, "owner"), (row.RequestedByUserId, row.RequestedByUserName));
            // The run row records who asked; queuing writes no audit row of its own.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Start_joins_a_run_a_sync_queued_which_then_searches_every_work_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var waiting = await _runs.Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.LibrarySynced);

        //Act
        var response = await _factory.PostHttpRequest(RunsUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<LibraryLinkSuggestionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal((waiting.Id, LibraryLinkSuggestionRunTrigger.Requested, (string?)"owner"), (run!.Id, run.Trigger, run.RequestedByUserName));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, LibraryLinkSuggestionRunTrigger.Requested, (Guid?)_owner.User.Id), (row.Id, row.Trigger, row.RequestedByUserId));
        });
    }

    [Fact]
    public async Task Start_beside_a_running_run_queues_one_behind_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var running = await _runs.Create(LibraryLinkSuggestionRunStatus.Running, LibraryLinkSuggestionRunTrigger.LibrarySynced);

        //Act
        var response = await _factory.PostHttpRequest(RunsUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var runs = await db.LibraryLinkSuggestionRuns.AsNoTracking().OrderBy(run => run.Status).ToListAsync(cancellationToken);
            Assert.Equal(
                [(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.Requested), (LibraryLinkSuggestionRunStatus.Running, LibraryLinkSuggestionRunTrigger.LibrarySynced)],
                runs.Select(run => (run.Status, run.Trigger)));
            Assert.Contains(runs, run => run.Id == running.Id);
        });
    }

    [Fact]
    public async Task Start_without_a_catalog_is_409_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(RunsUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoCatalogError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Start_when_the_queue_is_full_is_503_and_the_run_stays_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var queue = _factory.Resolve<LibraryLinkSuggestionRunQueue>();
        for (var index = 0; index < LibraryLinkSuggestionRunQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());

        //Act
        var response = await _factory.PostHttpRequest(RunsUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(QueueFullError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((LibraryLinkSuggestionRunStatus.Failed, QueueFullError), (run.Status, TestTexts.En(run.Error)));
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibraryLinkSuggestionRunQueue).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task Latest_before_the_first_run_is_204()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest($"{RunsUrl}/latest", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Latest_is_the_running_run_before_a_newer_queued_one_with_its_error_and_requester()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _runs.Create(LibraryLinkSuggestionRunStatus.Failed, createdUtc: now.AddMinutes(-30), error: Message.Legacy("TMDb answered 500."));
        var running = await _runs.Create(
            LibraryLinkSuggestionRunStatus.Running, requestedBy: _owner.User, createdUtc: now.AddMinutes(-10), worksTotal: 12, worksDone: 5);
        await _runs.Create(LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.LibrarySynced, createdUtc: now);

        //Act
        var response = await _factory.GetHttpRequest($"{RunsUrl}/latest", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<LibraryLinkSuggestionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            (running.Id, LibraryLinkSuggestionRunStatus.Running, (string?)"owner", (int?)12, 5),
            (run!.Id, run.Status, run.RequestedByUserName, run.WorksTotal, run.WorksDone));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync(bool isEnabled = true) =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token, isEnabled: isEnabled);

    private static CreateIntegrationRequest CreateCatalogRequest(bool isEnabled) =>
        new("tmdb", "TMDb", FakeTmdbServer.BaseUrl, ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, FakeTmdbServer.Token), IsEnabled: isEnabled);

    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<LibraryLinkSuggestionRunQueue>();
        var ids = new List<Guid>();
        while (queue.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }
}
