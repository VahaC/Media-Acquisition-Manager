using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — "Not interested" (PUT /api/recommendations/dismissals/{kind}/{catalogId}) and its undo (DELETE), Vasyl's
// decision 3: kept per user — another user's list, and another user's mark, never change — idempotent both ways (204
// again, one row; a race on the unique index answers 204 too), a kind other than Movie or Series or an id that isn't
// the catalog's a 400 with its sentence, and no audit row: a preference, like the Profile's settings.
public sealed class RecommendationDismissalsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations/dismissals";
    private const string ListUrl = "/api/recommendations";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _anna = null!;
    private AuthorizedUser _bohdan = null!;
    private CatalogWorkEntity _matrix = null!;

    public RecommendationDismissalsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _anna = await users.CreateAuthorized("anna");
        _bohdan = await users.CreateAuthorized("bohdan");
        _matrix = await new CatalogWorkSeeder(_factory.Scope).Create(MediaWorkKind.Movie, "603", "Матриця", new DateOnly(1999, 3, 31));
        await new RecommendationCandidateSeeder(_factory.Scope).Create(_matrix, RecommendationTab.Movies);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Dismiss_is_204_and_hides_the_work_from_the_callers_list_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);
        var annas = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", _anna.AccessToken);
        var bohdans = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", _bohdan.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty((await ReadListAsync(annas, cancellationToken)).Items);
        Assert.Equal([_matrix.Id], (await ReadListAsync(bohdans, cancellationToken)).Items.Select(item => item.Id));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.RecommendationDismissals.SingleAsync(cancellationToken);
            Assert.Equal((_anna.User.Id, MediaWorkKind.Movie, "603", now), (row.UserId, row.Kind, row.CatalogId, row.CreatedUtc));
            // A preference, not an action on the household's things.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Dismiss_reads_the_kind_in_any_letter_case_and_keeps_the_mark_by_kind()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A show of the film's number: the catalog numbers the two apart, and so does the mark.
        var show = await new CatalogWorkSeeder(_factory.Scope).Create(MediaWorkKind.Series, "603", "Серіал того ж номера", new DateOnly(2020, 1, 1));
        await new RecommendationCandidateSeeder(_factory.Scope).Create(show, RecommendationTab.Series);

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/series/603", _anna.AccessToken);
        var movies = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", _anna.AccessToken);
        var series = await _factory.GetHttpRequest($"{ListUrl}?tab=Series", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([_matrix.Id], (await ReadListAsync(movies, cancellationToken)).Items.Select(item => item.Id));
        Assert.Empty((await ReadListAsync(series, cancellationToken)).Items);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(MediaWorkKind.Series, (await db.RecommendationDismissals.SingleAsync(cancellationToken)).Kind));
    }

    [Fact]
    public async Task Dismiss_twice_is_204_both_times_and_keeps_one_mark()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);
        var second = await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, second.StatusCode));
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.RecommendationDismissals.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Dismiss_of_a_work_no_list_holds_is_204_and_kept_for_when_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // By its kind and id, not its catalog row: a mark outlives the row, and a work recommended later stays hidden.
        var response = await _factory.PutHttpRequest($"{BaseUrl}/Movie/157336", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal("157336", (await db.RecommendationDismissals.SingleAsync(cancellationToken)).CatalogId));
    }

    [Fact]
    public async Task Dismiss_when_another_request_marks_it_first_is_204_with_one_mark()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Deterministic race: both found no mark, and a second click's request lands its mark just before this one's —
        // through a context of its own, as a parallel request would.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<RecommendationDismissalEntity>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                await new RecommendationDismissalSeeder(scope.ServiceProvider).Dismiss(_anna.User, _matrix);
            });

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);

        //Assert
        // The unique index lets one commit, and the loser was asked for what is now so: a 204, not a 500.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.RecommendationDismissals.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Undismiss_is_204_and_brings_the_work_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationDismissalSeeder(_factory.Scope).Dismiss(_anna.User, _matrix);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);
        var list = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([_matrix.Id], (await ReadListAsync(list, cancellationToken)).Items.Select(item => item.Id));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.RecommendationDismissals.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Undismiss_without_a_mark_is_204_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.RecommendationDismissals.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Undismiss_leaves_another_users_mark_on_the_same_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dismissals = new RecommendationDismissalSeeder(_factory.Scope);
        await dismissals.Dismiss(_anna.User, _matrix);
        await dismissals.Dismiss(_bohdan.User, _matrix);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);
        var bohdans = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", _bohdan.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await ReadListAsync(bohdans, cancellationToken)).Items);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(_bohdan.User.Id, (await db.RecommendationDismissals.SingleAsync(cancellationToken)).UserId));
    }

    [Theory]
    [InlineData("PUT", "Episode/603", "“Episode” is not a kind of work. Use Movie or Series.")]
    // By name only: a number would be read as the kind with that value.
    [InlineData("PUT", "1/603", "“1” is not a kind of work. Use Movie or Series.")]
    [InlineData("PUT", "Tv/603", "“Tv” is not a kind of work. Use Movie or Series.")]
    [InlineData("PUT", "Movie/tt0133093", "“tt0133093” is not a catalog id, which is a number such as 603.")]
    [InlineData("PUT", "Movie/-603", "“-603” is not a catalog id, which is a number such as 603.")]
    [InlineData("PUT", "Movie/1234567890123", "“1234567890123” is not a catalog id, which is a number such as 603.")]
    [InlineData("DELETE", "Cartoon/603", "“Cartoon” is not a kind of work. Use Movie or Series.")]
    [InlineData("DELETE", "Series/abc", "“abc” is not a catalog id, which is a number such as 603.")]
    public async Task Dismissal_with_a_kind_or_id_it_doesnt_take_is_400_with_why_and_changes_nothing(string method, string path, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationDismissalSeeder(_factory.Scope).Dismiss(_anna.User, _matrix);

        //Act
        var response = method == "PUT"
            ? await _factory.PutHttpRequest($"{BaseUrl}/{path}", _anna.AccessToken)
            : await _factory.DeleteHttpRequest($"{BaseUrl}/{path}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(("603", 1), ((await db.RecommendationDismissals.SingleAsync(cancellationToken)).CatalogId, await db.RecommendationDismissals.CountAsync(cancellationToken))));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Dismissal_without_a_token_is_401_and_changes_nothing(string method)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new RecommendationDismissalSeeder(_factory.Scope).Dismiss(_anna.User, _matrix);

        //Act
        var response = method == "PUT"
            ? await _factory.PutHttpRequest($"{BaseUrl}/Movie/157336")
            : await _factory.DeleteHttpRequest($"{BaseUrl}/Movie/603");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal("603", (await db.RecommendationDismissals.SingleAsync(cancellationToken)).CatalogId));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<RecommendationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
