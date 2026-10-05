using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33, ADR 0023 — each signal a computation collects, end to end: the owner's Recompute now over HTTP, the worker
// against a fake TMDb, and the candidates and reasons it stored. A collection's released parts the library lacks
// ("2 of 3"); a director's and a creator's works once two of theirs are seeds; the catalog's recommendations weighed by
// how many seeds recommend them, naming those seeds' titles; what a release source just published; and the catalog's
// best-rated works for a tab the library holds fewer than ten works of, listed only once a source carries them.
public sealed class RecommendationSignalsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>TMDb's movie genre ids (VendorSamples/Tmdb/genres-movie.json): Action, Drama, Animation, Documentary.</summary>
    private const int Action = 28;
    private const int Drama = 18;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _library = null!;
    private IntegrationConnectionEntity _catalog = null!;
    private FakeTmdbServer _tmdb = null!;

    public RecommendationSignalsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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

    [Fact]
    public async Task Recompute_recommends_a_collections_released_parts_the_library_lacks_with_held_of_total()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Чужий", 1979, tmdbId: "348");
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        _tmdb.AddWork(Film(348, "Чужий", "1979-05-25", collectionId: 8091));
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", collectionId: 8091));
        _tmdb.AddWork(Film(8077, "Чужий 3", "1992-05-22"));
        // Still to come: it counts neither as a part to recommend nor in the total.
        _tmdb.AddWork(Film(999001, "Чужий: Наступний", "2099-01-01"));
        _tmdb.AddCollection(8091, "Чужий (Колекція)", 348, 679, 8077, 999001);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((RecommendationRunStatus.Succeeded, 2, 1), (run.Status, run.SeedCount, run.CandidateCount));
        var stored = await ReadStoredAsync(cancellationToken);
        var part = Assert.Single(stored);
        Assert.Equal(("8077", RecommendationTab.Movies, false, (string?)null), (part.CatalogId, part.Tab, part.RequiresAvailability, part.MissingSeasons));
        // "2 of 3": the two held of the three released.
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Collection, "Чужий (Колекція)", 2, 3)], part.Reasons);
        // One call for the collection, shared by both seeds of it.
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/collection/8091");
    }

    [Fact]
    public async Task Recompute_recommends_a_directors_released_films_once_two_seeds_are_theirs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Початок", 2010, tmdbId: "27205");
        await _works.CreateMovie(_library, "Інтерстеллар", 2014, tmdbId: "157336");
        await _works.CreateMovie(_library, "Дюна", 2021, tmdbId: "438631");
        _tmdb.AddPerson(525, "Christopher Nolan");
        _tmdb.AddPerson(137427, "Denis Villeneuve");
        _tmdb.AddWork(Film(27205, "Початок", "2010-07-15", directors: [525]));
        _tmdb.AddWork(Film(157336, "Інтерстеллар", "2014-11-05", directors: [525]));
        _tmdb.AddWork(Film(1124, "Престиж", "2006-10-17", directors: [525]));
        _tmdb.AddWork(Film(999002, "Наступний фільм Нолана", null, directors: [525]));
        // One seed of his: not enough to say the owner follows him.
        _tmdb.AddWork(Film(438631, "Дюна", "2021-09-15", directors: [137427]));
        _tmdb.AddWork(Film(27576, "Полонянки", "2013-09-19", directors: [137427]));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var stored = await ReadStoredAsync(cancellationToken);
        var film = Assert.Single(stored);
        Assert.Equal("1124", film.CatalogId);
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Director, "Christopher Nolan", 2)], film.Reasons);
        Assert.DoesNotContain(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/person/137427/combined_credits");
    }

    [Fact]
    public async Task Recompute_recommends_a_creators_released_shows_once_two_show_seeds_are_theirs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateSeries(_library, "Пуститься берега", 2008, tmdbTvId: "1396");
        await _works.CreateSeries(_library, "Краще подзвоніть Солу", 2015, tmdbTvId: "60059");
        _tmdb.AddPerson(66633, "Vince Gilligan");
        _tmdb.AddWork(Show(1396, "Пуститься берега", "2008-01-20", creators: [66633]));
        _tmdb.AddWork(Show(60059, "Краще подзвоніть Солу", "2015-02-08", creators: [66633]));
        _tmdb.AddWork(Show(201834, "Плюрибус", "2025-11-06", creators: [66633]));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var show = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal((MediaWorkKind.Series, "201834", RecommendationTab.Series), (show.Kind, show.CatalogId, show.Tab));
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Creator, "Vince Gilligan", 2)], show.Reasons);
    }

    [Fact]
    public async Task Recompute_weighs_a_recommended_work_by_its_recommenders_and_names_their_titles()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        await _works.CreateMovie(_library, "Хижак", 1987, tmdbId: "106");
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", recommendations: [218, 280]));
        _tmdb.AddWork(Film(106, "Хижак", "1987-06-12", recommendations: [218]));
        _tmdb.AddWork(Film(218, "Термінатор", "1984-10-26"));
        _tmdb.AddWork(Film(280, "Термінатор 2", "1991-07-03"));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var stored = (await ReadStoredAsync(cancellationToken)).ToDictionary(candidate => candidate.CatalogId);
        Assert.Equal(["218", "280"], stored.Keys.Order());
        // The library's display titles, in its title order; Held counts the recommending catalog works (the seeds).
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Recommended, Held: 2, Titles: "Хижак\nЧужі")], stored["218"].Reasons);
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Recommended, Held: 1, Titles: "Чужі")], stored["280"].Reasons);
        // Two points per doubling of the recommenders, 2·log2(1 + n): two (about 3.17) outweigh one (2).
        Assert.True(stored["218"].Score > stored["280"].Score + 0.9);
    }

    [Fact]
    public async Task Recompute_counts_a_film_the_library_holds_twice_as_one_recommender_and_names_its_title_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two library works of one catalog work — the film as two libraries reported it — and one other film.
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        await _works.CreateMovie(_library, "Хижак", 1987, tmdbId: "106");
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", recommendations: [218]));
        _tmdb.AddWork(Film(106, "Хижак", "1987-06-12", recommendations: [218]));
        _tmdb.AddWork(Film(218, "Термінатор", "1984-10-26"));

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Two seeds from three library works: Held counts the catalog works recommending it, and each title is named once.
        Assert.Equal((RecommendationRunStatus.Succeeded, 2, 1), (run.Status, run.SeedCount, run.CandidateCount));
        var terminator = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal("218", terminator.CatalogId);
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.Recommended, Held: 2, Titles: "Хижак\nЧужі")], terminator.Reasons);
        // One read of the film's details, however many library works stand for it.
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == "/3/movie/679");
    }

    [Fact]
    public async Task Recompute_names_at_most_five_recommending_titles_and_counts_them_all()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] titles = ["Альфа", "Бета", "Гамма", "Дельта", "Епсилон", "Зета"];
        for (var index = 0; index < titles.Length; index++)
        {
            await _works.CreateMovie(_library, titles[index], 2001 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, titles[index], $"{2001 + index}-01-01", recommendations: [500]));
        }

        _tmdb.AddWork(Film(500, "Омега", "2010-01-01"));

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var omega = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal(
            [new StoredRecommendationReason(RecommendationReasonKind.Recommended, Held: 6, Titles: "Альфа\nБета\nГамма\nДельта\nЕпсилон")],
            omega.Reasons);
    }

    [Fact]
    public async Task Recompute_sends_each_list_of_ids_it_reads_by_as_one_parameter_however_long()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        int[] recommended = [.. Enumerable.Range(500, 12)];
        foreach (var id in recommended)
            _tmdb.AddWork(Film(id, $"Рекомендований {id}", "2010-01-01"));
        for (var index = 0; index < 12; index++)
        {
            await _works.CreateMovie(_library, $"Фільм {index}", 2000 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, $"Фільм {index}", $"{2000 + index}-01-01", recommendations: recommended));
        }

        // MAM-33 — EF Core 10 sends each value of a list as a parameter of its own unless told otherwise (EF.Parameter),
        // and a library's or a run's thousands of ids would run into SQLite's limit on them. Twelve library works, twelve
        // recommended ones: a read sending a parameter per id has more than a read of a few values ever needs.
        string? readWithAParameterPerId = null;
        _factory.DbCommandHook.BeforeNextCommand(
            command =>
            {
                if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || command.Parameters.Count <= 8)
                    return false;
                readWithAParameterPerId = command.CommandText;
                return true;
            },
            _ => Task.CompletedTask);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((RecommendationRunStatus.Succeeded, 12, 12), (run.Status, run.SeedCount, run.CandidateCount));
        Assert.Null(readWithAParameterPerId);
    }

    [Fact]
    public async Task Recompute_recommends_what_a_release_source_published_lately_with_its_name_and_date()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await new IntegrationConnectionSeeder(_factory.Scope).Create("torznab", "Toloka", apiKey: JellyfinApiKey);
        var works = new CatalogWorkSeeder(_factory.Scope);
        var fresh = await works.Create(MediaWorkKind.Movie, "1061474", "Супермен", new DateOnly(2025, 7, 9), [Action], catalog: _catalog);
        var stale = await works.Create(MediaWorkKind.Movie, "1234821", "Світ Юрського періоду", new DateOnly(2025, 7, 1), [Action], catalog: _catalog);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var published = now.AddHours(-30);
        var feed = new SourceFeedEntrySeeder(_factory.Scope);
        await feed.Create(toloka, "Супермен / Superman (2025) WEB-DL 1080p", fresh, seenUtc: now.AddDays(-2), publishedUtc: published);
        // Seen 15 days ago: what the tracker had all along, not news.
        await feed.Create(toloka, "Світ Юрського періоду: Відродження (2025)", stale, seenUtc: now.AddDays(-15), publishedUtc: now.AddDays(-15));
        // Not resolved to any work: nothing to recommend.
        await feed.Create(toloka, "Щось невідоме (2025)", resolvedTo: null);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var superman = Assert.Single(await ReadStoredAsync(cancellationToken));
        Assert.Equal(("1061474", RecommendationTab.Movies, false), (superman.CatalogId, superman.Tab, superman.RequiresAvailability));
        var reason = Assert.Single(superman.Reasons);
        Assert.Equal((RecommendationReasonKind.NewAtSource, "Toloka"), (reason.Kind, reason.Name));
        Assert.Equal(published, reason.DateUtc!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Recompute_fills_a_tab_with_few_seeds_with_its_best_rated_works_listed_only_once_available()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_owner.User, [RecommendationTab.Movies]);
        await _works.CreateMovie(_library, "Чужі", 1986, tmdbId: "679");
        _tmdb.AddWork(Film(679, "Чужі", "1986-07-18", recommendations: [278]));
        _tmdb.AddWork(Film(278, "Втеча з Шоушенка", "1994-09-23"));
        _tmdb.AddWork(Film(238, "Хрещений батько", "1972-03-14"));
        _tmdb.AddWork(Film(240, "Хрещений батько 2", "1974-12-20"));
        _tmdb.AddDiscover(MediaWorkKind.Movie, 1, 278, 238);
        _tmdb.AddDiscover(MediaWorkKind.Movie, 2, 240);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var stored = (await ReadStoredAsync(cancellationToken)).ToDictionary(candidate => candidate.CatalogId);
        Assert.Equal(["238", "240", "278"], stored.Keys.Order());
        // A fallback alone waits for a release source to carry it; one the library's own seed recommends too doesn't.
        Assert.Equal((true, true, false), (stored["238"].RequiresAvailability, stored["240"].RequiresAvailability, stored["278"].RequiresAvailability));
        Assert.Equal([new StoredRecommendationReason(RecommendationReasonKind.TopRated)], stored["240"].Reasons);
        Assert.Equal(
            [RecommendationReasonKind.Recommended, RecommendationReasonKind.TopRated],
            stored["278"].Reasons.Select(reason => reason.Kind));
        // The Movies tab's genres: none required, Animation and Documentary left out; two pages, films only.
        var discovered = _factory.TmdbHttp.Requests.Where(request => request.Uri.AbsolutePath.StartsWith("/3/discover/", StringComparison.Ordinal)).ToList();
        Assert.Equal(["/3/discover/movie", "/3/discover/movie"], discovered.Select(request => request.Uri.AbsolutePath));
        Assert.All(discovered, request => Assert.Contains("without_genres=16,99", Uri.UnescapeDataString(request.Uri.Query), StringComparison.Ordinal));
        Assert.All(discovered, request => Assert.DoesNotContain("with_genres=", Uri.UnescapeDataString(request.Uri.Query), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recompute_asks_no_best_rated_works_for_a_tab_with_ten_seeds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_owner.User, [RecommendationTab.Movies]);
        for (var index = 0; index < 10; index++)
        {
            await _works.CreateMovie(_library, $"Фільм {index}", 2000 + index, tmdbId: $"{100 + index}");
            _tmdb.AddWork(Film(100 + index, $"Фільм {index}", $"{2000 + index}-01-01"));
        }

        _tmdb.AddWork(Film(238, "Хрещений батько", "1972-03-14"));
        _tmdb.AddDiscover(MediaWorkKind.Movie, 1, 238);

        //Act
        var run = await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Ten seeds are enough of the owner's taste to recommend from.
        Assert.Equal((10, 0), (run.SeedCount, run.CandidateCount));
        Assert.DoesNotContain(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath.StartsWith("/3/discover/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recompute_asks_the_cartoon_and_documentary_tabs_genres_of_films_and_shows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new UserRecommendationSettingsSeeder(_factory.Scope);
        await settings.Save(_owner.User, [RecommendationTab.Cartoons, RecommendationTab.Documentaries]);

        //Act
        await _factory.RecomputeRecommendationsAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Cartoons: with Animation, without Documentary; Documentaries: with Documentary. Both of films and of shows;
        // an empty first page ends each.
        var discovered = _factory.TmdbHttp.Requests
            .Where(request => request.Uri.AbsolutePath.StartsWith("/3/discover/", StringComparison.Ordinal))
            .Select(request => (request.Uri.AbsolutePath, Genres: GenresAsked(request)))
            .ToList();
        Assert.Equal(
            [
                ("/3/discover/movie", "with_genres=16 without_genres=99"),
                ("/3/discover/tv", "with_genres=16 without_genres=99"),
                ("/3/discover/movie", "with_genres=99"),
                ("/3/discover/tv", "with_genres=99"),
            ],
            discovered);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static FakeTmdbWork Film(
        int id, string title, string? releaseDate, int? collectionId = null, int[]? directors = null, int[]? recommendations = null) =>
        new(MediaWorkKind.Movie, id, title, ReleaseDate: releaseDate, GenreIds: [Action], CollectionId: collectionId, DirectorIds: directors, RecommendationIds: recommendations);

    private static FakeTmdbWork Show(int id, string title, string? releaseDate, int[]? creators = null) =>
        new(MediaWorkKind.Series, id, title, ReleaseDate: releaseDate, GenreIds: [Drama], DirectorIds: creators);

    private static string GenresAsked(RecordedHttpRequest request) =>
        string.Join(' ', Uri.UnescapeDataString(request.Uri.Query).TrimStart('?').Split('&').Where(parameter => parameter.Contains("_genres=", StringComparison.Ordinal)));

    private async Task<IReadOnlyList<StoredRecommendation>> ReadStoredAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredRecommendation> stored = [];
        await _factory.WithDbAsync(async db => stored = await StoredRecommendation.ReadAsync(db, cancellationToken));
        return stored;
    }
}
