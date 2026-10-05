using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33, Vasyl's decisions 2 and 5 — which tab a work goes to (a documentary first, then an animation, else a film or
// a show), and what the users' tabs ask of the catalog: the union of the tabs every user who can sign in has on (their
// saved ones, or all four by default). A work in a tab nobody has on is dropped; a kind of work no tab holds is never
// asked for; and with every tab off for everyone, a run asks the catalog nothing at all and leaves no candidate.
public sealed class RecommendationTabsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    // TMDb's genre ids (VendorSamples/Tmdb): the same Animation and Documentary in both lists.
    private const int Action = 28;
    private const int Drama = 18;
    private const int Animation = 16;
    private const int Documentary = 99;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private IUserRecommendationSettingsSeeder _settings = null!;
    private AuthorizedUser _owner = null!;
    private UserEntity _member = null!;
    private IntegrationConnectionEntity _library = null!;
    private IntegrationConnectionEntity _catalog = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationTabsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new MediaWorkSeeder(_factory.Scope);
        _settings = new UserRecommendationSettingsSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.Create("member");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _library = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _catalog = await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Recompute_puts_a_documentary_in_documentaries_an_animation_in_cartoons_and_the_rest_in_movies_or_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantLibraryAsync();

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // An animated documentary is a documentary first; an animation is a cartoon, a film or a show alike.
        Assert.Equal(
            [
                (MediaWorkKind.Movie, "500", RecommendationTab.Documentaries),
                (MediaWorkKind.Movie, "501", RecommendationTab.Cartoons),
                (MediaWorkKind.Movie, "502", RecommendationTab.Movies),
                (MediaWorkKind.Series, "600", RecommendationTab.Cartoons),
                (MediaWorkKind.Series, "601", RecommendationTab.Documentaries),
                (MediaWorkKind.Series, "602", RecommendationTab.Series),
            ],
            (await ReadStoredAsync(cancellationToken)).Select(candidate => (candidate.Kind, candidate.CatalogId, candidate.Tab)));
    }

    [Fact]
    public async Task Recompute_keeps_the_tabs_some_user_has_on_and_drops_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantLibraryAsync();
        await _settings.Save(_owner.User, [RecommendationTab.Movies]);
        await _settings.Save(_member, [RecommendationTab.Cartoons]);
        // Every tab on by default, but a disabled user sees no list: their tabs count for nothing.
        await new UserSeeder(_factory.Scope).Create("gone", isDisabled: true);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(
            [("501", RecommendationTab.Cartoons), ("502", RecommendationTab.Movies), ("600", RecommendationTab.Cartoons)],
            (await ReadStoredAsync(cancellationToken)).Select(candidate => (candidate.CatalogId, candidate.Tab)));
    }

    [Fact]
    public async Task Recompute_with_only_series_on_never_asks_the_catalog_for_films()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantLibraryAsync();
        await _settings.Save(_owner.User, [RecommendationTab.Series]);
        await _settings.Save(_member, [RecommendationTab.Series]);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The library's film is no seed, and nothing of a film — its details, genres, collections, discovery — is asked.
        Assert.Equal(1, run.SeedCount);
        Assert.Equal([("602", RecommendationTab.Series)], (await ReadStoredAsync(cancellationToken)).Select(candidate => (candidate.CatalogId, candidate.Tab)));
        var paths = _factory.TmdbHttp.Requests.Select(request => request.Uri.AbsolutePath).ToList();
        Assert.DoesNotContain(paths, path => path.Contains("movie", StringComparison.Ordinal) || path.StartsWith("/3/collection", StringComparison.Ordinal));
        Assert.Contains("/3/tv/1396", paths);
        Assert.Contains("/3/genre/tv/list", paths);
    }

    [Fact]
    public async Task Recompute_with_every_tab_off_for_everyone_asks_the_catalog_nothing_and_leaves_no_candidate()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantLibraryAsync();
        await _settings.Save(_owner.User, []);
        await _settings.Save(_member, []);
        await new UserSeeder(_factory.Scope).Create("gone", isDisabled: true);
        // What an earlier run recommended, when tabs were on: nobody sees it now.
        var earlier = await new CatalogWorkSeeder(_factory.Scope).Create(MediaWorkKind.Movie, "502", "Бойовик", new DateOnly(2001, 1, 1), [Action], catalog: _catalog);
        await new RecommendationCandidateSeeder(_factory.Scope).Create(earlier, RecommendationTab.Movies);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((RecommendationRunStatus.Succeeded, 0, 0), (run.Status, run.SeedCount, run.CandidateCount));
        Assert.Empty(_factory.TmdbHttp.Requests);
        Assert.Empty(await ReadStoredAsync(cancellationToken));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A film and a show in the library, each recommended beside works of every tab.</summary>
    private async Task PlantLibraryAsync()
    {
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        await _works.CreateSeries(_library, "Пуститься берега", 2008, tmdbTvId: "1396");
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 679, "Чужі", GenreIds: [Action], RecommendationIds: [500, 501, 502]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 500, "Анімаційна документалка", GenreIds: [Animation, Documentary]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 501, "Мультфільм", GenreIds: [Animation, Action]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 502, "Бойовик", GenreIds: [Action]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 1396, "Пуститься берега", GenreIds: [Drama], RecommendationIds: [600, 601, 602]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 600, "Мультсеріал", GenreIds: [Animation]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 601, "Документальний серіал", GenreIds: [Documentary]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 602, "Драма", GenreIds: [Drama]));
    }

    private async Task<IReadOnlyList<StoredRecommendation>> ReadStoredAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        return stored;
    }
}
