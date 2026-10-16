using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-5 — "Not interested" from a script (ADR 0040): a token with Read and download access marks a work for its user — the
// mark its user's, hiding the work from their list only — while its undo stays the web app's: 403 for any token, the mark
// kept. A Read-only token reads the list and is refused the mark with its own sentence. Every refusal is recorded against
// the token.
public sealed class RecommendationDismissalsPersonalAccessTokenHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations/dismissals";
    private const string ListUrl = "/api/recommendations";

    // Spelled out rather than read from PersonalAccessTokenErrors: these are what a script's author reads.
    private const string ReadOnlyError =
        "This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README.";

    private const string NotForTokensError =
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _anna = null!;
    private AuthorizedUser _bohdan = null!;
    private CatalogWorkEntity _matrix = null!;

    public RecommendationDismissalsPersonalAccessTokenHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _anna = await users.CreateAuthorized("anna");
        _bohdan = await users.CreateAuthorized("bohdan");
        _matrix = await new CatalogWorkSeeder(_factory.Scope).Create(MediaWorkKind.Movie, "603", "Матриця", new DateOnly(1999, 3, 31));
        await new RecommendationCandidateSeeder(_factory.Scope).Create(_matrix, RecommendationTab.Movies);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Dismiss_with_a_read_and_download_token_is_204_and_marks_the_work_for_its_user_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", token.Secret);
        var annas = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", token.Secret);
        var bohdans = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", _bohdan.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await ReadListAsync(annas, cancellationToken)).Items);
        Assert.Equal([_matrix.Id], (await ReadListAsync(bohdans, cancellationToken)).Items.Select(item => item.Id));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.RecommendationDismissals.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((_anna.User.Id, MediaWorkKind.Movie, "603", now), (row.UserId, row.Kind, row.CatalogId, row.CreatedUtc));
            // A preference, as from the web app: no audit row, and nothing refused.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData(PersonalAccessTokenAccess.ReadOnly)]
    [InlineData(PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Undismiss_with_a_token_is_403_and_keeps_the_mark(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", _anna.AccessToken);
        var token = await _tokens.Create(_anna.User, access, name: "Claude");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/Movie/603", token.Secret);

        //Assert
        // ADR 0040 — undoing Not interested is the web app's: a script marks, it doesn't unmark.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotForTokensError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(_anna.User.Id, (await db.RecommendationDismissals.AsNoTracking().SingleAsync(cancellationToken)).UserId);
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRefused, (Guid?)_anna.User.Id, (Guid?)token.Token.Id, (string?)"Claude", now),
                (audit.Action, audit.ActorUserId, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
    }

    [Fact]
    public async Task Dismiss_with_a_read_only_token_is_403_with_its_sentence_and_marks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadOnly);

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/Movie/603", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ReadOnlyError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.RecommendationDismissals.CountAsync(cancellationToken));
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task List_with_a_read_only_token_is_200_with_its_users_list()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_anna.User, PersonalAccessTokenAccess.ReadOnly);

        //Act
        var response = await _factory.GetHttpRequest($"{ListUrl}?tab=Movies", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([_matrix.Id], (await ReadListAsync(response, cancellationToken)).Items.Select(item => item.Id));
    }

    private static async Task<RecommendationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
