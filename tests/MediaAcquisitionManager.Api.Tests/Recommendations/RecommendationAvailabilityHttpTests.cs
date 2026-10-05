using System.Net;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33, ADR 0024 — what one pass of the availability check asks a release source and stores per work, over the real
// Torznab adapter with the network faked by Toloka's real answers: a film asked with the movie search alone, by its
// original title and year when that title is Latin; a show with the TV search alone, never a year; what counts as a
// release of the work (its kind, an id or its title and year from the title or the description, for a held series a
// missing season or none named); what is stored (how many, the best resolution, Ukrainian audio, the most seeders, or
// not found); a source that fails storing nothing; no source, no search; and the checker's own loop.
// When it searches and for whom are RecommendationAvailabilityScheduleHttpTests', the budget
// RecommendationAvailabilityBudgetHttpTests'.
public sealed class RecommendationAvailabilityHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string FeedAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    // The categories of Toloka's caps sample each family searches in (ADR 0020).
    private const string MovieCategories = "cat=2000,2020,2040,2070,5070,5080";
    private const string TvCategories = "cat=5000,5040,5050,5060,5070,5080";

    private const string MovieCategory = "<category>2000</category>";
    private const string TvCategory = "<category>5040</category>";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private IIntegrationConnectionSeeder _integrations = null!;
    private UserEntity _anna = null!;
    private FakeTorznabServer _toloka = null!;

    public RecommendationAvailabilityHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _anna = await new UserSeeder(_factory.Scope).Create("anna");
        _toloka = new FakeTorznabServer(FeedAddress, ApiKey) { SearchXml = TorznabSamples.Feed() };
        _toloka.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Check_stores_the_count_best_resolution_ukrainian_audio_and_most_seeders_of_a_film()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.MovieSearch(FeedAddress, ApiKey);
        var film = await Interstellar();
        await _candidates.Create(film, RecommendationTab.Movies);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // Toloka's sixteen Interstellar releases in the movie categories (the audio-only one is not asked for): up to
        // 2160p, most with Ukrainian audio, 89 seeders at most.
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, SampleMovieCount, (int?)2160, true, (int?)89, now),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders, availability.CheckedUtc));
        // The search counts against the user whose list it served, in the work's tab.
        var search = Assert.Single(await SearchesAsync(cancellationToken));
        Assert.Equal((_anna.Id, RecommendationTab.Movies, film.Id, now), (search.UserId, search.Tab, search.CatalogWorkId, search.SearchedUtc));
    }

    [Fact]
    public async Task Check_asks_a_film_with_the_movie_search_alone_by_its_original_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.MovieSearch(FeedAddress, ApiKey);
        await _candidates.Create(await Interstellar(), RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // The movie tiers alone — the year in the text, as Toloka's movie search takes no year — and the first that
        // lists a release ends them; never the TV search, whose releases can't be a film.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&q=Interstellar%202014&{MovieCategories}&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Check_asks_a_show_with_the_tv_search_alone_and_counts_the_releases_of_that_show()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.TvSearchXml = TorznabSamples.TvSearch(FeedAddress, ApiKey);
        var show = await Yellowstone();
        await _candidates.Create(show, RecommendationTab.Series);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // The TV search alone, by the title without a year (ADR 0020: a release names its seasons' years).
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&{TvCategories}&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        // The five packs of the 2018 show; not the BBC's "Yellowstone (S1) (2009)", a show of another year, nor the
        // three documentary films beside it.
        var availability = await AvailabilityAsync(show.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, 5, (int?)1080, true, (int?)68),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
    }

    [Fact]
    public async Task Check_counts_a_documentary_film_filed_under_tv_documentary_as_a_film()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.FilmInTvCategorySearch(FeedAddress, ApiKey);
        var film = await _works.Create(
            MediaWorkKind.Movie,
            "1530001",
            "Шумахер 1994: Народження легенди",
            new DateOnly(2026, 9, 18),
            genreIds: [99],
            originalTitle: "Schumacher '94 - Die Geburt einer Legende");
        await _candidates.Create(film, RecommendationTab.Documentaries);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // The movie search, which asks TV/Documentary too (Vasyl's decision 6), by the German original and the year.
        var movieSearch = Assert.Single(_factory.TorznabHttp.Requests, request => Parameter(request.Uri, "t") == "movie");
        Assert.Equal("Schumacher '94 - Die Geburt einer Legende 2026", Parameter(movieSearch.Uri, "q"));
        Assert.DoesNotContain(_factory.TorznabHttp.Requests, request => Parameter(request.Uri, "t") == "tvsearch");
        // All five: the one in Movies and the four in TV/Documentary that name no season.
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, TorznabSamples.FilmInTvCategoryItemCount, (int?)2160, true, (int?)33),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
    }

    [Theory]
    [InlineData("Інтерстеллар", "Interstellar", 2014, "Interstellar 2014")]
    [InlineData("Амелі", "Le Fabuleux Destin d'Amélie Poulain", 2001, "Le Fabuleux Destin d'Amélie Poulain 2001")]
    [InlineData("Твоє ім'я", "君の名は。", 2016, "Твоє ім'я 2016")]
    [InlineData("Брат", "Брат", 1997, "Брат 1997")]
    public async Task Check_searches_by_the_original_title_in_latin_script_else_by_the_catalog_title(
        string title, string originalTitle, int year, string expectedQuery)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var film = await _works.Create(MediaWorkKind.Movie, "1000", title, new DateOnly(year, 6, 1), originalTitle: originalTitle);
        await _candidates.Create(film, RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // Toloka's listings are "Ukrainian / Original": a Latin original is the part Prowlarr's title keeps, and an
        // original in another script is in neither part. The first tier, with the year; nothing found, the title alone
        // follows.
        var movieSearches = _factory.TorznabHttp.Requests.Where(request => Parameter(request.Uri, "t") == "movie").ToList();
        Assert.Equal([expectedQuery, expectedQuery[..^" 0000".Length]], movieSearches.Select(request => Parameter(request.Uri, "q")));
    }

    [Fact]
    public async Task Check_counts_only_releases_of_the_work_by_its_kind_and_an_id_or_its_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var link = $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=";
        _toloka.SearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014) BDRip 1080p Ukr/Eng", link + "1", extra: MovieCategory + Seeders(5)),
            // No year, so no title to read — but the listing's TMDb id is the film's.
            TorznabSamples.Item("Interstellar BDRip 720p", link + "2", extra: MovieCategory + """<torznab:attr name="tmdbid" value="157336" />"""),
            // Its Ukrainian title and year are only in the description.
            TorznabSamples.Item(
                "Interstellar BDRip 480p", link + "3", extra: MovieCategory + Seeders(2) + "<description>Інтерстеллар (2014) BDRip 480p</description>"),
            // Another film whose title begins the same, the same title of another year, and nothing to tell at all.
            TorznabSamples.Item("Interstellar Wars (2016) 2160p DVDRip", link + "4", extra: MovieCategory + Seeders(90)),
            TorznabSamples.Item("Interstellar (2015) WEB-DL 2160p", link + "5", extra: MovieCategory + Seeders(91)),
            TorznabSamples.Item("Interstellar 2160p", link + "6", extra: MovieCategory + Seeders(92)),
            // A series of that title and year in TV/Anime, which the movie search asks too: not a film.
            TorznabSamples.Item("Interstellar (S1) (2014) WEB-DL 2160p", link + "7", extra: "<category>5070</category>" + Seeders(93)));
        var film = await Interstellar();
        await _candidates.Create(film, RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, 3, (int?)1080, true, (int?)5),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
    }

    [Fact]
    public async Task Check_stores_not_found_when_no_release_is_the_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var film = await Interstellar();
        await _candidates.Create(film, RecommendationTab.Movies);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (false, 0, (int?)null, false, (int?)null, now),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders, availability.CheckedUtc));
    }

    [Fact]
    public async Task Check_counts_for_a_held_series_only_the_releases_of_a_missing_season_or_of_no_season_named()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var link = $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=";
        _toloka.TvSearchXml = TorznabSamples.Feed(
            TorznabSamples.Item("Yellowstone (S1-5) (2018-2024) WEB-DL 1080p Ukr/Eng", link + "1", extra: TvCategory + Seeders(24)),
            TorznabSamples.Item("Yellowstone (S5) (2022-2024) WEB-DL 720p", link + "2", extra: TvCategory + Seeders(18)),
            TorznabSamples.Item("Yellowstone (2018) WEB-DL 1080p", link + "3", extra: TvCategory + Seeders(3)),
            // Seasons the library holds: nothing it lacks.
            TorznabSamples.Item("Yellowstone (S4) (2021) WEB-DL 2160p Ukr", link + "4", extra: TvCategory + Seeders(44)),
            TorznabSamples.Item("Yellowstone (S1-3) (2018-2020) WEB-DL 2160p", link + "5", extra: TvCategory + Seeders(68)));
        var show = await Yellowstone();
        await _candidates.Create(show, RecommendationTab.Series, missingSeasons: "5");

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        var availability = await AvailabilityAsync(show.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, 3, (int?)1080, true, (int?)24),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
    }

    [Fact]
    public async Task Check_counts_a_later_season_pack_of_a_show_begun_by_the_packs_first_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var link = $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=";
        _toloka.TvSearchXml = TorznabSamples.Feed(
            // Season 5's years, not the show's: a show begun in 2022 or before, as the 2018 one was.
            TorznabSamples.Item("Yellowstone (S5) (2022-2024) WEB-DL 1080p", link + "1", extra: TvCategory + Seeders(18)),
            // A show whose second season aired in 2016 began by then: not the 2018 one.
            TorznabSamples.Item("Yellowstone (S2) (2016) WEB-DL 2160p", link + "2", extra: TvCategory + Seeders(70)));
        var show = await Yellowstone();
        await _candidates.Create(show, RecommendationTab.Series);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        var availability = await AvailabilityAsync(show.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, 1, (int?)1080, (int?)18),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.MaxSeeders));
    }

    [Fact]
    public async Task Check_counts_a_series_filed_with_episodes_alone_under_tv_anime_by_its_title_and_year()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var link = $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=";
        // MAM-33 — Toloka's TV/Anime way (VendorSamples README): episodes without a season, the Ukrainian title only in
        // the description, which reads "Зошит смерті", of 2006, once the episodes are cut from it.
        _toloka.TvSearchXml = TorznabSamples.Feed(
            TorznabSamples.Item(
                "Death Note (E1-31 of 37) (2006) BDRip 1080p",
                link + "1",
                extra: "<category>5070</category>" + Seeders(12)
                    + "<description>Зошит смерті (серії 1-31 з 37) / Death Note (E1-31 of 37) (2006) BDRip 1080p</description>"));
        var show = await _works.Create(MediaWorkKind.Series, "13916", "Зошит смерті", new DateOnly(2006, 10, 4), originalTitle: "デスノート");
        await _candidates.Create(show, RecommendationTab.Cartoons);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        var availability = await AvailabilityAsync(show.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, 1, (int?)1080, (int?)12),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.MaxSeeders));
    }

    [Fact]
    public async Task Check_that_no_source_answers_stores_nothing_counts_against_the_budget_and_stops_the_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var first = await Interstellar();
        var second = await _works.Create(MediaWorkKind.Movie, "27205", "Початок", new DateOnly(2010, 7, 15), originalTitle: "Inception");
        await _candidates.Create(first, RecommendationTab.Movies, score: 2);
        await _candidates.Create(second, RecommendationTab.Movies, score: 1);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // One search, which fails at its first request: the second work isn't asked for, as it would fail the same.
        Assert.Single(_factory.TorznabHttp.Requests);
        var search = Assert.Single(await SearchesAsync(cancellationToken));
        Assert.Equal(first.Id, search.CatalogWorkId);
        Assert.Null(await AvailabilityAsync(first.Id, cancellationToken));
        Assert.Null(await AvailabilityAsync(second.Id, cancellationToken));
        // Both stay due: the next pass, with the source answering again, checks them.
        _toloka.Intercept = null;
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        Assert.NotNull(await AvailabilityAsync(first.Id, cancellationToken));
        Assert.NotNull(await AvailabilityAsync(second.Id, cancellationToken));
        Assert.Equal(3, (await SearchesAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Check_of_a_kind_no_source_can_be_asked_for_sends_nothing_counts_nothing_and_goes_on_to_the_next_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        // MAM-33 — a feed with no TV category has no TV search (ReleaseSearchPlanning.ForTv): no show can be asked of it.
        _toloka.CapsXml =
            """
            <caps>
              <searching>
                <search available="yes" supportedParams="q" />
                <movie-search available="yes" supportedParams="q" />
                <tv-search available="yes" supportedParams="q,season" />
              </searching>
              <categories>
                <category id="2000" name="Movies" />
              </categories>
            </caps>
            """;
        _toloka.SearchXml = TorznabSamples.MovieSearch(FeedAddress, ApiKey);
        var first = await Interstellar();
        var show = await Yellowstone();
        var second = await _works.Create(MediaWorkKind.Movie, "27205", "Початок", new DateOnly(2010, 7, 15), originalTitle: "Inception");
        // The budget goes round the tabs: the first film, then the show, then the second film.
        await _candidates.Create(first, RecommendationTab.Movies, score: 2);
        await _candidates.Create(show, RecommendationTab.Series);
        await _candidates.Create(second, RecommendationTab.Movies, score: 1);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // No search was sent for the show, so none counts against the budget and nothing is stored: it stays due. Not
        // a source that is down either, so the pass goes on and checks the second film.
        Assert.DoesNotContain(_factory.TorznabHttp.Requests, request => Parameter(request.Uri, "t") == "tvsearch");
        Assert.Equal(
            new[] { first.Id, second.Id }.Order(),
            (await SearchesAsync(cancellationToken)).Select(search => search.CatalogWorkId).Order());
        Assert.Null(await AvailabilityAsync(show.Id, cancellationToken));
        Assert.NotNull(await AvailabilityAsync(first.Id, cancellationToken));
        Assert.NotNull(await AvailabilityAsync(second.Id, cancellationToken));
    }

    [Fact]
    public async Task Check_without_an_enabled_release_source_searches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey, isEnabled: false);
        await _candidates.Create(await Interstellar(), RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(await SearchesAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Empty(await db.ReleaseAvailabilities.ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Checker_runs_a_pass_as_soon_as_it_starts_and_stops_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var film = await Interstellar();
        await _candidates.Create(film, RecommendationTab.Movies);
        var checker = _factory.Resolve<RecommendationAvailabilityChecker>();
        _factory.Logs.Clear();

        //Act
        // The host's own start and stop, which the test host leaves to the test.
        await checker.StartAsync(cancellationToken);
        var checkedOnce = await WaitForAvailabilityAsync(film.Id, cancellationToken);
        await checker.StopAsync(cancellationToken);

        //Assert
        // The first pass right after the start, without waiting PassInterval; the stop ends the wait for the next one,
        // which is no error. On .NET 10 a stop can also win the race before the loop runs at all.
        Assert.True(checkedOnce);
        Assert.True(checker.ExecuteTask is { Status: TaskStatus.RanToCompletion or TaskStatus.Canceled }, $"{checker.ExecuteTask?.Status}");
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<CatalogWorkEntity> Interstellar() =>
        _works.Create(MediaWorkKind.Movie, "157336", "Інтерстеллар", new DateOnly(2014, 11, 5), originalTitle: "Interstellar");

    private Task<CatalogWorkEntity> Yellowstone() =>
        _works.Create(MediaWorkKind.Series, "73586", "Єллоустоун", new DateOnly(2018, 6, 20), originalTitle: "Yellowstone");

    private static string Seeders(int seeders) => $"""<torznab:attr name="seeders" value="{seeders}" />""";

    /// <summary>The value of the query parameter <paramref name="name"/>, unescaped; <c>null</c> without one.</summary>
    private static string? Parameter(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(pair[0], name, StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "")
            .FirstOrDefault();

    private async Task<ReleaseAvailabilityEntity?> AvailabilityAsync(Guid catalogWorkId, CancellationToken cancellationToken)
    {
        ReleaseAvailabilityEntity? availability = null;
        await _factory.WithDbAsync(async db => availability = await db.ReleaseAvailabilities.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CatalogWorkId == catalogWorkId, cancellationToken));
        return availability;
    }

    private async Task<List<RecommendationSearchEntity>> SearchesAsync(CancellationToken cancellationToken)
    {
        List<RecommendationSearchEntity> searches = [];
        await _factory.WithDbAsync(async db => searches = await db.RecommendationSearches.AsNoTracking().ToListAsync(cancellationToken));
        return searches;
    }

    /// <summary>Whether the work's check is stored within five seconds, read every 50 ms: for the one test that lets
    /// the checker's own loop run.</summary>
    private async Task<bool> WaitForAvailabilityAsync(Guid catalogWorkId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await AvailabilityAsync(catalogWorkId, cancellationToken) is not null)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        return false;
    }
}
