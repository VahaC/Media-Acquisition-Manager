using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// The owner setting a new password for a user — their own included, without giving the current one
// (changing it on Profile asks for it: AuthPasswordHttpTests). Every session opened with the old
// password must end, except the one the owner is resetting from. MAM-5 — and every personal access
// token of the user, the owner's own too, one "revoked" audit row each naming the owner (ADR 0016
// amended, ADR 0040).
public sealed class UsersPasswordHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/users";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string RefreshUrl = "/api/auth/refresh";
    private const string MeUrl = "/api/auth/me";
    private const string DownloadsUrl = "/api/downloads";
    private const string NewPassword = "brand-new-password";
    private const string InvalidCredentialsBody = """{"error":"The user name or password is incorrect."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;

    public UsersPasswordHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ResetPassword_is_204_and_only_the_new_password_signs_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));
        var withTheOldPassword = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("member", UserSeeder.DefaultPassword));
        var withTheNewPassword = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("member", NewPassword));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldPassword.StatusCode);
        Assert.Equal(InvalidCredentialsBody, await withTheOldPassword.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, withTheNewPassword.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_stores_a_hash_of_the_new_password_not_the_password()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var hasher = _factory.Resolve<IPasswordHasher>();
        var member = await _users.Create("member");

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Users.SingleAsync(u => u.Id == member.Id, cancellationToken);
            Assert.NotEqual(member.PasswordHash, row.PasswordHash);
            Assert.DoesNotContain(NewPassword, row.PasswordHash, StringComparison.Ordinal);
            Assert.True(hasher.Verify(NewPassword, row.PasswordHash));
            Assert.False(hasher.Verify(UserSeeder.DefaultPassword, row.PasswordHash));
        });
    }

    [Fact]
    public async Task ResetPassword_ends_every_session_of_the_user_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var laptop = await _users.CreateAuthorized("member");
        var phone = await _users.CreateSession(laptop.User);

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{laptop.User.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));
        var me = await _factory.GetHttpRequest(MeUrl, phone.AccessToken);
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: laptop.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.UserSessions.SingleAsync(s => s.Id == laptop.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(now, (await db.UserSessions.SingleAsync(s => s.Id == phone.SessionId, cancellationToken)).RevokedUtc);
            // The owner's own session is not the member's; resetting someone else leaves it alone.
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == _owner.SessionId, cancellationToken)).RevokedUtc);
        });
    }

    [Fact]
    public async Task ResetPassword_of_the_owners_own_password_keeps_the_calling_session_and_ends_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var otherDevice = await _users.CreateSession(_owner.User);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{_owner.User.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));
        var here = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var there = await _factory.GetHttpRequest(BaseUrl, otherDevice.AccessToken);
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("owner", NewPassword));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, there.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == _owner.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(now, (await db.UserSessions.SingleAsync(s => s.Id == otherDevice.SessionId, cancellationToken)).RevokedUtc);
        });
    }

    [Fact]
    public async Task ResetPassword_records_a_user_password_reset_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var member = await _users.Create("member");

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.UserPasswordReset, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            Assert.Equal(member.Id, audit.TargetId);
            Assert.Equal("member", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    // MAM-5 — a reset is the tool for a leaked password, and whoever had it may have made a token with it.
    [Fact]
    public async Task ResetPassword_revokes_every_personal_access_token_of_the_user_with_a_revoked_audit_row_each()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var member = await _users.Create("member");
        var laptop = await _tokens.Create(member, PersonalAccessTokenAccess.ReadAndDownload, name: "Laptop");
        var nas = await _tokens.Create(member, name: "NAS script");
        var ownersOwn = await _tokens.Create(_owner.User, name: "Owner's script");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));
        var laptopAfterwards = await _factory.GetHttpRequest(DownloadsUrl, laptop.Secret);
        var nasAfterwards = await _factory.GetHttpRequest(DownloadsUrl, nas.Secret);
        var ownersAfterwards = await _factory.GetHttpRequest(DownloadsUrl, ownersOwn.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, laptopAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nasAfterwards.StatusCode);
        // Resetting someone else's password leaves the owner's tokens alone.
        Assert.Equal(HttpStatusCode.OK, ownersAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var revokedUtc = await db.PersonalAccessTokens.AsNoTracking().ToDictionaryAsync(token => token.Id, token => token.RevokedUtc, cancellationToken);
            Assert.Equal(now, revokedUtc[laptop.Token.Id]);
            Assert.Equal(now, revokedUtc[nas.Token.Id]);
            Assert.Null(revokedUtc[ownersOwn.Token.Id]);
            // Ordinal by name, as SQLite sorts text: capitals first.
            var rows = await db.AuditEvents.AsNoTracking().OrderBy(audit => audit.TargetName).ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    (AuditAction.PersonalAccessTokenRevoked, (Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)laptop.Token.Id, (string?)"Laptop", now),
                    (AuditAction.PersonalAccessTokenRevoked, (Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)nas.Token.Id, (string?)"NAS script", now),
                    (AuditAction.UserPasswordReset, (Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.User, (Guid?)member.Id, (string?)"member", now),
                ],
                rows.Select(audit => (audit.Action, audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc)));
        });
    }

    // MAM-5 — the owner's reset of his own password too: a token is no device he is working on (ADR 0016 amended).
    [Fact]
    public async Task ResetPassword_of_the_owners_own_password_revokes_the_owners_tokens_and_keeps_the_calling_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var ownersOwn = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, name: "Owner's Claude");
        var member = await _users.Create("member");
        var membersOwn = await _tokens.Create(member, name: "Member's script");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{_owner.User.Id}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));
        var tokenAfterwards = await _factory.GetHttpRequest(DownloadsUrl, ownersOwn.Secret);
        var here = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var membersAfterwards = await _factory.GetHttpRequest(DownloadsUrl, membersOwn.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        Assert.Equal(HttpStatusCode.OK, membersAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(token => token.Id == ownersOwn.Token.Id, cancellationToken)).RevokedUtc);
            Assert.Null((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(token => token.Id == membersOwn.Token.Id, cancellationToken)).RevokedUtc);
            var revoked = await db.AuditEvents.AsNoTracking().SingleAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRevoked, cancellationToken);
            Assert.Equal(((Guid?)_owner.User.Id, (Guid?)ownersOwn.Token.Id), (revoked.ActorUserId, revoked.TargetId));
        });
    }

    [Theory]
    [InlineData(7, "A password must be at least 8 characters long.")]
    [InlineData(129, "A password can be at most 128 characters long.")]
    public async Task ResetPassword_with_a_password_of_the_wrong_length_is_400_and_changes_nothing(int length, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.CreateAuthorized("member");

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/{member.User.Id}/password", _owner.AccessToken, new ResetPasswordRequest(new string('p', length)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(member.User.PasswordHash, (await db.Users.SingleAsync(u => u.Id == member.User.Id, cancellationToken)).PasswordHash);
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == member.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // MAM-4 — a blank or missing password reaches the rule and gets its sentence, which the Reset
    // password dialog shows as is; a framework validation body has no "error" for it to show.
    [Theory]
    [InlineData("empty password")]
    [InlineData("whitespace-only password")]
    [InlineData("missing password")]
    public async Task ResetPassword_with_a_blank_or_missing_password_is_400_with_the_rule_and_changes_nothing(string blank)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.CreateAuthorized("member");
        object body = blank switch
        {
            "empty password" => new ResetPasswordRequest(""),
            "whitespace-only password" => new ResetPasswordRequest("   "),
            "missing password" => new { },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.User.Id}/password", _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A password must be at least 8 characters long.", await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(member.User.PasswordHash, (await db.Users.SingleAsync(u => u.Id == member.User.Id, cancellationToken)).PasswordHash);
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == member.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task ResetPassword_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/password", _owner.AccessToken, new ResetPasswordRequest(NewPassword));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }
}
