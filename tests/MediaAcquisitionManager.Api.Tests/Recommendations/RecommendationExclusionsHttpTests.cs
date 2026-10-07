using MediaAcquisitionManager.Core.Abstractions.Metadata;
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

// MAM-33 — what is never recommended: a work the library holds — by its own TMDb id, by an IMDb id the catalog filed
// it under, by title and year (ADR 0012, kinds kept apart), by the name of a file only a folder scan found, or as a
// seed's possible match — and a work not released yet. One seed recommends every work these tests ask about, so
// only the exclusion decides.
public sealed class RecommendationExclusionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const int Action = 28;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _library = null!;
    private IntegrationConnectionEntity _catalog = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationExclusionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new MediaWorkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _library = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _catalog = await integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        // The seed: "Чужий", which TMDb recommends "Чужі" (679) and "Термінатор" (218) beside.
        await _works.CreateMovie(_library, "Чужий", 1979, tmdbId: "348");
        _tmdb.AddWork(Film(348, "Чужий", "Alien", "1979-05-25", recommendations: [679, 218]));
        _tmdb.AddWork(Film(679, "Чужі", "Aliens", "1986-07-18"));
        _tmdb.AddWork(Film(218, "Термінатор", "The Terminator", "1984-10-26"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Recompute_with_nothing_else_held_recommends_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The baseline every other test here leaves "Чужі" out of.
        Assert.Equal(["218", "679"], await CandidateIdsAsync(cancellationToken));
    }

    [Fact]
    public async Task Recompute_leaves_out_a_work_the_library_holds_by_its_own_tmdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A title the catalog's doesn't match: only the id says it is held.
        await _works.CreateMovie(_library, "Aliens (Special Edition)", 1990, tmdbId: "679");

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(["218"], await CandidateIdsAsync(cancellationToken));
    }

    [Fact]
    public async Task Recompute_leaves_out_a_work_whose_known_imdb_id_the_library_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A library film the catalog finds nothing for — no id it files, a title it doesn't know — but whose IMDb id is
        // the one "Чужі"'s details gave when they were read.
        await _works.CreateMovie(_library, "Aliens. Director's Cut", 1990, imdbId: "tt0090605");
        await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Movie, "679", "Чужі", new DateOnly(1986, 7, 18), [Action], imdbId: "tt0090605",
            detailsUtc: _factory.Time.GetUtcNow().UtcDateTime.AddDays(-3), catalog: _catalog);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(["218"], await CandidateIdsAsync(cancellationToken));
        // A list's summary refreshed the row and left what its details said.
        await _factory.WithDbAsync(async db =>
            Assert.Equal("tt0090605", (await db.CatalogWorks.SingleAsync(work => work.CatalogId == "679", cancellationToken)).ImdbId));
    }

    [Theory]
    [InlineData("Aliens", 1986, false)]
    [InlineData("Чужі", 1986, false)]
    [InlineData("Aliens", 1985, true)]
    public async Task Recompute_leaves_out_a_work_the_library_holds_by_title_and_year(string title, int year, bool recommended)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // No id at all, and a title lookup that finds nothing: only the title and the year can say it is held.
        await _works.CreateMovie(_library, title, year);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The original or the catalog's title with the year; a year apart is another film (ADR 0012: no year ± 1).
        Assert.Equal(recommended, (await CandidateIdsAsync(cancellationToken)).Contains("679"));
    }

    [Fact]
    public async Task Recompute_matches_titles_kind_against_kind_so_a_held_show_of_the_title_leaves_a_film_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateSeries(_library, "Aliens", 1986);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Contains("679", await CandidateIdsAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/media/movies/Aliens (1986)/aliens.mkv")]
    [InlineData("/media/downloads/Aliens (1986) BDRip 1080p [Hurtom].mkv")]
    // MAM-20 — a Windows-style path claims by its folder too, since FileNameClaims splits at a backslash.
    [InlineData(@"D:\Movies\Aliens (1986)\aliens.mkv")]
    public async Task Recompute_leaves_out_a_work_a_scan_found_files_name_claims(string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A file in the media folders no library reported (MAM-18): never a work, but evidently the owner has it.
        await new PhysicalFileDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time).CreateUnreported(path);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(["218"], await CandidateIdsAsync(cancellationToken));
    }

    [Fact]
    public async Task Recompute_leaves_out_a_seeds_possible_match_found_by_its_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // No TMDb id and a Ukrainian title of another year: TMDb files its IMDb id under "Чужі", the best guess there is.
        await _works.CreateMovie(_library, "Чужі 2", 1992, imdbId: "tt0090605");
        _tmdb.AddFind("tt0090605", MediaWorkKind.Movie, 679);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A seed now, so held — and its own recommendations are the library's too.
        Assert.Equal(2, run.SeedCount);
        Assert.Equal(["218"], await CandidateIdsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var lookup = await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                ("Movie|imdb|tt0090605", MediaWorkKind.Movie, "679", MatchEvidence.Identifier),
                (lookup.QueryKey, lookup.ResultKind!.Value, lookup.ResultCatalogId, lookup.Evidence!.Value));
            Assert.Equal((ProvenanceSourceKind.Metadata, _catalog.Id, "TMDb"), (lookup.SourceKind, lookup.SourceConnectionId!.Value, lookup.SourceName));
        });
    }

    [Fact]
    public async Task Recompute_leaves_out_a_work_not_released_yet_or_of_no_date()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(Film(348, "Чужий", "Alien", "1979-05-25", recommendations: [679, 218, 999001, 999002]));
        _tmdb.AddWork(Film(999001, "Чужий: Земля", "Alien: Earth", "2099-08-12"));
        _tmdb.AddWork(Film(999002, "Чужий: Без дати", "Alien: Undated", null));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(["218", "679"], await CandidateIdsAsync(cancellationToken));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FakeTmdbWork Film(int id, string title, string originalTitle, string? releaseDate, int[]? recommendations = null) =>
        new(MediaWorkKind.Movie, id, title, originalTitle, releaseDate, [Action], RecommendationIds: recommendations);

    private async Task<IReadOnlyList<string>> CandidateIdsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ids = [];
        await _factory.WithDbAsync(async db =>
            ids = [.. (await StoredRecommendation.ReadAsync(db, cancellationToken)).Select(candidate => candidate.CatalogId).Order(StringComparer.Ordinal)]);
        return ids;
    }
}
