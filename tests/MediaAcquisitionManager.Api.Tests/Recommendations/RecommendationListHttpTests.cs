using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — a page of one recommendation tab (GET /api/recommendations): every signed-in user reads the list the last run
// stored for everyone, through their own tabs — a tab switched off in their Profile is a 400, not an empty page. The
// answer is the JSON the SPA reads (the frontend contract): seasons as numbers, titles as strings, a film's TMDb id as
// text and none for a show, the search text from the release query title and the year, the poster's own URL, the
// availability check or null, whether a catalog is connected and the latest run. Ordered by score, votes, title and id,
// paged by the server. Its filters and facets are RecommendationListFiltersHttpTests', the download jobs
// RecommendationListDownloadsHttpTests', "Not interested" RecommendationDismissalsHttpTests'.
public sealed class RecommendationListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private AuthorizedUser _anna = null!;

    public RecommendationListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _anna = await new UserSeeder(_factory.Scope).CreateAuthorized("anna");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync(RecommendationTab.Movies, "603", "Матриця");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("", "Choose a tab: Movies, Series, Cartoons or Documentaries.")]
    [InlineData("?tab=", "Choose a tab: Movies, Series, Cartoons or Documentaries.")]
    [InlineData("?tab=Anime", "“Anime” is not a recommendation tab. Use Movies, Series, Cartoons or Documentaries.")]
    // By name only: a number would be read as the tab with that value.
    [InlineData("?tab=1", "“1” is not a recommendation tab. Use Movies, Series, Cartoons or Documentaries.")]
    [InlineData("?tab=Movies,%20Series", "“Movies, Series” is not a recommendation tab. Use Movies, Series, Cartoons or Documentaries.")]
    [InlineData(
        "?tab=AVeryLongTabNameThatGoesOnAndOnAndOn",
        "“AVeryLongTabNameThatGoesOnAndOnA…” is not a recommendation tab. Use Movies, Series, Cartoons or Documentaries.")]
    public async Task List_without_a_tab_or_with_one_it_doesnt_know_is_400_with_why(string query, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl + query, _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_reads_the_tab_in_any_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await RecommendAsync(RecommendationTab.Documentaries, "1001", "Планета Земля");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=%20documentaries%20", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([work.Id], (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_of_a_tab_the_caller_switched_off_is_400_while_another_user_still_reads_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync(RecommendationTab.Movies, "603", "Матриця");
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_anna.User, [RecommendationTab.Series]);
        var bohdan = await new UserSeeder(_factory.Scope).CreateAuthorized("bohdan");

        //Act
        var annas = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);
        var bohdans = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", bohdan.AccessToken);

        //Assert
        // Her tabs (Vasyl's decision 5): one she switched off is no list of hers, even by its URL. His are the defaults.
        Assert.Equal(HttpStatusCode.BadRequest, annas.StatusCode);
        Assert.Equal("This tab is switched off in your profile.", await annas.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, bohdans.StatusCode);
        Assert.Single((await ReadListAsync(bohdans, cancellationToken)).Items);
    }

    [Fact]
    public async Task List_answers_the_json_the_page_reads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var genres = new CatalogGenreSeeder(_factory.Scope);
        await genres.Create(MediaWorkKind.Series, 18, "Драма");
        await genres.Create(MediaWorkKind.Series, 37, "Вестерн");
        var publishedUtc = _factory.Time.GetUtcNow().UtcDateTime.AddDays(-2);
        var work = await _works.Create(
            MediaWorkKind.Series,
            "73586",
            "Єллоустоун",
            new DateOnly(2018, 6, 20),
            [37, 18],
            rating: 8.2,
            voteCount: 2900,
            originalTitle: "Yellowstone",
            posterPath: "/peNC0eyc3TQJa6x4TdKcBPNP4t0.jpg");
        await _candidates.Create(
            work,
            RecommendationTab.Series,
            score: 9,
            missingSeasons: "4,5",
            reasons:
            [
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.MissingSeasons, Seasons = "4,5" },
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.Recommended, Held = 7, Titles = "1883\nTaylor Sheridan's Lioness" },
                new RecommendationReasonEntity { Kind = RecommendationReasonKind.NewAtSource, Name = "Toloka", DateUtc = publishedUtc },
            ]);
        var checkedUtc = _factory.Time.GetUtcNow().UtcDateTime.AddHours(-3);
        await new ReleaseAvailabilitySeeder(_factory.Scope).Create(
            work, releaseCount: 4, bestResolution: 2160, hasUkrainianAudio: true, maxSeeders: 24, ago: TimeSpan.FromHours(3));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Series", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(work.Id.ToString(), item.GetProperty("id").GetString());
        Assert.Equal("Series", item.GetProperty("kind").GetString());
        Assert.Equal("73586", item.GetProperty("catalogId").GetString());
        Assert.Equal("Єллоустоун", item.GetProperty("title").GetString());
        Assert.Equal("Yellowstone", item.GetProperty("originalTitle").GetString());
        Assert.Equal(2018, item.GetProperty("year").GetInt32());
        // The catalog's names of its kind's list, alphabetical.
        Assert.Equal(["Вестерн", "Драма"], item.GetProperty("genres").EnumerateArray().Select(genre => genre.GetString()));
        Assert.Equal(8.2, item.GetProperty("rating").GetDouble());
        Assert.Equal(2900, item.GetProperty("voteCount").GetInt32());
        Assert.Equal("/api/recommendations/posters/peNC0eyc3TQJa6x4TdKcBPNP4t0.jpg", item.GetProperty("posterUrl").GetString());
        // A show: the Search page takes a film's id only, so none — null, not absent.
        Assert.Equal(JsonValueKind.Null, item.GetProperty("searchTmdbId").ValueKind);
        Assert.Equal("Yellowstone (2018)", item.GetProperty("searchText").GetString());
        Assert.False(item.GetProperty("downloading").GetBoolean());

        var reasons = item.GetProperty("reasons");
        Assert.Equal(["MissingSeasons", "Recommended", "NewAtSource"], reasons.EnumerateArray().Select(reason => reason.GetProperty("kind").GetString()));
        // Numbers, never the stored "4,5".
        Assert.Equal([JsonValueKind.Number, JsonValueKind.Number], reasons[0].GetProperty("seasons").EnumerateArray().Select(season => season.ValueKind));
        Assert.Equal([4, 5], reasons[0].GetProperty("seasons").EnumerateArray().Select(season => season.GetInt32()));
        Assert.Empty(reasons[0].GetProperty("titles").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, reasons[0].GetProperty("name").ValueKind);
        Assert.Equal(["1883", "Taylor Sheridan's Lioness"], reasons[1].GetProperty("titles").EnumerateArray().Select(title => title.GetString()));
        Assert.Equal(7, reasons[1].GetProperty("held").GetInt32());
        Assert.Empty(reasons[1].GetProperty("seasons").EnumerateArray());
        Assert.Equal("Toloka", reasons[2].GetProperty("name").GetString());
        Assert.EndsWith("Z", reasons[2].GetProperty("dateUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(publishedUtc, reasons[2].GetProperty("dateUtc").GetDateTime().ToUniversalTime(), TimeSpan.FromMilliseconds(1));

        var availability = item.GetProperty("availability");
        Assert.EndsWith("Z", availability.GetProperty("checkedUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(checkedUtc, availability.GetProperty("checkedUtc").GetDateTime().ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        Assert.True(availability.GetProperty("found").GetBoolean());
        Assert.Equal(4, availability.GetProperty("releaseCount").GetInt32());
        Assert.Equal(2160, availability.GetProperty("bestResolution").GetInt32());
        Assert.True(availability.GetProperty("hasUkrainianAudio").GetBoolean());
        Assert.Equal(24, availability.GetProperty("maxSeeders").GetInt32());

        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(25, root.GetProperty("pageSize").GetInt32());
        Assert.False(root.GetProperty("catalogConnected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("lastRun").ValueKind);
        Assert.Equal(
            ["reasons", "genres", "decades", "kinds"],
            root.GetProperty("facets").EnumerateObject().Select(facet => facet.Name));
    }

    [Fact]
    public async Task List_gives_a_film_its_tmdb_id_as_text_for_the_search_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync(RecommendationTab.Movies, "286217", "Марсіянин", originalTitle: "The Martian");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tmdbId = document.RootElement.GetProperty("items")[0].GetProperty("searchTmdbId");
        Assert.Equal(JsonValueKind.String, tmdbId.ValueKind);
        Assert.Equal("286217", tmdbId.GetString());
    }

    [Theory]
    // The release sources' query title (RecommendationQueryTitle): a Latin original finds the most at Toloka…
    [InlineData("Марсіянин", "The Martian", 2015, "The Martian (2015)")]
    [InlineData("Амелі", "Le Fabuleux Destin d'Amélie Poulain", 2001, "Le Fabuleux Destin d'Amélie Poulain (2001)")]
    [InlineData("Леон", "Léon", 1994, "Léon (1994)")]
    // …and an original in another script is in neither part of a listing, so the catalog's title is searched.
    [InlineData("Твоє ім'я", "君の名は。", 2016, "Твоє ім'я (2016)")]
    [InlineData("Брат", "Брат", 1997, "Брат (1997)")]
    // No date, no year in brackets.
    [InlineData("Дюна: Пророцтво", "Dune: Prophecy", null, "Dune: Prophecy")]
    public async Task List_search_text_is_the_query_title_with_the_year(string title, string originalTitle, int? year, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync(RecommendationTab.Movies, "100", title, year: year, originalTitle: originalTitle);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        var item = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(expected, item.SearchText);
        Assert.Equal(year, item.Year);
    }

    [Fact]
    public async Task List_gives_no_poster_url_or_availability_before_there_is_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync(RecommendationTab.Movies, "603", "Матриця");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("posterUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("availability").ValueKind);
    }

    [Fact]
    public async Task List_gives_a_check_that_found_nothing_as_not_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var work = await RecommendAsync(RecommendationTab.Movies, "603", "Матриця");
        await new ReleaseAvailabilitySeeder(_factory.Scope).Create(work, releaseCount: 0, ago: TimeSpan.FromDays(3));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        var availability = Assert.Single((await ReadListAsync(response, cancellationToken)).Items).Availability;
        Assert.NotNull(availability);
        Assert.Equal((false, 0, (int?)null, false, (int?)null), (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
    }

    [Fact]
    public async Task List_holds_the_tabs_own_works_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = await RecommendAsync(RecommendationTab.Movies, "603", "Матриця");
        await RecommendAsync(RecommendationTab.Series, "1399", "Гра престолів", kind: MediaWorkKind.Series);
        await RecommendAsync(RecommendationTab.Cartoons, "862", "Історія іграшок");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([film.Id], list.Items.Select(item => item.Id));
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task List_orders_by_score_then_votes_then_title_then_catalog_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var low = await RecommendAsync(RecommendationTab.Movies, "1", "Аеро", score: 1, voteCount: 9000);
        var tiedOnVotesB = await RecommendAsync(RecommendationTab.Movies, "2", "Б", score: 5, voteCount: 100);
        var tiedOnTitleId9 = await RecommendAsync(RecommendationTab.Movies, "9", "А", score: 5, voteCount: 100);
        var tiedOnTitleId10 = await RecommendAsync(RecommendationTab.Movies, "10", "А", score: 5, voteCount: 100);
        var moreVotes = await RecommendAsync(RecommendationTab.Movies, "3", "Я", score: 5, voteCount: 500);
        var best = await RecommendAsync(RecommendationTab.Movies, "4", "Ю", score: 7.5, voteCount: 10);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        // The score; then how many rated it; then the title and the catalog id, both ordinal ("10" before "9").
        Assert.Equal(
            [best.Id, moreVotes.Id, tiedOnTitleId10.Id, tiedOnTitleId9.Id, tiedOnVotesB.Id, low.Id],
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_pages_the_ordered_tab_and_counts_every_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = new List<CatalogWorkEntity>();
        for (var index = 0; index < 5; index++)
            works.Add(await RecommendAsync(RecommendationTab.Movies, $"{100 + index}", $"Фільм {index}", score: 10 - index));

        //Act
        var second = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&page=2&pageSize=2", _anna.AccessToken);
        var last = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&page=3&pageSize=2", _anna.AccessToken);
        var past = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&page=4&pageSize=2", _anna.AccessToken);

        //Assert
        var secondPage = await ReadListAsync(second, cancellationToken);
        Assert.Equal([works[2].Id, works[3].Id], secondPage.Items.Select(item => item.Id));
        Assert.Equal((5, 2, 2), (secondPage.TotalCount, secondPage.Page, secondPage.PageSize));
        Assert.Equal([works[4].Id], (await ReadListAsync(last, cancellationToken)).Items.Select(item => item.Id));
        var pastPage = await ReadListAsync(past, cancellationToken);
        Assert.Empty(pastPage.Items);
        Assert.Equal((5, 4), (pastPage.TotalCount, pastPage.Page));
    }

    [Theory]
    [InlineData("page=0", "Page must be 1 or more.")]
    [InlineData("page=-1", "Page must be 1 or more.")]
    [InlineData("page=abc", "Page must be 1 or more.")]
    [InlineData("page=99999999999", "Page must be 1 or more.")]
    [InlineData("pageSize=0", "Page size must be between 1 and 100.")]
    [InlineData("pageSize=101", "Page size must be between 1 and 100.")]
    [InlineData("pageSize=ten", "Page size must be between 1 and 100.")]
    public async Task List_with_a_page_or_page_size_it_doesnt_take_is_400_with_why(string query, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&{query}", _anna.AccessToken);

        //Assert
        // The { error } sentence even for a page that isn't a number: never a validation body.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task List_of_a_far_page_is_empty_rather_than_an_overflow()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync(RecommendationTab.Movies, "603", "Матриця");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&page={int.MaxValue}&pageSize=100", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Empty(list.Items);
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task List_says_a_catalog_is_connected_and_gives_the_latest_run_without_the_owners_details()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var owner = await new UserSeeder(_factory.Scope).CreateOwner("owner");
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var run = await new RecommendationRunSeeder(_factory.Scope).Create(
            RecommendationRunStatus.Failed, requestedBy: owner, createdUtc: at, error: "TMDb refused the key (401 Unauthorized).");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        // runs/latest's rule: who asked and why it failed are the owner's.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.True(list.CatalogConnected);
        Assert.Equal(
            new RecommendationRunDto(run.Id, RecommendationRunStatus.Failed, null, null, null, RecommendationRunTrigger.Requested, null, at, at, at, null, null, null),
            list.LastRun);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_reads_without_writing_anything_or_asking_the_catalog()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        FakeTmdbServer.Arm(_factory.TmdbHttp);
        await RecommendAsync(RecommendationTab.Movies, "603", "Матриця", posterPath: "/matrix.jpg");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.TmdbHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReleaseAvailabilities.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A catalog work of <paramref name="kind"/> released in <paramref name="year"/>, recommended in
    /// <paramref name="tab"/> with <paramref name="score"/> for one Recommended reason.</summary>
    private async Task<CatalogWorkEntity> RecommendAsync(
        RecommendationTab tab,
        string catalogId,
        string title,
        MediaWorkKind kind = MediaWorkKind.Movie,
        int? year = 2015,
        double score = 1,
        int voteCount = 1000,
        string? originalTitle = null,
        string? posterPath = null)
    {
        var work = await _works.Create(
            kind,
            catalogId,
            title,
            year is { } released ? new DateOnly(released, 6, 1) : null,
            voteCount: voteCount,
            originalTitle: originalTitle,
            posterPath: posterPath);
        await _candidates.Create(work, tab, score);
        return work;
    }

    private static async Task<RecommendationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
