using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-5 — what a personal access token may do about tokens and about its user's account: nothing (ADR 0040). The token
// management is refused to a token as a whole, the list included, whatever its access — so a token in the wrong hands can't
// find, revoke or make a sibling that outlives it; and so are the account's own session (GET api/auth/me, which describes a
// session a token hasn't got, and once answered a token with a 500) and its password. Each is a 403 with the sentence a
// script's author reads, an audit row naming the token, and nothing changed.
public sealed class PersonalAccessTokensAccessHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/profile/tokens";
    private const string MeUrl = "/api/auth/me";
    private const string PasswordUrl = "/api/auth/password";
    private const string DownloadsUrl = "/api/downloads";
    private const string NewPassword = "brand-new-password";
    private const string TokenName = "Claude on the laptop";

    // Spelled out rather than read from PersonalAccessTokenErrors: this is what a script's author reads.
    private static readonly string NotForTokensBody = ErrorBody.Json(
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.",
        "account.token.not-for-tokens");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _member = null!;

    public PersonalAccessTokensAccessHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every token-management endpoint, by method, with a token of each access.</summary>
    public static TheoryData<string, PersonalAccessTokenAccess> Endpoints => new()
    {
        { "GET", PersonalAccessTokenAccess.ReadOnly },
        { "GET", PersonalAccessTokenAccess.ReadAndDownload },
        { "POST", PersonalAccessTokenAccess.ReadOnly },
        { "POST", PersonalAccessTokenAccess.ReadAndDownload },
        { "DELETE", PersonalAccessTokenAccess.ReadOnly },
        { "DELETE", PersonalAccessTokenAccess.ReadAndDownload },
    };

    public static TheoryData<PersonalAccessTokenAccess> Accesses => new()
    {
        PersonalAccessTokenAccess.ReadOnly,
        PersonalAccessTokenAccess.ReadAndDownload,
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_with_a_token_is_403_with_the_sentence_and_creates_or_revokes_nothing(string method, PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_member.User, access, name: TokenName);
        var sibling = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload, name: "Sibling");

        //Act
        // A request that would succeed from a session: a valid body, the user's own sibling token to revoke.
        var response = method switch
        {
            "GET" => await _factory.GetHttpRequest(BaseUrl, token.Secret),
            "POST" => await _factory.PostHttpRequest(
                BaseUrl, token.Secret, new CreatePersonalAccessTokenRequest("Outlives me", PersonalAccessTokenAccess.ReadAndDownload, PersonalAccessTokenLifetime.Never)),
            "DELETE" => await _factory.DeleteHttpRequest($"{BaseUrl}/{sibling.Token.Id}", token.Secret),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
        var siblingStillWorks = await _factory.GetHttpRequest(DownloadsUrl, sibling.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotForTokensBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, siblingStillWorks.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            // The two tokens there were, neither revoked: no sibling made, none revoked.
            Assert.Equal(
                new[] { token.Token.Id, sibling.Token.Id }.Order(),
                (await db.PersonalAccessTokens.AsNoTracking().Select(row => row.Id).ToListAsync(cancellationToken)).Order());
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(row => row.RevokedUtc != null, cancellationToken));
            // The refusal, recorded against the token that knocked, as its user.
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRefused, (Guid?)_member.User.Id, "member", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)token.Token.Id, (string?)TokenName, now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains($"{method} {BaseUrl}", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Accesses))]
    public async Task Me_with_a_token_is_403_with_the_sentence_not_a_500(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, access);

        //Act
        var response = await _factory.GetHttpRequest(MeUrl, token.Secret);

        //Assert
        // The answer describes the session the request came on, and a token has none: refused rather than made up.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotForTokensBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action));
    }

    [Theory]
    [MemberData(nameof(Accesses))]
    public async Task ChangePassword_with_a_token_is_403_runs_no_hash_and_changes_nothing(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, access);

        //Act
        // The right current password: only who sends it is wrong.
        var response = await _factory.PostHttpRequest(PasswordUrl, token.Secret, new ChangePasswordRequest(UserSeeder.DefaultPassword, NewPassword));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotForTokensBody, await response.Content.ReadAsStringAsync(cancellationToken));
        // Refused before the password was looked at: a token can't even probe it.
        Assert.Empty(_factory.PasswordHasher.VerifiedHashes);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(_member.User.PasswordHash, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == _member.User.Id, cancellationToken)).PasswordHash);
            Assert.Equal(0, await db.UserSessions.CountAsync(s => s.RevokedUtc != null, cancellationToken));
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
        });
    }
}
