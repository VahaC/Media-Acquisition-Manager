using System.Net;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Catalog;

// MAM-34 — the catalog store's part in linking a library work to TMDb: a work's details keep its runtime, overview,
// alternative titles and a show's IMDb id, and come back from the rows as they came from the catalog; details an older
// reading stored (DetailsVersion below CatalogStore.CurrentDetailsVersion) are read again however young (R22); and a
// search or a find by IMDb id stores every work it lists — so a suggestion is a row whose poster is served — answers in
// the catalog's order, and asks the catalog every time. MAM-46 — a connection built before the owner changed the
// catalog's language (a run under way) still gets its answers but changes no stored row, and stores a work not stored yet
// in its own language, so it is read again in the new one; a lookup by a title is asked again in another language, one by
// an IMDb id is not. The store is resolved from the app's own container, one scope a call, as the suggestions use it,
// against TMDb faked at the network; the poster is asked over HTTP.
public sealed class CatalogStoreTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string PostersUrl = "/api/recommendations/posters";
    private const string PlanetEarthPoster = "ajNcvf2fvOmus8rxPdpoGrdL4OI.jpg";
    private const string PlanetEarthOverview = "Документальний серіал BBC про природу Землі.";

    /// <summary>MAM-46 — Planet Earth's name by the language TMDb is asked in.</summary>
    private static readonly Dictionary<string, string> PlanetEarthTitles = new(StringComparer.Ordinal)
    {
        ["uk-UA"] = "Планета Земля",
        ["en-US"] = "Planet Earth",
    };

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _catalog = null!;
    private FakeTmdbServer _tmdb = null!;

    public CatalogStoreTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _catalog = await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetWork_stores_a_shows_runtime_overview_alternative_titles_and_imdb_id_and_serves_them_back_from_the_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth", "Planet Erde", "Planeta Tierra"]));
        var fresh = await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);

        //Act
        var cached = await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);

        //Assert
        Assert.Equal(1, Count("/3/tv/1044"));
        // The answer rebuilt from the rows is the catalog's: a caller can't tell them apart.
        foreach (var work in new[] { fresh, cached })
        {
            Assert.Equal((53, "tt0795176", PlanetEarthOverview), (work.RuntimeMinutes!.Value, work.ImdbId, work.Overview));
            Assert.Equal(["Planet Earth", "Planet Erde", "Planeta Tierra"], work.AlternativeTitles);
        }

        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (53, "tt0795176", PlanetEarthOverview, CatalogStore.CurrentDetailsVersion),
                (row.RuntimeMinutes!.Value, row.ImdbId, row.Overview, row.DetailsVersion));
            Assert.Equal(
                [(0, "Planet Earth"), (1, "Planet Erde"), (2, "Planeta Tierra")],
                (await db.CatalogWorkAlternativeTitles.AsNoTracking()
                    .Where(title => title.CatalogWorkId == row.Id)
                    .OrderBy(title => title.Position)
                    .ToListAsync(cancellationToken))
                .Select(title => (title.Position, title.Title)));
        });
    }

    [Theory]
    [InlineData(0, 1, 53, "tt0795176")]
    [InlineData(CatalogStore.CurrentDetailsVersion, 0, null, null)]
    public async Task GetWork_reads_again_within_30_days_only_the_details_an_older_reading_stored(
        int detailsVersion, int reads, int? runtimeMinutes, string? imdbId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth"]));
        // Details read a day ago: by a reading before MAM-34 — no runtime, overview, titles or show's IMDb id, the
        // migration's DetailsVersion 0 — or by today's.
        await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Series, "1044", "Планета Земля", new DateOnly(2006, 3, 5), [99],
            detailsUtc: _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1), catalog: _catalog);
        await _factory.WithDbAsync(db => db.CatalogWorks.ExecuteUpdateAsync(
            setters => setters.SetProperty(work => work.DetailsVersion, detailsVersion), cancellationToken));

        //Act
        var work = await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);

        //Assert
        // R22: no migration statement clears the cache; the older reading is simply not fresh.
        Assert.Equal(reads, Count("/3/tv/1044"));
        Assert.Equal((runtimeMinutes, imdbId), (work.RuntimeMinutes, work.ImdbId));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((CatalogStore.CurrentDetailsVersion, runtimeMinutes), (row.DetailsVersion, row.RuntimeMinutes));
        });
    }

    [Fact]
    public async Task GetWork_read_again_after_30_days_replaces_the_runtime_overview_and_alternative_titles()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth", "Planet Erde", "Planeta Tierra"]));
        await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);
        _tmdb.AddWork(PlanetEarth(50, ["Planeta Tierra", "BBC Planet Earth"]) with { Overview = null });
        _factory.Time.Advance(TimeSpan.FromDays(31));

        //Act
        var work = await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);

        //Assert
        Assert.Equal(2, Count("/3/tv/1044"));
        Assert.Equal((50, (string?)null), (work.RuntimeMinutes!.Value, work.Overview));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((50, (string?)null), (row.RuntimeMinutes!.Value, row.Overview));
            // The titles the details gave now, in their order; the ones they no longer give are gone.
            Assert.Equal(
                [(0, "Planeta Tierra"), (1, "BBC Planet Earth")],
                (await db.CatalogWorkAlternativeTitles.AsNoTracking().OrderBy(title => title.Position).ToListAsync(cancellationToken))
                .Select(title => (title.Position, title.Title)));
        });
    }

    [Fact]
    public async Task Search_stores_every_work_it_lists_so_their_posters_are_served_and_answers_in_the_catalogs_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth"]));
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, 68595, "Планета Земля II", "Planet Earth II", "2016-11-06", [99, 10751], PosterPath: "/m4TMauOurlkXuWP2XWN3P6kM13f.jpg"));
        _tmdb.AddSearch(MediaWorkKind.Series, "Planet Earth", null, 1044, 68595);
        using var posterBefore = await _factory.GetHttpRequest($"{PostersUrl}/{PlanetEarthPoster}", _owner.AccessToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var found = await SearchAsync(MediaWorkKind.Series, "Planet Earth", null, cancellationToken);

        //Assert
        Assert.Equal([(MediaWorkKind.Series, "1044"), (MediaWorkKind.Series, "68595")], found.Select(work => (work.Kind, work.Id)));
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.CatalogWorks.AsNoTracking().OrderBy(work => work.CatalogId).ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    ("1044", "Планета Земля", "/" + PlanetEarthPoster, "tv/1044"),
                    ("68595", "Планета Земля II", "/m4TMauOurlkXuWP2XWN3P6kM13f.jpg", "tv/68595"),
                ],
                rows.Select(row => (row.CatalogId, row.Title, row.PosterPath!, row.SourceItemId)));
            // Summaries, with their provenance: a list says nothing of the details.
            Assert.All(rows, row => Assert.Equal(
                (MediaWorkKind.Series, ProvenanceSourceKind.Metadata, _catalog.Id, (DateTime?)null, now),
                (row.Kind, row.SourceKind, row.SourceConnectionId!.Value, row.DetailsUtc, row.ObservedUtc)));
            Assert.Equal(
                [99, 99, 10751],
                await db.CatalogWorkGenres.OrderBy(genre => genre.GenreId).Select(genre => genre.GenreId).ToListAsync(cancellationToken));
        });
        // A poster is served only for a stored work's poster path: none before the search, now this one.
        Assert.Equal(HttpStatusCode.NotFound, posterBefore.StatusCode);
        using var posterAfter = await _factory.GetHttpRequest($"{PostersUrl}/{PlanetEarthPoster}", _owner.AccessToken);
        Assert.Equal((HttpStatusCode.OK, "image/jpeg"), (posterAfter.StatusCode, posterAfter.Content.Headers.ContentType?.MediaType));
    }

    [Fact]
    public async Task Search_asks_the_catalog_every_time_and_leaves_a_listed_works_details_as_they_were()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth", "Planet Erde"]));
        _tmdb.AddSearch(MediaWorkKind.Series, "Planet Earth", 2006, 1044);
        await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);

        //Act
        var first = await SearchAsync(MediaWorkKind.Series, "Planet Earth", 2006, cancellationToken);
        var second = await SearchAsync(MediaWorkKind.Series, "Planet Earth", 2006, cancellationToken);

        //Assert
        // Never cached itself: the suggestions keep what they searched, and the owner's search wants the answer of now.
        Assert.Equal(2, Count("/3/search/tv"));
        Assert.Equal(["1044"], first.Select(work => work.Id));
        Assert.Equal(["1044"], second.Select(work => work.Id));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.NotNull(row.DetailsUtc);
            Assert.Equal((53, "tt0795176", CatalogStore.CurrentDetailsVersion), (row.RuntimeMinutes!.Value, row.ImdbId, row.DetailsVersion));
            Assert.Equal(2, await db.CatalogWorkAlternativeTitles.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Search_the_catalog_fails_passes_the_masked_failure_on_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth"]));
        _tmdb.AddSearch(MediaWorkKind.Series, "Planet Earth", null, 1044);
        _tmdb.Intercept = request => request.RequestUri!.AbsolutePath == "/3/search/tv" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;

        //Act
        var result = await WithStoreAsync((store, catalog) => store.SearchAsync(catalog, MediaWorkKind.Series, "Planet Earth", null, cancellationToken));

        //Assert
        Assert.False(result.IsSuccess);
        Assert.StartsWith("TMDb", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeTmdbServer.Token, result.Error, StringComparison.Ordinal);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.CatalogWorks.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task FindByImdbId_stores_the_films_and_shows_filed_under_the_id_the_films_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth"]));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1044, "Фільм 1044", ReleaseDate: "2007-01-01", PosterPath: "/film1044.jpg"));
        _tmdb.AddFind("tt0795176", MediaWorkKind.Series, 1044);
        _tmdb.AddFind("tt0795176", MediaWorkKind.Movie, 1044);

        //Act
        var found = await WithStoreAsync(async (store, catalog) =>
            (await store.FindByImdbIdAsync(catalog, "tt0795176", cancellationToken)).Value!);
        var second = await WithStoreAsync(async (store, catalog) =>
            (await store.FindByImdbIdAsync(catalog, "tt0795176", cancellationToken)).Value!);

        //Assert
        // A film and a show of one number are two works (ADR 0017), each a row of its own.
        Assert.Equal([(MediaWorkKind.Movie, "1044"), (MediaWorkKind.Series, "1044")], found.Select(work => (work.Kind, work.Id)));
        Assert.Equal(found.Select(work => (work.Kind, work.Id)), second.Select(work => (work.Kind, work.Id)));
        Assert.Equal(2, Count("/3/find/tt0795176"));
        await _factory.WithDbAsync(async db => Assert.Equal(
            [(MediaWorkKind.Movie, "Фільм 1044", "/film1044.jpg"), (MediaWorkKind.Series, "Планета Земля", "/" + PlanetEarthPoster)],
            (await db.CatalogWorks.AsNoTracking().OrderBy(work => work.SourceItemId).ToListAsync(cancellationToken))
            .Select(work => (work.Kind, work.Title, work.PosterPath!))));
    }

    // ── MAM-46: the catalog's language ───────────────────────────────────────

    [Fact]
    public async Task Connection_built_before_a_language_change_gets_its_answers_but_changes_no_stored_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth", "Planet Erde"]) with { TitlesByLanguage = PlanetEarthTitles });
        _tmdb.AddSearch(MediaWorkKind.Series, "Planet Earth", null, 1044);
        // Read in English yesterday, by a run that started after the owner chose English.
        var readUtc = _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1);
        var stored = await new CatalogWorkSeeder(_factory.Scope).Create(
            MediaWorkKind.Series, "1044", "Planet Earth", new DateOnly(2006, 3, 5), [99],
            detailsUtc: readUtc, catalog: _catalog, posterPath: "/english.jpg", detailsLanguage: "en-US");

        //Act
        var (asked, found, work) = await WithOutdatedStoreAsync(async (store, catalog) => (
            catalog.Language,
            (await store.SearchAsync(catalog, MediaWorkKind.Series, "Planet Earth", null, cancellationToken)).Value!,
            (await store.GetWorkAsync(catalog, MediaWorkKind.Series, "1044", cancellationToken)).Value!));

        //Assert
        // The run that was under way asks in Ukrainian and gets Ukrainian answers: the English row is no answer to it.
        Assert.Equal("uk-UA", asked);
        Assert.Equal(["Планета Земля"], found.Select(summary => summary.Title));
        Assert.Equal(("Планета Земля", (int?)53), (work.Summary.Title, work.RuntimeMinutes));
        Assert.Equal(["uk-UA"], LanguagesOf("/3/tv/1044"));
        // The row the new language's run read stays as it was: its title, poster, details and their language.
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                ("Planet Earth", "/english.jpg", "en-US", (DateTime?)readUtc, (int?)null, stored.ObservedUtc),
                (row.Title, row.PosterPath, row.DetailsLanguage, row.DetailsUtc, row.RuntimeMinutes, row.ObservedUtc));
            Assert.Equal(0, await db.CatalogWorkAlternativeTitles.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Connection_built_before_a_language_change_stores_a_new_work_in_its_own_language_which_is_read_again_in_the_new_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(PlanetEarth(53, ["Planet Earth"]) with { TitlesByLanguage = PlanetEarthTitles });
        _tmdb.AddFind("tt0795176", MediaWorkKind.Series, 1044);
        await WithOutdatedStoreAsync(async (store, catalog) =>
        {
            await store.GetWorkAsync(catalog, MediaWorkKind.Series, "1044", cancellationToken);
            return await store.LookupAsync(catalog, CatalogLookupQuery.ByImdbId(MediaWorkKind.Series, "tt0795176"), cancellationToken);
        });
        (string Title, string Language) added = default;
        await _factory.WithDbAsync(async db => added = await db.CatalogWorks.AsNoTracking()
            .Select(row => ValueTuple.Create(row.Title, row.DetailsLanguage))
            .SingleAsync(cancellationToken));

        //Act
        var work = await GetWorkAsync(MediaWorkKind.Series, "1044", cancellationToken);

        //Assert
        // A work no row held is added, stamped in the language it was read in; the next read in English reads it again.
        Assert.Equal(("Планета Земля", "uk-UA"), added);
        Assert.Equal("Planet Earth", work.Summary.Title);
        Assert.Equal(["uk-UA", "en-US"], LanguagesOf("/3/tv/1044"));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.CatalogWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("Planet Earth", "en-US"), (row.Title, row.DetailsLanguage));
            // A lookup by an IMDb id holds ids alone: stored whatever the language it was asked in.
            var lookup = await db.CatalogLookups.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("Series|imdb|tt0795176", "1044", "uk-UA"), (lookup.QueryKey, lookup.ResultCatalogId, lookup.Language));
        });
    }

    [Fact]
    public async Task Lookup_by_a_title_is_asked_again_in_another_language_and_one_by_an_imdb_id_is_not()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 129, "Віднесені привидами", "千と千尋の神隠し", "2001-07-20", ImdbId: "tt0245429")
        {
            TitlesByLanguage = new Dictionary<string, string>(StringComparer.Ordinal) { ["uk-UA"] = "Віднесені привидами", ["en-US"] = "Spirited Away" },
        });
        _tmdb.AddSearch(MediaWorkKind.Movie, "Віднесені привидами", 2001, 129);
        _tmdb.AddFind("tt0245429", MediaWorkKind.Movie, 129);
        var byTitle = CatalogLookupQuery.ByTitle(MediaWorkKind.Movie, "Віднесені привидами", 2001);
        var byImdbId = CatalogLookupQuery.ByImdbId(MediaWorkKind.Movie, "tt0245429");
        var inUkrainian = await LookupAsync(byTitle, cancellationToken);
        await LookupAsync(byImdbId, cancellationToken);

        //Act
        await SetCatalogLanguageAsync("en-US");
        var inEnglish = await LookupAsync(byTitle, cancellationToken);
        var imdbInEnglish = await LookupAsync(byImdbId, cancellationToken);
        await SetCatalogLanguageAsync("uk-UA");
        var backInUkrainian = await LookupAsync(byTitle, cancellationToken);

        //Assert
        // TMDb keeps a result by its title in the language asked, or its original: asked in English, the film is "Spirited
        // Away", so the Ukrainian title finds nothing; asked in Ukrainian again, it finds the film again.
        Assert.Equal(("129", false, "129"), (inUkrainian.CatalogId, inEnglish.IsFound, backInUkrainian.CatalogId));
        Assert.Equal(["uk-UA", "en-US", "uk-UA"], LanguagesOf("/3/search/movie"));
        Assert.Equal("129", imdbInEnglish.CatalogId);
        Assert.Equal(1, Count("/3/find/tt0245429"));
        await _factory.WithDbAsync(async db => Assert.Equal(
            [("Movie|imdb|tt0245429", "uk-UA"), (byTitle.Key, "uk-UA")],
            (await db.CatalogLookups.AsNoTracking().OrderBy(lookup => lookup.QueryKey).ToListAsync(cancellationToken))
            .Select(lookup => (lookup.QueryKey, lookup.Language))));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Planet Earth (2006) as TMDb lists it: its latest episode of <paramref name="runtimeMinutes"/>, its IMDb id
    /// in its external ids, and <paramref name="alternativeTitles"/>.</summary>
    private static FakeTmdbWork PlanetEarth(int runtimeMinutes, string[] alternativeTitles) =>
        new(
            MediaWorkKind.Series, 1044, "Планета Земля", "Planet Earth", "2006-03-05", [99], PosterPath: "/" + PlanetEarthPoster,
            ImdbId: "tt0795176", Seasons: [new FakeTmdbSeason(1, "2006-03-05", 11)], RuntimeMinutes: runtimeMinutes,
            Overview: PlanetEarthOverview, AlternativeTitles: alternativeTitles);

    private async Task<CatalogWork> GetWorkAsync(MediaWorkKind kind, string catalogId, CancellationToken cancellationToken) =>
        await WithStoreAsync(async (store, catalog) => (await store.GetWorkAsync(catalog, kind, catalogId, cancellationToken)).Value!);

    private async Task<IReadOnlyList<CatalogWorkSummary>> SearchAsync(MediaWorkKind kind, string title, int? year, CancellationToken cancellationToken) =>
        await WithStoreAsync(async (store, catalog) => (await store.SearchAsync(catalog, kind, title, year, cancellationToken)).Value!);

    /// <summary>Runs <paramref name="action"/> with the app's catalog store and the connected catalog, in a scope of their
    /// own, as a request or a run has them.</summary>
    private async Task<T> WithStoreAsync<T>(Func<ICatalogStore, CatalogConnection, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var connected = await scope.ServiceProvider.GetRequiredService<ICatalogConnector>().ConnectAsync();
        return await action(scope.ServiceProvider.GetRequiredService<ICatalogStore>(), connected.Connection!);
    }

    /// <summary>MAM-46 — runs <paramref name="action"/> with a connection built while the catalog was asked in Ukrainian, the
    /// owner having chosen English since: a run that was under way when the language changed.</summary>
    private async Task<T> WithOutdatedStoreAsync<T>(Func<ICatalogStore, CatalogConnection, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var connected = await scope.ServiceProvider.GetRequiredService<ICatalogConnector>().ConnectAsync();
        await SetCatalogLanguageAsync("en-US");
        return await action(scope.ServiceProvider.GetRequiredService<ICatalogStore>(), connected.Connection!);
    }

    private async Task<CatalogLookupResult> LookupAsync(CatalogLookupQuery query, CancellationToken cancellationToken) =>
        await WithStoreAsync(async (store, catalog) => (await store.LookupAsync(catalog, query, cancellationToken)).Value!);

    /// <summary>The owner's change of the catalog's language, as the integration's row holds it.</summary>
    private Task SetCatalogLanguageAsync(string language) =>
        _factory.WithDbAsync(db => db.IntegrationConnections.ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.CatalogLanguage, language)));

    private int Count(string path) => _factory.TmdbHttp.Requests.Count(request => request.Uri.AbsolutePath == path);

    /// <summary>The <c>language</c> of every request to <paramref name="path"/>, in the order they were sent.</summary>
    private List<string> LanguagesOf(string path) =>
    [
        .. _factory.TmdbHttp.Requests
            .Where(request => request.Uri.AbsolutePath == path)
            .Select(request => QueryHelpers.ParseQuery(request.Uri.Query)["language"].ToString()),
    ];
}
