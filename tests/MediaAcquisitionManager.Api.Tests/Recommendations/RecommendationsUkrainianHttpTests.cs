using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-47 WP14 (ADR 0042, design §8.6) — the Recommendations page and its tools in Ukrainian: a run's stored error is
// rendered in the reader's language when runs/latest is read — one an older build stored in English converted at start
// —, a refused filter quotes its value, list_recommendations counts a tab's recommendations in their own form (1
// рекомендація, 3 рекомендації), and Not interested is the web app's own label in «…». The same request without the
// header answers English. Each answer spelled here, never read from the locale files.
public sealed class RecommendationsUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ListUrl = "/api/recommendations";
    private const string LatestUrl = "/api/recommendations/runs/latest";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;

    public RecommendationsUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        // Three films on their tab and a show on its own.
        var works = new CatalogWorkSeeder(_factory.Scope);
        var candidates = new RecommendationCandidateSeeder(_factory.Scope);
        await candidates.Create(await works.Create(MediaWorkKind.Movie, "603", "Матриця", new DateOnly(1999, 3, 31), originalTitle: "The Matrix"), RecommendationTab.Movies, score: 3);
        await candidates.Create(await works.Create(MediaWorkKind.Movie, "157336", "Інтерстеллар", new DateOnly(2014, 11, 5), originalTitle: "Interstellar"), RecommendationTab.Movies, score: 2);
        await candidates.Create(await works.Create(MediaWorkKind.Movie, "329865", "Прибуття", new DateOnly(2016, 11, 10), originalTitle: "Arrival"), RecommendationTab.Movies, score: 1);
        await candidates.Create(await works.Create(MediaWorkKind.Series, "1396", "Пуститися берега", new DateOnly(2008, 1, 20), originalTitle: "Breaking Bad"), RecommendationTab.Series);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Latest_run_asked_in_ukrainian_reads_its_error_in_ukrainian_and_in_english_without_the_header()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationRunSeeder(_factory.Scope).Create(RecommendationRunStatus.Failed, error: Message.Of("recommendations.run.app-error"));

        //Act
        var ukrainian = await LatestAsync("uk", cancellationToken);
        var english = await LatestAsync(null, cancellationToken);

        //Assert
        Assert.Equal("Обчислення рекомендацій зупинилося через помилку в застосунку. Подробиці — в журналі застосунку.", ukrainian.Error);
        Assert.Equal("The recommendations stopped computing because of an error in the app. Its log says more.", english.Error);
    }

    [Fact]
    public async Task Latest_run_asked_in_ukrainian_reads_an_error_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = await new RecommendationRunSeeder(_factory.Scope).Create(RecommendationRunStatus.Failed, error: Message.Of("recommendations.run.app-error"));
        await _factory.ConvertStoredEnglishAsync(
            "RecommendationRuns", "Error", run.Id, "The app restarted before these recommendations were computed. The previous ones are kept.");

        //Act
        var latest = await LatestAsync("uk", cancellationToken);

        //Assert
        Assert.Equal("Застосунок перезапустився, перш ніж ці рекомендації було обчислено. Попередні рекомендації залишено.", latest.Error);
    }

    [Fact]
    public async Task List_of_a_tab_it_lacks_asked_in_ukrainian_is_400_quoting_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{ListUrl}?tab=Anime", _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «Anime» не є вкладкою рекомендацій. Вкажіть Movies, Series, Cartoons або Documentaries.","code":"recommendations.list.tab-unknown"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("Movies", "На вкладці Movies усього 3 рекомендації; на сторінці 1 — 3.")]
    [InlineData("Series", "На вкладці Series усього 1 рекомендація; на сторінці 1 — 1.")]
    public async Task ListRecommendations_on_a_connection_asking_for_ukrainian_counts_the_tab_in_its_own_form(string tab, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("list_recommendations", new Dictionary<string, object?> { ["tab"] = tab }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(expected, result.ReadRawText());
    }

    [Fact]
    public async Task DismissRecommendation_on_a_connection_asking_for_ukrainian_names_the_web_apps_not_interested_label()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync(
            "dismiss_recommendation", new Dictionary<string, object?> { ["kind"] = "Movie", ["catalogId"] = "603" }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(
            "Фільм з ID 603 позначено «Не цікавить» і прибрано з ваших рекомендацій; у вебзастосунку це рішення можна скасувати.",
            result.ReadRawText());
    }

    private async Task<RecommendationRunDto> LatestAsync(string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(LatestUrl, _owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return (await response.Content.ReadFromJsonAsync<RecommendationRunDto>(options, cancellationToken))!;
    }
}
