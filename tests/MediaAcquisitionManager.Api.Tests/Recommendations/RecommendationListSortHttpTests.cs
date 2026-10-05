using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — the orders a recommendation tab can be listed in (Vasyl asked to sort the list), each applied by the server
// after the filters and before the page: the score (the default), the weighted rating, the votes, the newest and the
// oldest (a work of no date last either way), the title in the Library page's collation, and what the release sources
// carry. Every order falls back to the score's, so it is total; the facets don't depend on it. A sort it doesn't know —
// by name only — is a 400 with its sentence.
public sealed class RecommendationListSortHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private AuthorizedUser _anna = null!;

    public RecommendationListSortHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _anna = await new UserSeeder(_factory.Scope).CreateAuthorized("anna");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_without_a_sort_is_in_the_score_order_the_same_as_sorted_by_score()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var low = await RecommendAsync("1", "Аеро", score: 1, voteCount: 9000);
        var fewerVotes = await RecommendAsync("2", "Б", score: 5, voteCount: 100);
        var moreVotes = await RecommendAsync("3", "Я", score: 5, voteCount: 500);
        var best = await RecommendAsync("4", "Ю", score: 7.5, voteCount: 10);

        //Act
        var unsorted = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);
        var byScore = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=score", _anna.AccessToken);
        var byTitle = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Title", _anna.AccessToken);

        //Assert
        var list = await ReadListAsync(unsorted, cancellationToken);
        Assert.Equal([best.Id, moreVotes.Id, fewerVotes.Id, low.Id], list.Items.Select(item => item.Id));
        Assert.Equal(list.Items.Select(item => item.Id), (await ReadListAsync(byScore, cancellationToken)).Items.Select(item => item.Id));
        // The order changes what a page holds, never what the filters offer.
        var sortedFacets = (await ReadListAsync(byTitle, cancellationToken)).Facets;
        Assert.Equal(list.Facets.Reasons, sortedFacets.Reasons);
        Assert.Equal(list.Facets.Genres, sortedFacets.Genres);
        Assert.Equal(list.Facets.Decades, sortedFacets.Decades);
        Assert.Equal(list.Facets.Kinds, sortedFacets.Kinds);
    }

    [Fact]
    public async Task List_sorted_by_rating_puts_the_best_weighted_average_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // (votes × rating + 50 × 5.0) / (votes + 50): 5.28, 8.47, 7.80, 6.90; nobody rated the last, the prior 5.0.
        var tenOfThree = await RecommendAsync("1", "Десять з трьох", score: 9, rating: 10.0, voteCount: 3);
        var eightAndAHalfOfMany = await RecommendAsync("2", "Вісім з половиною багатьох", score: 1, rating: 8.5, voteCount: 5000);
        var eightAndAHalfOfFew = await RecommendAsync("3", "Вісім з половиною небагатьох", score: 2, rating: 8.5, voteCount: 200);
        var seven = await RecommendAsync("4", "Сім", score: 3, rating: 7.0, voteCount: 1000);
        var unrated = await RecommendAsync("5", "Без оцінки", score: 4, rating: null, voteCount: 0);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=rating", _anna.AccessToken);

        //Assert
        // A 10.0 of three votes is no top rating; it ranks just over a work nobody rated.
        Assert.Equal(
            [eightAndAHalfOfMany.Id, eightAndAHalfOfFew.Id, seven.Id, tenOfThree.Id, unrated.Id],
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_sorted_by_votes_puts_the_most_rated_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var few = await RecommendAsync("1", "Мало", score: 9, voteCount: 40);
        var many = await RecommendAsync("2", "Багато", score: 1, voteCount: 25000);
        var some = await RecommendAsync("3", "Трохи", score: 5, voteCount: 800);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Votes", _anna.AccessToken);

        //Assert
        Assert.Equal([many.Id, some.Id, few.Id], (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_sorted_newest_or_oldest_puts_a_work_of_no_date_last_and_pages_after_ordering()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var undated = await RecommendAsync("1", "Без дати", score: 9, year: null);
        var old = await RecommendAsync("2", "Старий", score: 1, year: 1979);
        var recent = await RecommendAsync("3", "Новий", score: 2, year: 2024);
        var middle = await RecommendAsync("4", "Середній", score: 3, year: 2001);
        var sameDay = await RecommendAsync("5", "Того ж дня", score: 4, year: 2001);

        //Act
        var newest = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Newest", _anna.AccessToken);
        var oldest = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Oldest", _anna.AccessToken);
        var oldestSecondPage = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Oldest&page=2&pageSize=2", _anna.AccessToken);

        //Assert
        // Two of one day: the score decides, the higher first, either way.
        Assert.Equal(
            [recent.Id, sameDay.Id, middle.Id, old.Id, undated.Id],
            (await ReadListAsync(newest, cancellationToken)).Items.Select(item => item.Id));
        Assert.Equal(
            [old.Id, sameDay.Id, middle.Id, recent.Id, undated.Id],
            (await ReadListAsync(oldest, cancellationToken)).Items.Select(item => item.Id));
        var page = await ReadListAsync(oldestSecondPage, cancellationToken);
        Assert.Equal([middle.Id, recent.Id], page.Items.Select(item => item.Id));
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task List_sorted_by_title_follows_the_ukrainian_alphabet_then_the_original_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // By code point Ґ comes after Я and І before А; the alphabet puts Ґ after Г and І after И.
        var hudzyk = await RecommendAsync("1", "Ґудзик", score: 9);
        var ihrashky = await RecommendAsync("2", "Іграшки", score: 8);
        var aero = await RecommendAsync("3", "Аеро", score: 1);
        var yalynka = await RecommendAsync("4", "Ялинка", score: 7);
        var hora = await RecommendAsync("5", "Гора", score: 6);
        // One title, two works: the original title decides.
        var duneTwo = await RecommendAsync("6", "Дюна", score: 5, originalTitle: "Dune: Part Two");
        var dune = await RecommendAsync("7", "Дюна", score: 2, originalTitle: "Dune");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=title", _anna.AccessToken);

        //Assert
        Assert.Equal(
            [aero.Id, hora.Id, hudzyk.Id, dune.Id, duneTwo.Id, ihrashky.Id, yalynka.Id],
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_sorted_by_releases_puts_the_found_most_first_then_not_found_then_never_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var checks = new ReleaseAvailabilitySeeder(_factory.Scope);
        var neverChecked = await RecommendAsync("1", "Не перевірений", score: 9);
        var notFound = await RecommendAsync("2", "Не знайдений", score: 8);
        var foundTwo = await RecommendAsync("3", "Два релізи", score: 7);
        var foundFive = await RecommendAsync("4", "П'ять релізів", score: 1);
        await checks.Create(notFound, releaseCount: 0);
        await checks.Create(foundTwo, releaseCount: 2);
        await checks.Create(foundFive, releaseCount: 5);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Releases", _anna.AccessToken);

        //Assert
        Assert.Equal(
            [foundFive.Id, foundTwo.Id, notFound.Id, neverChecked.Id],
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_sorted_breaks_a_tie_by_the_score_then_votes_then_title_then_catalog_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // All of one vote count: the votes order alone can't tell them apart.
        var lowScore = await RecommendAsync("1", "А", score: 1, voteCount: 300);
        var titleB = await RecommendAsync("2", "Б", score: 5, voteCount: 300);
        var titleAId20 = await RecommendAsync("20", "А", score: 5, voteCount: 300);
        var titleAId10 = await RecommendAsync("10", "А", score: 5, voteCount: 300);
        var highScore = await RecommendAsync("3", "Я", score: 9, voteCount: 300);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&sort=Votes", _anna.AccessToken);

        //Assert
        // The score; then the title and the catalog id, both ordinal ("10" before "20").
        Assert.Equal(
            [highScore.Id, titleAId10.Id, titleAId20.Id, titleB.Id, lowScore.Id],
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("sort=Popular", "“Popular” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or Releases.")]
    // By name only: a number would be read as the sort with that value.
    [InlineData("sort=2", "“2” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or Releases.")]
    [InlineData("sort=Rating,%20Votes", "“Rating, Votes” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or Releases.")]
    [InlineData(
        "sort=TheMostPopularFirstAndThenTheRest",
        "“TheMostPopularFirstAndThenTheRes…” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or Releases.")]
    public async Task List_with_a_sort_it_doesnt_take_is_400_with_why(string query, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("1", "Матриця");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies&{query}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A film recommended in the Movies tab, scored <paramref name="score"/>, for one Recommended reason.</summary>
    private async Task<CatalogWorkEntity> RecommendAsync(
        string catalogId,
        string title,
        double score = 1,
        double? rating = 7.5,
        int voteCount = 1000,
        int? year = 2015,
        string? originalTitle = null)
    {
        var work = await _works.Create(
            MediaWorkKind.Movie,
            catalogId,
            title,
            year is { } released ? new DateOnly(released, 3, 1) : null,
            rating: rating,
            voteCount: voteCount,
            originalTitle: originalTitle);
        await _candidates.Create(work, RecommendationTab.Movies, score);
        return work;
    }

    private static async Task<RecommendationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
