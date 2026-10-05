using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-33 — a Profile save that switches on a tab no other user who can sign in has on queues a computation of the
// recommendations (TabEnabled, the saver named): the last run computed none of that tab's works. A tab another user has
// on — a user who never saved has every tab on — or a save that switches nothing on queues nothing, nor does one without
// a catalog; a waiting run is joined; and a full queue never fails the save.
public sealed class RecommendationSettingsTriggerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/profile/recommendations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IUserRecommendationSettingsSeeder _settings = null!;
    private AuthorizedUser _anna = null!;
    private IntegrationConnectionEntity _tmdb = null!;

    public RecommendationSettingsTriggerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _settings = new UserRecommendationSettingsSeeder(_factory.Scope);
        _anna = await _users.CreateAuthorized("anna");
        await _settings.Save(_anna.User, [RecommendationTab.Movies]);
        _tmdb = await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Update_switching_on_a_tab_nobody_else_has_on_queues_a_tab_enabled_run_in_the_savers_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bohdan = await _users.Create("bohdan");
        await _settings.Save(bohdan, [RecommendationTab.Movies, RecommendationTab.Series]);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await SaveAsync(movies: true, cartoons: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.SingleAsync(cancellationToken);
            Assert.Equal(
                (RecommendationRunStatus.Queued, RecommendationRunTrigger.TabEnabled, (Guid?)_anna.User.Id, "anna", now),
                (run.Status, run.Trigger, run.RequestedByUserId, run.RequestedByUserName, run.CreatedUtc));
            Assert.Equal([run.Id], DrainQueue());
            Assert.True((await db.UserRecommendationSettings.SingleAsync(row => row.UserId == _anna.User.Id, cancellationToken)).CartoonsEnabled);
            // The run row records who switched the tab on; the save writes no audit row.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_switching_on_a_tab_a_user_who_never_saved_has_on_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // No row: every tab on (Vasyl's decision of 2026-10-05), so the last run computed Cartoons for him.
        await _users.Create("bohdan");

        //Act
        var response = await SaveAsync(movies: true, cartoons: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoRunAsync(cancellationToken);
    }

    [Fact]
    public async Task Update_switching_on_a_tab_only_a_disabled_user_has_on_queues_a_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A disabled user sees no list, so their tabs asked nothing of the catalog.
        await _users.Create("bohdan", isDisabled: true);

        //Act
        var response = await SaveAsync(movies: true, documentaries: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(RecommendationRunTrigger.TabEnabled, (await db.RecommendationRuns.SingleAsync(cancellationToken)).Trigger));
        Assert.Single(DrainQueue());
    }

    [Fact]
    public async Task Update_that_switches_nothing_on_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // Movies stays on; switching every other tab off computes nothing new.
        var response = await SaveAsync(movies: true, dailySearchLimit: 10);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoRunAsync(cancellationToken);
    }

    [Fact]
    public async Task Update_of_a_user_who_never_saved_switches_nothing_on_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bohdan = await _users.CreateAuthorized("bohdan");

        //Act
        // His tabs were all on before he saved: the defaults.
        var response = await _factory.PutHttpRequest(BaseUrl, bohdan.AccessToken, new RecommendationSettingsRequest(true, true, true, true, 60));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoRunAsync(cancellationToken);
    }

    [Fact]
    public async Task Update_switching_a_tab_on_without_a_catalog_saves_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.WithDbAsync(db => db.IntegrationConnections.Where(row => row.Id == _tmdb.Id).ExecuteDeleteAsync(cancellationToken));

        //Act
        var response = await SaveAsync(movies: true, series: true);

        //Assert
        // Nothing to compute from: quietly nothing, as after a sync.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.True((await db.UserRecommendationSettings.SingleAsync(cancellationToken)).SeriesEnabled));
        await AssertNoRunAsync(cancellationToken);
    }

    [Fact]
    public async Task Update_switching_a_tab_on_joins_the_run_already_waiting()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var waiting = await new RecommendationRunSeeder(_factory.Scope).Create(RecommendationRunStatus.Queued, RecommendationRunTrigger.LibrarySynced);

        //Act
        var response = await SaveAsync(movies: true, series: true);

        //Assert
        // The waiting run reads every user's tabs when it starts, so it computes the new one too.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, RecommendationRunTrigger.LibrarySynced), (run.Id, run.Trigger));
        });
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task Update_switching_a_tab_on_with_the_queue_full_still_saves()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = _factory.Resolve<RecommendationRunQueue>();
        for (var index = 0; index < RecommendationRunQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());

        //Act
        var response = await SaveAsync(movies: true, series: true);

        //Assert
        // The save stands; the attempt stays as a failed run, with the queue's sentence.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.True((await db.UserRecommendationSettings.SingleAsync(cancellationToken)).SeriesEnabled);
            var run = await db.RecommendationRuns.SingleAsync(cancellationToken);
            Assert.Equal((RecommendationRunStatus.Failed, RecommendationRunTrigger.TabEnabled), (run.Status, run.Trigger));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Anna's save of exactly the tabs given, and <paramref name="dailySearchLimit"/>.</summary>
    private Task<HttpResponseMessage> SaveAsync(
        bool movies = false, bool series = false, bool cartoons = false, bool documentaries = false, int dailySearchLimit = 60) =>
        _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new RecommendationSettingsRequest(movies, series, cartoons, documentaries, dailySearchLimit));

    private async Task AssertNoRunAsync(CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken)));
        Assert.Empty(DrainQueue());
    }

    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<RecommendationRunQueue>();
        var ids = new List<Guid>();
        while (queue.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }
}
