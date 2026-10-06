using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-34, R15 — a run reads a work's ids as the Library page counts them: a link to the work itself is the work's own
// catalog id, so the work seeds by it and is held by it, and the MAM-33 guess never runs for a linked work; a link beside a
// different library id of its scheme leaves two ids — both held, neither a seed. An episode-to-show link, read straight
// from the links whatever its item is (a film item, an episode of no series), holds the show and seeds it, the seasons
// the links name held; a show seeded by a link that names no season gets no missing seasons.
public sealed class RecommendationLinksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const int Animation = 16;
    private const int Documentary = 99;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _library = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationLinksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new MediaWorkSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _library = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Recompute_seeds_a_linked_work_by_its_link_holds_it_and_never_looks_it_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The seed "Puss Gets the Boot", which recommends the short the owner linked and "Fit to Be Tied".
        await _works.CreateMovie(_library, "Puss Gets the Boot", 1940, tmdbId: "177");
        _tmdb.AddWork(Film(177, "Puss Gets the Boot", "1940-02-10", recommendations: [223021, 300]));
        // No id of the library's: the guess would find 700 by the cleaned title and the year, but the owner said 223021.
        var stripling = await _works.CreateMovie(_library, "120. Landing Stripling", 1962);
        await LinkAsync(stripling, MediaIdentifierScheme.Tmdb, "223021");
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, 700);
        _tmdb.AddWork(Film(700, "Landing Stripling", "1962-03-01", recommendations: [301]));
        _tmdb.AddWork(Film(223021, "Посадка", "1962-05-18", recommendations: [300]));
        _tmdb.AddWork(Film(300, "Fit to Be Tied", "1952-07-26"));
        _tmdb.AddWork(Film(301, "Not to Be Seen", "1960-01-01"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(2, run.SeedCount);
        Assert.Equal(["300"], await CandidateIdsAsync(cancellationToken));
        Assert.Equal((1, 0, 0), (Count("/3/movie/223021"), Count("/3/movie/700"), Count("/3/search/movie")));
        await _factory.WithDbAsync(async db => Assert.False(await db.CatalogLookups.AnyAsync(cancellationToken)));
    }

    [Fact]
    public async Task Recompute_holds_both_ids_of_a_work_whose_link_and_library_disagree_and_seeds_by_neither()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Puss Gets the Boot", 1940, tmdbId: "177");
        _tmdb.AddWork(Film(177, "Puss Gets the Boot", "1940-02-10", recommendations: [300, 301, 302]));
        // Jellyfin says 300, the owner's link says 301, and neither replaced the other: a conflict, which nothing settles.
        var conflicting = await _works.CreateMovie(_library, "Fit to Be Tied", 1952, tmdbId: "300");
        await LinkAsync(conflicting, MediaIdentifierScheme.Tmdb, "301");
        _tmdb.AddWork(Film(300, "Fit to Be Tied", "1952-07-26", recommendations: [303]));
        _tmdb.AddWork(Film(301, "Not to Be Seen", "1960-01-01", recommendations: [303]));
        _tmdb.AddWork(Film(302, "Pecos Pest", "1955-11-11"));
        _tmdb.AddWork(Film(303, "Never Recommended", "1961-01-01"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(1, run.SeedCount);
        Assert.Equal(["302"], await CandidateIdsAsync(cancellationToken));
        Assert.Equal((0, 0, 0), (Count("/3/movie/300"), Count("/3/movie/301"), Count("/3/search/movie")));
    }

    [Fact]
    public async Task Recompute_holds_and_seeds_the_show_an_episode_link_names_and_drops_the_film_id_it_replaces()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // "Blue Planet" recommends "Planet Earth" and "Frozen Planet"; the library lists a Planet Earth episode as a film,
        // with a wrong film id the episode's link replaces.
        await _works.CreateSeries(_library, "Блакитна планета", 2001, tmdbTvId: "1043", seasons: [1]);
        _tmdb.AddWork(Show(1043, "Блакитна планета", "2001-09-12", [new FakeTmdbSeason(1, "2001-09-12")], recommendations: [1044, 1045]));
        var jungles = await _works.CreateMovie(_library, "Planet_Earth_E08 - Jungles", 2006, tmdbId: "999");
        await LinkAsync(jungles, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, season: 1, episode: 8, supersededValue: "999", imdbId: "tt0795176");
        _tmdb.AddWork(Show(1044, "Планета Земля", "2006-03-05", [new FakeTmdbSeason(1, "2006-03-05")], recommendations: [1046]));
        _tmdb.AddWork(Show(1045, "Крижана планета", "2011-10-26", [new FakeTmdbSeason(1, "2011-10-26")]));
        _tmdb.AddWork(Show(1046, "Планета Земля II", "2016-11-06", [new FakeTmdbSeason(1, "2016-11-06")]));
        _tmdb.AddWork(Film(999, "Wrong Film", "2006-01-01", recommendations: [300]));
        _tmdb.AddWork(Film(300, "Fit to Be Tied", "1952-07-26"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Planet Earth is held (and holds its one season), so only what it and Blue Planet recommend besides is listed;
        // the film id the link replaced seeds nothing, and nothing is guessed.
        Assert.Equal(2, run.SeedCount);
        Assert.Equal(["1045", "1046"], await CandidateIdsAsync(cancellationToken));
        Assert.Equal((1, 0, 0, 0), (Count("/3/tv/1044"), Count("/3/movie/999"), Count("/3/search/movie"), Count("/3/search/tv")));
    }

    [Fact]
    public async Task Recompute_holds_a_film_id_jellyfin_gave_an_episode_linked_item_after_its_link_and_seeds_by_it_never()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateSeries(_library, "Блакитна планета", 2001, tmdbTvId: "1043", seasons: [1]);
        _tmdb.AddWork(Show(1043, "Блакитна планета", "2001-09-12", [new FakeTmdbSeason(1, "2001-09-12")], recommendations: [1044, 1045]));
        // Linked as an episode of the show while it held no film id; a refresh gave it film 12345 since.
        var jungles = await _works.CreateMovie(_library, "Planet_Earth_E08 - Jungles", 2006, tmdbId: "12345");
        await LinkAsync(jungles, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, season: 1, episode: 8);
        _tmdb.AddWork(Show(1044, "Планета Земля", "2006-03-05", [new FakeTmdbSeason(1, "2006-03-05")], recommendations: [1046]));
        _tmdb.AddWork(Show(1045, "Крижана планета", "2011-10-26", [new FakeTmdbSeason(1, "2011-10-26")]));
        _tmdb.AddWork(Show(1046, "Планета Земля II", "2016-11-06", [new FakeTmdbSeason(1, "2016-11-06")]));
        _tmdb.AddWork(Film(12345, "Jungles", "2006-01-01", recommendations: [300]));
        _tmdb.AddWork(Film(300, "Fit to Be Tied", "1952-07-26"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The owner says episode, Jellyfin says film: neither wins — the show seeds by the link, the film by nothing.
        Assert.Equal(2, run.SeedCount);
        Assert.Equal(["1045", "1046"], await CandidateIdsAsync(cancellationToken));
        Assert.Equal((1, 0), (Count("/3/tv/1044"), Count("/3/movie/12345")));
    }

    [Theory]
    [InlineData(1, "2")]
    [InlineData(null, null)]
    public async Task Recompute_recommends_the_seasons_of_a_linked_show_its_links_do_not_name_and_none_for_a_link_naming_none(
        int? season, string? missingSeasons)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jungles = await _works.CreateMovie(_library, "Planet_Earth_E08 - Jungles", 2006);
        await LinkAsync(jungles, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, season: season, episode: season is null ? null : 8);
        _tmdb.AddWork(Show(1044, "Планета Земля", "2006-03-05", [new FakeTmdbSeason(1, "2006-03-05"), new FakeTmdbSeason(2, "2008-01-01")]));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Without a season, which of the show's seasons the library holds is unknown: none is called missing.
        Assert.Equal(1, run.SeedCount);
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        Assert.Equal(
            missingSeasons is null ? Array.Empty<(string, string?)>() : [("1044", missingSeasons)],
            stored.Select(candidate => (candidate.CatalogId, candidate.MissingSeasons)));
    }

    [Fact]
    public async Task Recompute_seeds_the_show_an_episode_of_no_series_is_linked_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A Tom & Jerry Kids episode Jellyfin lists without a series: an Episode work of its own.
        var works = new MediaWorkDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        var episode = await works.Create(MediaWorkKind.Episode);
        await works.AddTitle(episode.Id, "Flippin Fido", MediaTitleKind.Display, null, _library.Id, _library.Name, "kids-1");
        await _links.Create(
            _library, "kids-1", episode.Id, MediaIdentifierScheme.TmdbTv, "1100", LibraryLinkTarget.ShowEpisode, setBy: _owner.User,
            configure: link =>
            {
                link.ItemKind = MediaWorkKind.Episode;
                link.SeasonNumber = 1;
                link.EpisodeNumber = 4;
            });
        _tmdb.AddWork(Show(1100, "Том і Джеррі в дитинстві", "1990-09-08", [new FakeTmdbSeason(1, "1990-09-08")], recommendations: [1101]));
        _tmdb.AddWork(Show(1101, "Чіп і Дейл", "1989-03-04", [new FakeTmdbSeason(1, "1989-03-04")]));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(1, run.SeedCount);
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        var candidate = Assert.Single(stored);
        Assert.Equal("1101", candidate.CatalogId);
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Recommended, Held: 1, Titles: "Flippin Fido")], candidate.Reasons);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A link the owner set on the one item of <paramref name="work"/> (the item that reported its title).</summary>
    private async Task LinkAsync(
        MediaWorkEntity work,
        MediaIdentifierScheme scheme,
        string value,
        LibraryLinkTarget target = LibraryLinkTarget.Work,
        int? season = null,
        int? episode = null,
        string? supersededValue = null,
        string? imdbId = null)
    {
        var itemId = "";
        await _factory.WithDbAsync(async db => itemId = (await db.MediaWorkTitles.FirstAsync(title => title.MediaWorkId == work.Id)).SourceItemId!);
        await _links.Create(
            _library, itemId, work.Id, scheme, value, target, setBy: _owner.User,
            configure: link =>
            {
                link.SeasonNumber = season;
                link.EpisodeNumber = episode;
                link.SupersededValue = supersededValue;
                link.ImdbId = imdbId;
            });
    }

    private static FakeTmdbWork Film(int id, string title, string releaseDate, int[]? recommendations = null) =>
        new(MediaWorkKind.Movie, id, title, ReleaseDate: releaseDate, GenreIds: [Animation], RecommendationIds: recommendations);

    private static FakeTmdbWork Show(int id, string title, string firstAirDate, FakeTmdbSeason[] seasons, int[]? recommendations = null) =>
        new(MediaWorkKind.Series, id, title, ReleaseDate: firstAirDate, GenreIds: [Documentary], Seasons: seasons, RecommendationIds: recommendations);

    private int Count(string path) => _factory.TmdbHttp.Requests.Count(request => request.Uri.AbsolutePath == path);

    private async Task<IReadOnlyList<string>> CandidateIdsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = [];
        await _factory.WithDbAsync(async db =>
            ids = [.. (await StoredRecommendation.ReadAsync(db, cancellationToken)).Select(candidate => candidate.CatalogId).Order(StringComparer.Ordinal)]);
        return ids;
    }
}
