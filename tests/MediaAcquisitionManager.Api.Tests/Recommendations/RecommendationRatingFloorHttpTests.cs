using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — the rating floor (a Bayesian one: votes × (rating − 6.0) ≥ 50) on the reasons of taste alone — Recommended,
// Director, Creator, TopRated — and not on the reasons the library itself gives — Collection, MissingSeasons,
// NewAtSource, which are kept whatever the rating. A work left with no reason is not recommended at all.
public sealed class RecommendationRatingFloorHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const int Action = 28;
    private const int Drama = 18;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _library = null!;
    private IntegrationConnectionEntity _catalog = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationRatingFloorHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new MediaWorkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _library = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _catalog = await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(7.0, 50, true)]
    [InlineData(7.0, 49, false)]
    [InlineData(6.5, 100, true)]
    [InlineData(6.5, 99, false)]
    [InlineData(8.0, 25, true)]
    [InlineData(9.5, 4, false)]
    [InlineData(5.9, 100000, false)]
    [InlineData(0.0, 0, false)]
    public async Task Recompute_keeps_a_recommended_work_only_above_the_floor(double rating, int votes, bool kept)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        _tmdb.AddWork(Film(679, "Чужі", recommendations: [218]));
        _tmdb.AddWork(Film(218, "Термінатор") with { Rating = rating, VoteCount = votes });

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A 7.0 needs 50 votes, a 6.5 a hundred, an 8.0 twenty-five; a few raving votes and a sub-6 average never pass.
        Assert.Equal(kept ? 1 : 0, run.CandidateCount);
        Assert.Equal(kept, (await ReadStoredAsync(cancellationToken)).Any(candidate => candidate.CatalogId == "218"));
    }

    [Fact]
    public async Task Recompute_keeps_a_collections_part_under_the_floor_but_only_for_the_collection()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Чужий", 1979, tmdbId: "348");
        // Both in the collection and recommended beside the seed, rated 3.0 by five.
        _tmdb.AddWork(Film(348, "Чужий", collectionId: 8091, recommendations: [8077]));
        _tmdb.AddWork(Film(8077, "Чужий 3") with { Rating = 3.0, VoteCount = 5 });
        _tmdb.AddCollection(8091, "Чужий (Колекція)", 348, 8077);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var part = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Collection, "Чужий (Колекція)", 1, 2)], part.Reasons);
    }

    [Fact]
    public async Task Recompute_keeps_a_held_series_missing_seasons_under_the_floor()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateSeries(_library, "Єллоустоун", 2018, tmdbTvId: "73586", seasons: [1]);
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, 73586, "Єллоустоун", ReleaseDate: "2018-06-20", GenreIds: [Drama], Rating: 2.0, VoteCount: 3,
            Seasons: [new FakeTmdbSeason(1, "2018-06-20"), new FakeTmdbSeason(2, "2019-06-19")]));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal("2", Assert.Single(await ReadStoredAsync(cancellationToken)).MissingSeasons);
    }

    [Fact]
    public async Task Recompute_keeps_a_work_new_at_a_source_however_few_rated_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await new IntegrationConnectionSeeder(_factory.Scope).Create("torznab", "Toloka", apiKey: JellyfinApiKey);
        var unrated = await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Movie, "1311031", "Новий фільм", new DateOnly(2026, 9, 1), [Action], rating: null, voteCount: 0, catalog: _catalog);
        await new SourceFeedEntrySeeder(_factory.Scope).Create(toloka, "Новий фільм (2026)", unrated);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var work = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal(RecommendationReasonKind.NewAtSource, Assert.Single(work.Reasons).Kind);
    }

    [Fact]
    public async Task Recompute_drops_a_directors_and_a_top_rated_work_under_the_floor()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_owner.User, [RecommendationTab.Movies]);
        await _works.CreateMovie(_library, "Початок", 2010, tmdbId: "27205");
        await _works.CreateMovie(_library, "Інтерстеллар", 2014, tmdbId: "157336");
        _tmdb.AddPerson(525, "Christopher Nolan");
        _tmdb.AddWork(Film(27205, "Початок") with { DirectorIds = [525] });
        _tmdb.AddWork(Film(157336, "Інтерстеллар") with { DirectorIds = [525] });
        _tmdb.AddWork(Film(11660, "Слідування") with { DirectorIds = [525], Rating = 5.5, VoteCount = 3000 });
        _tmdb.AddWork(Film(1124, "Престиж") with { DirectorIds = [525] });
        _tmdb.AddWork(Film(900, "Маловідомий") with { Rating = 9.0, VoteCount = 10 });
        _tmdb.AddWork(Film(238, "Хрещений батько") with { Rating = 8.7, VoteCount = 20000 });
        _tmdb.AddDiscover(MediaWorkKind.Movie, 1, 900, 238);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(["1124", "238"], (await ReadStoredAsync(cancellationToken)).Select(candidate => candidate.CatalogId).Order(StringComparer.Ordinal));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FakeTmdbWork Film(int id, string title, int? collectionId = null, int[]? recommendations = null) =>
        new(MediaWorkKind.Movie, id, title, GenreIds: [Action], CollectionId: collectionId, RecommendationIds: recommendations);

    private async Task<IReadOnlyList<StoredRecommendation>> ReadStoredAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        return stored;
    }
}
