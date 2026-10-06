using System.Net;
using System.Text;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33, ADR 0025 — the catalog the runs read through: TMDb's answers kept in tables of their own, with their
// provenance, served for 30 days and read again after; the lookups of library works without a TMDb id (by an IMDb id,
// else by a cleaned title and its year, used only when one work matches) cached the same way, "nothing" included; a
// seed TMDb no longer has skipped. A catalog failure fails the run and keeps the previous candidates; nothing is ever
// written to the library's works, titles or identifiers. MAM-34 — the details a run reads keep a work's runtime,
// overview and alternative titles, and the details a reading before MAM-34 stored are read again on the next run,
// however young (R22).
public sealed class RecommendationCatalogCacheHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const int Action = 28;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _library = null!;
    private IntegrationConnectionEntity _catalog = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationCatalogCacheHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    [InlineData(29, 1)]
    [InlineData(31, 2)]
    public async Task Recompute_reads_the_catalog_again_only_once_its_rows_are_older_than_30_days(int daysLater, int reads)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync();
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        var first = await ReadStoredAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromDays(daysLater));
        // The clock moved past the access token's life: the owner signs in again.
        _owner = await new UserSeeder(_factory.Scope).CreateSession(_owner.User);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The details, the collection and the genre list alike; the answers rebuilt from the rows are the same answers.
        Assert.Equal(reads, Count("/3/movie/348"));
        Assert.Equal(reads, Count("/3/collection/8091"));
        Assert.Equal(reads, Count("/3/genre/movie/list"));
        Assert.Equal(
            first.Select(candidate => (candidate.CatalogId, candidate.Tab, candidate.Reasons.Count)),
            (await ReadStoredAsync(cancellationToken)).Select(candidate => (candidate.CatalogId, candidate.Tab, candidate.Reasons.Count)));
    }

    [Fact]
    public async Task Recompute_stores_the_catalogs_rows_apart_from_the_library_with_their_provenance()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var works = await db.CatalogWorks.AsNoTracking().ToDictionaryAsync(work => work.CatalogId, cancellationToken);
            var alien = works["348"];
            Assert.Equal(
                (MediaWorkKind.Movie, "Чужий", ProvenanceSourceKind.Metadata, _catalog.Id, "TMDb", "movie/348"),
                (alien.Kind, alien.Title, alien.SourceKind, alien.SourceConnectionId!.Value, alien.SourceName, alien.SourceItemId));
            Assert.Equal(("8091", "Чужий (Колекція)", now), (alien.CollectionId, alien.CollectionName, alien.DetailsUtc!.Value));
            // Known from lists only: a summary, no details.
            Assert.Null(works["218"].DetailsUtc);
            Assert.Equal(
                [218],
                await db.CatalogWorkRecommendations.Where(recommendation => recommendation.CatalogWorkId == alien.Id)
                    .Select(recommendation => int.Parse(recommendation.CatalogId)).ToListAsync(cancellationToken));
            Assert.Equal([Action], await db.CatalogWorkGenres.Where(genre => genre.CatalogWorkId == alien.Id).Select(genre => genre.GenreId).ToListAsync(cancellationToken));
            var collection = await db.CatalogCollections.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("8091", "collection/8091"), (collection.CatalogId, collection.SourceItemId));
            Assert.Equal(
                ["348", "8077"],
                await db.CatalogCollectionParts.OrderBy(part => part.Position).Select(part => part.MovieCatalogId).ToListAsync(cancellationToken));
            Assert.Equal(
                (CatalogGenreRole.Animation, CatalogGenreRole.Documentary),
                ((await db.CatalogGenres.SingleAsync(genre => genre.Kind == MediaWorkKind.Movie && genre.GenreId == 16, cancellationToken)).Role,
                    (await db.CatalogGenres.SingleAsync(genre => genre.Kind == MediaWorkKind.Movie && genre.GenreId == 99, cancellationToken)).Role));
        });
    }

    [Fact]
    public async Task Recompute_stores_the_runtime_overview_and_alternative_titles_the_details_give()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync();
        _tmdb.AddWork(Film(348, "Чужий", "1979-05-25", collectionId: 8091, recommendations: [218]) with
        {
            RuntimeMinutes = 117,
            Overview = "Екіпаж буксира «Ностромо» отримує сигнал лиха.",
            AlternativeTitles = ["Alien", "Чужий (1979)", "ALIEN"],
        });

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var alien = await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == "348", cancellationToken);
            Assert.Equal(
                (117, "Екіпаж буксира «Ностромо» отримує сигнал лиха.", CatalogStore.CurrentDetailsVersion),
                (alien.RuntimeMinutes!.Value, alien.Overview, alien.DetailsVersion));
            // Each title once, whatever its letter case, in TMDb's order.
            Assert.Equal(
                ["Alien", "Чужий (1979)"],
                await db.CatalogWorkAlternativeTitles.Where(title => title.CatalogWorkId == alien.Id)
                    .OrderBy(title => title.Position).Select(title => title.Title).ToListAsync(cancellationToken));
            // Known from lists only: no details, so none of theirs.
            var terminator = await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == "218", cancellationToken);
            Assert.Equal(((int?)null, (string?)null, 0), (terminator.RuntimeMinutes, terminator.Overview, terminator.DetailsVersion));
        });
    }

    [Fact]
    public async Task Recompute_reads_again_within_30_days_the_details_a_reading_before_mam_34_stored()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync();
        _tmdb.AddWork(Film(348, "Чужий", "1979-05-25", collectionId: 8091, recommendations: [218]) with { RuntimeMinutes = 117 });
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        // The row as the AddLibraryLinks migration leaves a cached one: its details dated, read without a runtime, and
        // DetailsVersion 0.
        await _factory.WithDbAsync(db => db.CatalogWorks.ExecuteUpdateAsync(
            setters => setters.SetProperty(work => work.DetailsVersion, 0).SetProperty(work => work.RuntimeMinutes, (int?)null),
            cancellationToken));
        _factory.Time.Advance(TimeSpan.FromDays(1));
        _owner = await new UserSeeder(_factory.Scope).CreateSession(_owner.User);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Read again once, a day after the last reading — then fresh for 30 days like any other — with no migration
        // statement clearing the cache (R22). The collection and the genres were read by today's rules: kept.
        Assert.Equal(2, Count("/3/movie/348"));
        Assert.Equal(1, Count("/3/collection/8091"));
        Assert.Equal(1, Count("/3/genre/movie/list"));
        await _factory.WithDbAsync(async db =>
        {
            var alien = await db.CatalogWorks.AsNoTracking().SingleAsync(work => work.CatalogId == "348", cancellationToken);
            Assert.Equal((117, CatalogStore.CurrentDetailsVersion), (alien.RuntimeMinutes!.Value, alien.DetailsVersion));
        });
    }

    [Fact]
    public async Task Recompute_looks_an_id_less_work_up_by_its_imdb_id_once_and_caches_the_answer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Чужі 2", 1992, imdbId: "tt0090605");
        _tmdb.AddFind("tt0090605", MediaWorkKind.Movie, 679);
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", recommendations: [218]));
        _tmdb.AddWork(Film(218, "Термінатор", "1984-10-26"));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(1, Count("/3/find/tt0090605"));
        Assert.Equal(["218"], (await ReadStoredAsync(cancellationToken)).Select(candidate => candidate.CatalogId));
        await _factory.WithDbAsync(async db =>
        {
            var lookup = await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("Movie|imdb|tt0090605", "679", MatchEvidence.Identifier), (lookup.QueryKey, lookup.ResultCatalogId, lookup.Evidence!.Value));
        });
    }

    [Fact]
    public async Task Recompute_looks_a_numbered_title_up_cleaned_with_its_year_and_uses_the_one_match()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "120. Landing Stripling", 1962);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, 700);
        _tmdb.AddWork(Film(700, "Landing Stripling", "1962-03-01", recommendations: [701]));
        _tmdb.AddWork(Film(701, "Друга посадка", "1963-03-01"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(1, run.SeedCount);
        Assert.Equal(["701"], (await ReadStoredAsync(cancellationToken)).Select(candidate => candidate.CatalogId));
        var search = Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/search/movie");
        Assert.Contains("query=Landing Stripling", Uri.UnescapeDataString(search.Uri.Query), StringComparison.Ordinal);
        Assert.Contains("year=1962", search.Uri.Query, StringComparison.Ordinal);
        await _factory.WithDbAsync(async db =>
        {
            var lookup = await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                ("Movie|title|LANDING STRIPLING|1962", "700", MatchEvidence.TitleAndYear),
                (lookup.QueryKey, lookup.ResultCatalogId, lookup.Evidence!.Value));
        });
    }

    [Fact]
    public async Task Recompute_uses_no_title_two_works_of_the_year_share_and_caches_that_as_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Alien", 1979);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Alien", 1979, 348, 349);
        _tmdb.AddWork(Film(348, "Alien", "1979-05-25", recommendations: [218]));
        _tmdb.AddWork(Film(349, "Alien", "1979-11-01"));
        _tmdb.AddWork(Film(218, "Термінатор", "1984-10-26"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A title and a year never decide between two works (specification §3.1): no seed, and no details asked.
        Assert.Equal((0, 0), (run.SeedCount, run.CandidateCount));
        Assert.Equal(0, Count("/3/movie/348"));
        Assert.Equal(1, Count("/3/search/movie"));
        await _factory.WithDbAsync(async db =>
        {
            var lookup = await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("Movie|title|ALIEN|1979", (string?)null, (MatchEvidence?)null), (lookup.QueryKey, lookup.ResultCatalogId, lookup.Evidence));
        });
    }

    [Fact]
    public async Task Recompute_skips_a_seed_the_catalog_no_longer_has_and_forgets_the_lookup_that_named_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Загублений", 2001, tmdbId: "778");
        await _works.CreateMovie(_library, "Знайдений помилково", 2002, imdbId: "tt0000777");
        _tmdb.AddFind("tt0000777", MediaWorkKind.Movie, 777);
        _tmdb.AddWork(Film(777, "Злитий запис", "2002-01-01"));
        // TMDb merged or removed both since: their details answer its 404.
        _tmdb.Intercept = request => request.RequestUri!.AbsolutePath is "/3/movie/777" or "/3/movie/778"
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(FakeTmdbServer.NotFoundBody, Encoding.UTF8, "application/json") }
            : null;

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Not a failure: nothing to recommend from, and the lookup that led there is not followed again.
        Assert.Equal((RecommendationRunStatus.Succeeded, 0), (run.Status, run.SeedCount));
        await _factory.WithDbAsync(async db =>
        {
            var lookup = await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("Movie|imdb|tt0000777", (string?)null), (lookup.QueryKey, lookup.ResultCatalogId));
        });
    }

    [Fact]
    public async Task Recompute_failing_mid_run_keeps_the_previous_candidates_and_fails_with_the_catalogs_masked_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync();
        var first = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);
        var before = await ReadStoredAsync(cancellationToken);
        // A month on, the details are read again — and TMDb fails on its side.
        _factory.Time.Advance(TimeSpan.FromDays(31));
        _owner = await new UserSeeder(_factory.Scope).CreateSession(_owner.User);
        _tmdb.Intercept = request => request.RequestUri!.AbsolutePath == "/3/movie/348" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;

        //Act
        var failed = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(RecommendationRunStatus.Succeeded, first.Status);
        Assert.Equal((RecommendationRunStatus.Failed, (int?)null, (int?)null), (failed.Status, failed.SeedCount, failed.CandidateCount));
        Assert.StartsWith("TMDb", failed.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeTmdbServer.Token, failed.Error, StringComparison.Ordinal);
        // Candidates computed from half the library would replace good ones with fewer.
        var after = await ReadStoredAsync(cancellationToken);
        Assert.Equal(
            before.Select(candidate => (candidate.CatalogId, candidate.Tab, candidate.Score)),
            after.Select(candidate => (candidate.CatalogId, candidate.Tab, candidate.Score)));
    }

    [Fact]
    public async Task Recompute_never_writes_the_librarys_works_titles_or_identifiers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync();
        // Possible matches by an IMDb id and by a title: neither is ever written to the library (ADR 0025).
        await _works.CreateMovie(_library, "Чужі 2", 1992, imdbId: "tt0090605");
        _tmdb.AddFind("tt0090605", MediaWorkKind.Movie, 8077);
        await _works.CreateMovie(_library, "120. Landing Stripling", 1962);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, 700);
        _tmdb.AddWork(Film(700, "Landing Stripling", "1962-03-01"));
        IReadOnlyList<string> before = [];
        await _factory.WithDbAsync(async db => before = (await StoredLibrarySnapshot.ReadAsync(db, cancellationToken)).Rows());

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(3, run.SeedCount);
        await _factory.WithDbAsync(async db => Assert.Equal(before, (await StoredLibrarySnapshot.ReadAsync(db, cancellationToken)).Rows()));
        // MAM-34 — nor a link: a possible match is the run's guess, never the owner's word (ADR 0025).
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>"Чужий" in the library: TMDb's 348, of the collection 8091 with "Чужий 3", recommending "Термінатор".</summary>
    private async Task PlantAlienAsync()
    {
        await _works.CreateMovie(_library, "Чужий", 1979, tmdbId: "348");
        _tmdb.AddWork(Film(348, "Чужий", "1979-05-25", collectionId: 8091, recommendations: [218]));
        _tmdb.AddWork(Film(8077, "Чужий 3", "1992-05-22"));
        _tmdb.AddWork(Film(218, "Термінатор", "1984-10-26"));
        _tmdb.AddCollection(8091, "Чужий (Колекція)", 348, 8077);
    }

    private static FakeTmdbWork Film(int id, string title, string releaseDate, int? collectionId = null, int[]? recommendations = null) =>
        new(MediaWorkKind.Movie, id, title, ReleaseDate: releaseDate, GenreIds: [Action], CollectionId: collectionId, RecommendationIds: recommendations);

    private int Count(string path) => _factory.TmdbHttp.Requests.Count(request => request.Uri.AbsolutePath == path);

    private async Task<IReadOnlyList<StoredRecommendation>> ReadStoredAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        return stored;
    }
}
