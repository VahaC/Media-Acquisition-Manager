using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — a held series' missing seasons: the regular seasons TMDb says have aired (by today) of which the library
// holds no episode. The candidate is the series itself, held as it is. The specials (season 0) never count, nor does a
// season still to come; a season the library holds even one episode of is held.
public sealed class RecommendationMissingSeasonsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const int Drama = 18;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _library = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationMissingSeasonsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new MediaWorkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _library = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Recompute_recommends_a_held_series_for_its_aired_seasons_it_holds_no_episode_of()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One episode of each of seasons 1 and 2 — of ten each: a season partly held is held.
        await _works.CreateSeries(_library, "Єллоустоун", 2018, tmdbTvId: "73586", seasons: [1, 2]);
        _tmdb.AddWork(Show(
            new FakeTmdbSeason(0, "2018-06-20", 12),
            new FakeTmdbSeason(1, "2018-06-20"),
            new FakeTmdbSeason(2, "2019-06-19"),
            new FakeTmdbSeason(3, "2020-06-21"),
            new FakeTmdbSeason(4, "2021-11-07"),
            new FakeTmdbSeason(5, AirDate: null),
            new FakeTmdbSeason(6, "2099-01-01")));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Not the specials, not a season of no date, not one still to come.
        Assert.Equal((1, 1), (run.SeedCount, run.CandidateCount));
        var series = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal(
            (MediaWorkKind.Series, "73586", RecommendationTab.Series, "3,4", false),
            (series.Kind, series.CatalogId, series.Tab, series.MissingSeasons, series.RequiresAvailability));
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.MissingSeasons, Seasons: "3,4")], series.Reasons);
    }

    [Fact]
    public async Task Recompute_recommends_nothing_for_a_series_holding_every_aired_season()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateSeries(_library, "Єллоустоун", 2018, tmdbTvId: "73586", seasons: [1, 2, 3]);
        _tmdb.AddWork(Show(
            new FakeTmdbSeason(1, "2018-06-20"),
            new FakeTmdbSeason(2, "2019-06-19"),
            new FakeTmdbSeason(3, "2020-06-21"),
            new FakeTmdbSeason(4, "2099-06-21")));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Empty(await ReadStoredAsync(cancellationToken));
    }

    [Fact]
    public async Task Recompute_counts_the_seasons_of_every_library_series_on_the_same_show()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The Grand Tour's way (ADR 0017): one show as two library series, which hold a season each.
        await _works.CreateSeries(_library, "Єллоустоун", 2018, tmdbTvId: "73586", seasons: [1]);
        await _works.CreateSeries(_library, "Yellowstone", 2018, tmdbTvId: "73586", seasons: [3]);
        _tmdb.AddWork(Show(
            new FakeTmdbSeason(1, "2018-06-20"),
            new FakeTmdbSeason(2, "2019-06-19"),
            new FakeTmdbSeason(3, "2020-06-21")));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(1, run.SeedCount);
        Assert.Equal("2", Assert.Single(await ReadStoredAsync(cancellationToken)).MissingSeasons);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FakeTmdbWork Show(params FakeTmdbSeason[] seasons) =>
        new(MediaWorkKind.Series, 73586, "Єллоустоун", "Yellowstone", "2018-06-20", [Drama], Seasons: seasons);

    private async Task<IReadOnlyList<StoredRecommendation>> ReadStoredAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        return stored;
    }
}
