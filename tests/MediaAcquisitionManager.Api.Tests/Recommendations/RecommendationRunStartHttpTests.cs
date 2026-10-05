using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — asking for a computation of the recommendations (POST /api/recommendations/runs) and reading the latest one
// (GET /api/recommendations/runs/latest). Asking is the owner's alone and only queues a run (202 + run row,
// Queue/Worker): nothing is asked of the catalog before the worker takes it. At most one run waits at a time, so a
// request that finds one queued joins it, whoever queued it, while a run under way gets a new one behind it. Without a
// connected metadata catalog nothing is written; a full queue keeps its run as the failed attempt. The latest run is
// read by every signed-in user — the running one first — its requester and error by the owner only.
public sealed class RecommendationRunStartHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations/runs";
    private const string LatestUrl = "/api/recommendations/runs/latest";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string NoCatalogError = "Connect a metadata catalog under Integrations first.";
    private const string QueueFullError = "Too many recommendation runs are waiting. Try again in a minute.";
    private const string FailedError = "TMDb refused the key (401 Unauthorized).";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IRecommendationRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public RecommendationRunStartHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _runs = new RecommendationRunSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task StartRun_as_the_owner_is_202_with_the_queued_run_and_asks_the_catalog_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        FakeTmdbServer.Arm(_factory.TmdbHttp);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(LatestUrl, response.Headers.Location!.AbsolutePath);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.Equal(
            new RecommendationRunDto(run.Id, RecommendationRunStatus.Queued, null, null, null, RecommendationRunTrigger.Requested, "owner", now, null, null, null, null, null),
            run);
        // Only queued: the worker takes it, and the catalog hasn't been asked anything yet.
        Assert.Equal([run.Id], DrainQueue());
        Assert.Empty(_factory.TmdbHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.RecommendationRuns.SingleAsync(cancellationToken);
            Assert.Equal((run.Id, RecommendationRunTrigger.Requested, RecommendationRunStatus.Queued), (row.Id, row.Trigger, row.Status));
            Assert.Equal((_owner.User.Id, "owner"), (row.RequestedByUserId, row.RequestedByUserName));
            Assert.Equal((now, null, null), (row.CreatedUtc, row.StartedUtc, row.FinishedUtc));
            // The run row records who asked; a request writes no audit row of its own, like a sync's.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task StartRun_twice_answers_both_with_the_one_queued_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var first = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        var second = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The waiting run reads the library and the catalog when it starts, so a second would only compute the same.
        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (first.StatusCode, second.StatusCode));
        var firstRun = await ReadRunAsync(first, cancellationToken);
        Assert.Equal(firstRun, await ReadRunAsync(second, cancellationToken));
        Assert.Equal([firstRun.Id], DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(firstRun.Id, (await db.RecommendationRuns.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task StartRun_while_a_run_queued_by_a_sync_waits_is_202_with_that_run_as_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var queuedAt = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1);
        var waiting = await _runs.Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced, createdUtc: queuedAt);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Joined as it is: still the sync's, with nobody named as having asked for it.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            new RecommendationRunDto(waiting.Id, RecommendationRunStatus.Queued, null, null, null, RecommendationRunTrigger.LibrarySynced, null, queuedAt, null, null, null, null, null),
            await ReadRunAsync(response, cancellationToken));
        // Whoever queued it queued its id; joining adds no second one.
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.RecommendationRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task StartRun_while_a_run_is_running_queues_a_new_run_behind_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var running = await _runs.Create(RecommendationRunStatus.Running, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The running one read the library when it started; what a sync added since is only read by a run behind it.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.NotEqual(running.Id, run.Id);
        Assert.Equal([run.Id], DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.RecommendationRuns.ToDictionaryAsync(row => row.Id, cancellationToken);
            Assert.Equal(RecommendationRunStatus.Running, rows[running.Id].Status);
            Assert.Equal(RecommendationRunStatus.Queued, rows[run.Id].Status);
        });
    }

    [Fact]
    public async Task StartRun_without_a_token_is_401_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task StartRun_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // A computation asks the owner's catalog with the owner's key: everyone reads the lists, the owner recomputes them.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartRun_without_a_connected_catalog_is_409_with_the_sentence_and_queues_nothing(bool switchedOff)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        if (switchedOff)
            await ConnectCatalogAsync(isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // A switched-off catalog is no catalog: nothing could be computed, so the owner is told where to connect one.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoCatalogError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task StartRun_with_the_queue_full_is_503_and_keeps_the_run_as_failed_with_the_same_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var queue = _factory.Resolve<RecommendationRunQueue>();
        for (var index = 0; index < RecommendationRunQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(QueueFullError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // Failed, not left queued: a queued run no worker will take would be joined by every later request.
            var row = await db.RecommendationRuns.SingleAsync(cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, QueueFullError, now), (row.Status, row.Error, row.FinishedUtc));
            Assert.Equal((RecommendationRunTrigger.Requested, "owner"), (row.Trigger, row.RequestedByUserName));
        });
        // Warning, so it ships off the box: a full queue means the worker is stuck.
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(RecommendationRunQueue).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task StartRun_when_a_sync_queues_its_run_first_is_202_with_that_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        RecommendationRunEntity? competing = null;
        // Deterministic race: both found nothing queued, and a sync finishing as the owner clicks inserts its run just
        // before this request's — through a context of its own, like the sync's runner.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<RecommendationRunEntity>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                competing = await new RecommendationRunSeeder(scope.ServiceProvider).Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced);
            });

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The partial unique index keeps one queued run, and the loser joins it: a 202, not a 500.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(competing);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.Equal((competing.Id, RecommendationRunTrigger.LibrarySynced), (run.Id, run.Trigger));
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(competing.Id, (await db.RecommendationRuns.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task LatestRun_before_any_run_is_204_with_no_body()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task LatestRun_as_the_owner_is_200_with_who_asked_the_counts_and_the_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var failed = await _runs.Create(RecommendationRunStatus.Failed, requestedBy: _owner.User, createdUtc: at, error: FailedError);

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new RecommendationRunDto(failed.Id, RecommendationRunStatus.Failed, null, null, null, RecommendationRunTrigger.Requested, "owner", at, at, at, null, null, FailedError),
            await ReadRunAsync(response, cancellationToken));
    }

    [Fact]
    public async Task LatestRun_as_a_signed_in_non_owner_is_200_without_who_asked_or_the_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var failed = await _runs.Create(RecommendationRunStatus.Failed, requestedBy: _owner.User, createdUtc: at, error: FailedError);

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl, _member.AccessToken);

        //Assert
        // Everyone reads the lists, so everyone sees where their computation stands; who asked and why it failed — the
        // catalog's sentence can name its address — stay the owner's.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("owner", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", body, StringComparison.Ordinal);
        Assert.Equal(
            new RecommendationRunDto(failed.Id, RecommendationRunStatus.Failed, null, null, null, RecommendationRunTrigger.Requested, null, at, at, at, null, null, null),
            await ReadRunAsync(response, cancellationToken));
    }

    [Fact]
    public async Task LatestRun_gives_the_succeeded_runs_counts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-3);
        var succeeded = await _runs.Create(
            RecommendationRunStatus.Succeeded, RecommendationRunTrigger.LibrarySynced, createdUtc: at, seedCount: 870, candidateCount: 412);

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl, _member.AccessToken);

        //Assert
        Assert.Equal(
            new RecommendationRunDto(succeeded.Id, RecommendationRunStatus.Succeeded, null, null, null, RecommendationRunTrigger.LibrarySynced, null, at, at, at, 870, 412, null),
            await ReadRunAsync(response, cancellationToken));
    }

    [Fact]
    public async Task LatestRun_is_the_running_run_over_a_newer_queued_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _runs.Create(RecommendationRunStatus.Succeeded, createdUtc: now.AddMinutes(-30), seedCount: 3, candidateCount: 9);
        var running = await _runs.Create(RecommendationRunStatus.Running, createdUtc: now.AddMinutes(-5));
        await _runs.Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced, createdUtc: now.AddMinutes(-1));

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl, _owner.AccessToken);

        //Assert
        // The page shows what is happening now; the queued one only means "and again after this".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.Equal((running.Id, RecommendationRunStatus.Running), (run.Id, run.Status));
    }

    [Fact]
    public async Task LatestRun_without_a_running_run_is_the_newest_by_when_it_was_queued_a_queued_one_included()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _runs.Create(RecommendationRunStatus.Succeeded, createdUtc: now.AddMinutes(-30), seedCount: 3, candidateCount: 9);
        var queued = await _runs.Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.CatalogConnected, createdUtc: now.AddMinutes(-1));

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.Equal((queued.Id, RecommendationRunStatus.Queued, RecommendationRunTrigger.CatalogConnected), (run.Id, run.Status, run.Trigger));
    }

    [Fact]
    public async Task LatestRun_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _runs.Create(RecommendationRunStatus.Succeeded);

        //Act
        var response = await _factory.GetHttpRequest(LatestUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync(bool isEnabled = true) =>
        new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token, isEnabled: isEnabled);

    private async Task AssertNothingQueuedAsync(CancellationToken cancellationToken)
    {
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
    }

    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<RecommendationRunQueue>();
        var ids = new List<Guid>();
        while (queue.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }

    private static async Task<RecommendationRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<RecommendationRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
