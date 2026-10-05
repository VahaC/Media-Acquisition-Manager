using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — what a running computation tells the page it is doing (Vasyl asked for more than "Computing…"): its stage, in
// order, with "done of total" for a counted stage, written on the run's row when the stage begins, at its end, and in
// between at most every two seconds; read by every signed-in user from runs/latest while the run runs, and gone once it
// has ended, however it ended. A failed write of the progress never fails the run. Each run is the owner's Recompute
// now over HTTP and the worker against a fake TMDb; the row is read before every write to it, so the test sees what
// each write left.
public sealed class RecommendationRunProgressHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string LatestUrl = "/api/recommendations/runs/latest";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>TMDb's movie genre id of Action (VendorSamples/Tmdb/genres-movie.json).</summary>
    private const int Action = 28;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _library = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationRunProgressHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new MediaWorkSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // The Movies tab alone, for both: one kind of work and one tab to discover for, so the stages are few and fixed.
        var settings = new UserRecommendationSettingsSeeder(_factory.Scope);
        await settings.Save(_owner.User, [RecommendationTab.Movies]);
        await settings.Save(_member.User, [RecommendationTab.Movies]);
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _library = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Recompute_writes_each_stage_in_order_with_its_counts_and_ends_with_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two films of one collection and one director, and one known by its IMDb id alone: a lookup, three seeds, a
        // collection, a person, and the Movies tab short of seeds, so two pages of its best-rated films.
        await _works.CreateMovie(_library, "Чужий", 1979, tmdbId: "348");
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        await _works.CreateMovie(_library, "Хижак", 1987, imdbId: "tt0093773");
        _tmdb.AddPerson(578, "Рідлі Скотт");
        _tmdb.AddWork(Film(348, "Чужий", "1979-05-25", collectionId: 8091, directors: [578]));
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", collectionId: 8091, directors: [578]));
        _tmdb.AddWork(Film(106, "Хижак", "1987-06-12"));
        _tmdb.AddWork(Film(8077, "Чужий 3", "1992-05-22"));
        _tmdb.AddWork(Film(238, "Хрещений батько", "1972-03-14"));
        _tmdb.AddWork(Film(240, "Хрещений батько 2", "1974-12-20"));
        _tmdb.AddCollection(8091, "Чужий (Колекція)", 348, 679, 8077);
        _tmdb.AddFind("tt0093773", MediaWorkKind.Movie, 106);
        _tmdb.AddDiscover(MediaWorkKind.Movie, 1, 238);
        _tmdb.AddDiscover(MediaWorkKind.Movie, 2, 240);
        var writes = RecordWrites(cancellationToken);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((RecommendationRunStatus.Succeeded, 3), (run.Status, run.SeedCount));
        // From the claim on: each stage when it begins and at its end — a few catalog calls take far less than the two
        // seconds that would write one in between — and the stages without counts alone.
        Assert.Equal(
            [
                (RecommendationRunStatus.Running, null, null, null),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingLibrary, null, null),
                (RecommendationRunStatus.Running, RecommendationRunStage.FindingLibraryWorks, 0, 1),
                (RecommendationRunStatus.Running, RecommendationRunStage.FindingLibraryWorks, 1, 1),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 0, 3),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 3, 3),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingCollections, 0, 1),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingCollections, 1, 1),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingPeople, 0, 1),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingPeople, 1, 1),
                (RecommendationRunStatus.Running, RecommendationRunStage.Discovering, 0, 2),
                (RecommendationRunStatus.Running, RecommendationRunStage.Discovering, 2, 2),
                (RecommendationRunStatus.Running, RecommendationRunStage.Saving, null, null),
            ],
            writes.SkipWhile(write => write.Status == RecommendationRunStatus.Queued));
        // Ended: no stage on the answer or on the row.
        Assert.Equal(((RecommendationRunStage?)null, (int?)null, (int?)null), (run.Stage, run.StageDone, run.StageTotal));
        Assert.Equal((RecommendationRunStatus.Succeeded, null, null, null), await ReadRowAsync(cancellationToken));
    }

    [Fact]
    public async Task Recompute_passes_over_a_counted_stage_with_nothing_to_count()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Ten films with ids of their own: nothing to look up, no collection, no person, and enough seeds to need no
        // discovery.
        for (var index = 0; index < 10; index++)
        {
            await _works.CreateMovie(_library, $"Фільм {index}", 2000 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, $"Фільм {index}", $"{2000 + index}-01-01"));
        }

        var writes = RecordWrites(cancellationToken);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Never "0 of 0": the page goes from one stage with work to the next.
        Assert.Equal(RecommendationRunStatus.Succeeded, run.Status);
        Assert.Equal(
            [
                (RecommendationRunStatus.Running, null, null, null),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingLibrary, null, null),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 0, 10),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 10, 10),
                (RecommendationRunStatus.Running, RecommendationRunStage.Saving, null, null),
            ],
            writes.SkipWhile(write => write.Status == RecommendationRunStatus.Queued));
    }

    [Fact]
    public async Task Recompute_writes_a_stages_count_at_most_every_two_seconds_and_always_at_its_end()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var index = 0; index < 20; index++)
        {
            await _works.CreateMovie(_library, $"Фільм {index}", 2000 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, $"Фільм {index}", $"{2000 + index}-01-01"));
        }

        // Two seconds pass while the tenth work's details are read — by the clock's timestamps, which the run measures
        // with; the other nineteen reads take a few milliseconds each.
        var detailsRead = 0;
        _tmdb.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/3/movie/", StringComparison.Ordinal) && Interlocked.Increment(ref detailsRead) == 10)
                _factory.Time.AdvanceTimestamps(RecommendationRunner.ProgressInterval);
            return null;
        };
        var writes = RecordWrites(cancellationToken);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Twenty works read, three writes of their stage: its beginning, once two seconds had passed, and its end.
        Assert.Equal((RecommendationRunStatus.Succeeded, 20), (run.Status, run.SeedCount));
        Assert.Equal(
            [
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 0, 20),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 10, 20),
                (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 20, 20),
            ],
            writes.Where(write => write.Stage == RecommendationRunStage.ReadingWorks));
    }

    [Fact]
    public async Task LatestRun_while_a_run_reads_the_catalog_shows_its_stage_and_counts_to_the_owner_and_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var index = 0; index < 3; index++)
        {
            await _works.CreateMovie(_library, $"Фільм {index}", 2000 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, $"Фільм {index}", $"{2000 + index}-01-01"));
        }

        // What the page polls while the first work's details are read: the owner's and a member's own requests.
        RecommendationRunDto? ownerSaw = null;
        RecommendationRunDto? memberSaw = null;
        _tmdb.Intercept = request =>
        {
            if (ownerSaw is null && request.RequestUri!.AbsolutePath.StartsWith("/3/movie/", StringComparison.Ordinal))
            {
                ownerSaw = Task.Run(() => ReadLatestAsync(_owner, cancellationToken), cancellationToken).GetAwaiter().GetResult();
                memberSaw = Task.Run(() => ReadLatestAsync(_member, cancellationToken), cancellationToken).GetAwaiter().GetResult();
            }

            return null;
        };

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The stage is no secret of the owner's, unlike who asked and why a run failed.
        Assert.Equal(
            (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 0, 3, "owner"),
            (ownerSaw!.Status, ownerSaw.Stage, ownerSaw.StageDone, ownerSaw.StageTotal, ownerSaw.RequestedByUserName));
        Assert.Equal(
            (RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 0, 3, (string?)null),
            (memberSaw!.Status, memberSaw.Stage, memberSaw.StageDone, memberSaw.StageTotal, memberSaw.RequestedByUserName));
        // Once it succeeded, nothing of the stage is left for either.
        foreach (var user in new[] { _owner, _member })
        {
            var after = await ReadLatestAsync(user, cancellationToken);
            Assert.Equal(
                (RecommendationRunStatus.Succeeded, (RecommendationRunStage?)null, (int?)null, (int?)null),
                (after.Status, after.Stage, after.StageDone, after.StageTotal));
        }
    }

    [Fact]
    public async Task Recompute_that_fails_in_a_stage_ends_failed_with_no_stage()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var index = 0; index < 3; index++)
        {
            await _works.CreateMovie(_library, $"Фільм {index}", 2000 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, $"Фільм {index}", $"{2000 + index}-01-01"));
        }

        // The second work's details fail: the run stops in the middle of its stage.
        var detailsRead = 0;
        _tmdb.Intercept = request =>
            request.RequestUri!.AbsolutePath.StartsWith("/3/movie/", StringComparison.Ordinal) && Interlocked.Increment(ref detailsRead) == 2
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : null;
        var writes = RecordWrites(cancellationToken);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(RecommendationRunStatus.Failed, run.Status);
        Assert.NotNull(run.Error);
        // It was in the middle of reading the works, and the failure took the stage with it.
        Assert.Equal((RecommendationRunStatus.Running, RecommendationRunStage.ReadingWorks, 0, 3), writes[^1]);
        Assert.Equal(((RecommendationRunStage?)null, (int?)null, (int?)null), (run.Stage, run.StageDone, run.StageTotal));
        Assert.Equal((RecommendationRunStatus.Failed, null, null, null), await ReadRowAsync(cancellationToken));
    }

    [Fact]
    public async Task Recompute_goes_on_when_a_write_of_its_progress_fails()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", recommendations: [218]));
        _tmdb.AddWork(Film(218, "Термінатор", "1984-10-26"));
        // The first progress write — the only writes that set the stage from a value rather than clearing it — meets a
        // database that refuses it.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"StageDone\" = @", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("The database is locked."));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // What the page shows of a run never decides it: the run computed and stored its candidates all the same.
        Assert.Equal((RecommendationRunStatus.Succeeded, 1, 1), (run.Status, run.SeedCount, run.CandidateCount));
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(RecommendationRunner).FullName && entry.Level == LogLevel.Warning);
        Assert.Contains(run.Id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(warning.Exception);
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Level >= LogLevel.Error && entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reconcile_of_a_run_left_running_in_a_stage_fails_it_with_no_stage_after_both_users_saw_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A run the app stopped under, twelve of forty works read.
        var left = await new RecommendationRunSeeder(_factory.Scope).Create(RecommendationRunStatus.Running);
        await _factory.WithDbAsync(db => db.RecommendationRuns
            .Where(row => row.Id == left.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Stage, RecommendationRunStage.ReadingWorks)
                    .SetProperty(row => row.StageDone, 12)
                    .SetProperty(row => row.StageTotal, 40),
                cancellationToken));
        var ownerSaw = await ReadLatestAsync(_owner, cancellationToken);
        var memberSaw = await ReadLatestAsync(_member, cancellationToken);

        //Act
        await _factory.Resolve<RecommendationRunReconciler>().ReconcileAsync(cancellationToken);

        //Assert
        Assert.Equal((RecommendationRunStage.ReadingWorks, 12, 40), (ownerSaw.Stage!.Value, ownerSaw.StageDone!.Value, ownerSaw.StageTotal!.Value));
        Assert.Equal((RecommendationRunStage.ReadingWorks, 12, 40), (memberSaw.Stage!.Value, memberSaw.StageDone!.Value, memberSaw.StageTotal!.Value));
        var after = await ReadLatestAsync(_member, cancellationToken);
        Assert.Equal(
            (RecommendationRunStatus.Failed, (RecommendationRunStage?)null, (int?)null, (int?)null),
            (after.Status, after.Stage, after.StageDone, after.StageTotal));
        Assert.Equal((RecommendationRunStatus.Failed, null, null, null), await ReadRowAsync(cancellationToken));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FakeTmdbWork Film(int id, string title, string? releaseDate, int? collectionId = null, int[]? directors = null, int[]? recommendations = null) =>
        new(MediaWorkKind.Movie, id, title, ReleaseDate: releaseDate, GenreIds: [Action], CollectionId: collectionId, DirectorIds: directors, RecommendationIds: recommendations);

    /// <summary>
    /// The run's row as it stood just before each write to it, in order — so, write by write, what the write before
    /// left — from the queued run on. The last write's own result is the row once the run has ended.
    /// </summary>
    private List<(RecommendationRunStatus Status, RecommendationRunStage? Stage, int? Done, int? Total)> RecordWrites(CancellationToken cancellationToken)
    {
        var seen = new List<(RecommendationRunStatus Status, RecommendationRunStage? Stage, int? Done, int? Total)>();

        void Arm() =>
            _factory.DbCommandHook.BeforeNextCommand(
                command => command.CommandText.Contains("UPDATE \"RecommendationRuns\"", StringComparison.Ordinal),
                async _ =>
                {
                    // Armed again first: the read below, through a context of its own, is no write and passes untouched.
                    Arm();
                    await _factory.WithDbAsync(async db => seen.Add(await ReadRowAsync(db, cancellationToken)));
                });

        Arm();
        return seen;
    }

    private async Task<(RecommendationRunStatus Status, RecommendationRunStage? Stage, int? Done, int? Total)> ReadRowAsync(CancellationToken cancellationToken)
    {
        (RecommendationRunStatus, RecommendationRunStage?, int?, int?) row = default;
        await _factory.WithDbAsync(async db => row = await ReadRowAsync(db, cancellationToken));
        return row;
    }

    private static async Task<(RecommendationRunStatus Status, RecommendationRunStage? Stage, int? Done, int? Total)> ReadRowAsync(
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
        return (run.Status, run.Stage, run.StageDone, run.StageTotal);
    }

    private async Task<RecommendationRunDto> ReadLatestAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest(LatestUrl, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
