using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-33 — the signed-in user's own recommendation settings (GET|PUT /api/profile/recommendations), Vasyl's decision 5:
// the defaults (every tab on, 60 searches) until they save; the searches run for them in the last 24 hours; a save of
// all five fields, each required with its sentence, the limit 0 to 500; a body that isn't the settings answering
// { error } too, never a validation body; their own row only, and no audit row. Queuing a run when a tab is switched on
// is RecommendationSettingsTriggerHttpTests'.
public sealed class RecommendationSettingsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/profile/recommendations";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string LimitError = "The daily search limit is a whole number from 0 to 500.";
    private const string UnreadableBodyError =
        "Send the settings as JSON: movies, series, cartoons and documentaries true or false, and dailySearchLimit a whole number from 0 to 500.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _anna = null!;
    private AuthorizedUser _bohdan = null!;

    public RecommendationSettingsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _anna = await users.CreateAuthorized("anna");
        _bohdan = await users.CreateAuthorized("bohdan");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Get_before_any_save_is_every_tab_on_60_searches_and_none_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"movies":true,"series":true,"cartoons":true,"documentaries":true,"dailySearchLimit":60,"searchesLast24Hours":0}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
        // Read, never written: a user who never saved has no row.
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.UserRecommendationSettings.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Get_gives_the_saved_settings_and_the_callers_searches_of_the_last_24_hours()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_anna.User, [RecommendationTab.Movies, RecommendationTab.Documentaries], dailySearchLimit: 12);
        var searches = new RecommendationSearchSeeder(_factory.Scope);
        await searches.Log(_anna.User, RecommendationTab.Movies, 3, TimeSpan.FromHours(1));
        await searches.Log(_anna.User, RecommendationTab.Documentaries, 1, TimeSpan.FromHours(23));
        // Out of the window, and someone else's.
        await searches.Log(_anna.User, RecommendationTab.Movies, 2, TimeSpan.FromHours(25));
        await searches.Log(_bohdan.User, RecommendationTab.Movies, 5, TimeSpan.FromHours(1));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationSettingsDto(true, false, false, true, 12, 4), await ReadSettingsAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Update_saves_the_callers_settings_and_answers_them_without_an_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationSearchSeeder(_factory.Scope).Log(_anna.User, RecommendationTab.Movies, 2);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new RecommendationSettingsRequest(true, false, true, false, 0));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new RecommendationSettingsDto(true, false, true, false, 0, 2), await ReadSettingsAsync(response, cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.UserRecommendationSettings.SingleAsync(cancellationToken);
            Assert.Equal(
                (_anna.User.Id, true, false, true, false, 0, now),
                (row.UserId, row.MoviesEnabled, row.SeriesEnabled, row.CartoonsEnabled, row.DocumentariesEnabled, row.DailySearchLimit, row.CreatedUtc));
            // A per-user preference: the README's Audit trail lists what writes one, and this doesn't.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_again_changes_the_same_row_and_leaves_another_users_settings()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstSave = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new RecommendationSettingsRequest(true, true, true, true, 60));
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new RecommendationSettingsRequest(false, true, false, true, 500));
        var bohdans = await _factory.GetHttpRequest(BaseUrl, _bohdan.AccessToken);

        //Assert
        Assert.Equal(new RecommendationSettingsDto(false, true, false, true, 500, 0), await ReadSettingsAsync(response, cancellationToken));
        Assert.Equal(new RecommendationSettingsDto(true, true, true, true, 60, 0), await ReadSettingsAsync(bohdans, cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.UserRecommendationSettings.SingleAsync(cancellationToken);
            Assert.Equal((false, true, false, true, 500), (row.MoviesEnabled, row.SeriesEnabled, row.CartoonsEnabled, row.DocumentariesEnabled, row.DailySearchLimit));
            // Still the first save's row.
            Assert.Equal(firstSave, row.CreatedUtc);
        });
    }

    [Theory]
    [InlineData("""{"series":true,"cartoons":true,"documentaries":true,"dailySearchLimit":60}""", "Say whether the Movies tab is on.")]
    [InlineData("""{"movies":true,"cartoons":true,"documentaries":true,"dailySearchLimit":60}""", "Say whether the Series tab is on.")]
    [InlineData("""{"movies":true,"series":true,"documentaries":true,"dailySearchLimit":60}""", "Say whether the Cartoons tab is on.")]
    [InlineData("""{"movies":true,"series":true,"cartoons":true,"dailySearchLimit":60}""", "Say whether the Documentaries tab is on.")]
    [InlineData("""{"movies":true,"series":true,"cartoons":true,"documentaries":true}""", LimitError)]
    [InlineData("""{"movies":null,"series":true,"cartoons":true,"documentaries":true,"dailySearchLimit":60}""", "Say whether the Movies tab is on.")]
    [InlineData("""{}""", "Say whether the Movies tab is on.")]
    [InlineData("null", "Say whether the Movies tab is on.")]
    [InlineData("", "Say whether the Movies tab is on.")]
    public async Task Update_with_a_field_missing_is_400_with_its_sentence_and_saves_nothing(string json, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await PutJsonTextAsync(json);

        //Assert
        // A missing tab is never read as off: that would hide a list nobody asked to hide.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(cancellationToken);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(501)]
    [InlineData(int.MaxValue)]
    public async Task Update_with_a_limit_out_of_range_is_400_with_the_sentence_and_saves_nothing(int limit)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new RecommendationSettingsRequest(true, true, true, true, limit));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LimitError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(cancellationToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task Update_takes_the_limits_bounds(int limit)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new RecommendationSettingsRequest(true, true, true, true, limit));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(limit, (await ReadSettingsAsync(response, cancellationToken)).DailySearchLimit);
        await _factory.WithDbAsync(async db => Assert.Equal(limit, (await db.UserRecommendationSettings.SingleAsync(cancellationToken)).DailySearchLimit));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("true")]
    [InlineData("""{"movies":"yes","series":true,"cartoons":true,"documentaries":true,"dailySearchLimit":60}""")]
    [InlineData("""{"movies":true,"series":true,"cartoons":true,"documentaries":true,"dailySearchLimit":1.5}""")]
    [InlineData("""{"movies":true,"series":true,"cartoons":true,"documentaries":true,"dailySearchLimit":99999999999}""")]
    public async Task Update_with_a_body_that_isnt_the_settings_is_400_with_the_sentence_never_a_validation_body(string json)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await PutJsonTextAsync(json);

        //Assert
        // The { error } shape the Profile page shows: never [ApiController]'s problem details.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(UnreadableBodyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(cancellationToken);
    }

    [Fact]
    public async Task Get_and_update_without_a_token_are_401_and_save_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var get = await _factory.GetHttpRequest(BaseUrl);
        var put = await _factory.PutHttpRequest(BaseUrl, body: new RecommendationSettingsRequest(true, true, true, true, 60));

        //Assert
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (get.StatusCode, put.StatusCode));
        Assert.Equal(SignInRequiredBody, await get.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(SignInRequiredBody, await put.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingSavedAsync(cancellationToken);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Anna's save with <paramref name="json"/> as the body as it is — JSON the helpers can't write, such as a
    /// field left out or a malformed body; no body at all when it is empty.</summary>
    private Task<HttpResponseMessage> PutJsonTextAsync(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _anna.AccessToken);
        return _factory.SendHttpRequest(request);
    }

    private async Task AssertNothingSavedAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.UserRecommendationSettings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
        });

    private static async Task<RecommendationSettingsDto> ReadSettingsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationSettingsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
