using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Auth;

// MAM-16 — any signed-in user changing their own password, giving the current one. Every other
// session of theirs ends at once and the one they changed it from stays (ADR 0016). A refusal is a
// 400 with a sentence, never a 401, and changes nothing. MAM-5 — their personal access tokens keep
// working: changing one's own password is no lockout (ADR 0016 amended).
public sealed class AuthPasswordHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/auth/password";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string RefreshUrl = "/api/auth/refresh";
    private const string MeUrl = "/api/auth/me";
    private const string UsersUrl = "/api/settings/users";
    private const string DownloadsUrl = "/api/downloads";
    private const string NewPassword = "brand-new-password";
    private const string CurrentPasswordIncorrect = "The current password is incorrect.";
    private const string CurrentPasswordRequired = "Enter your current password.";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string InvalidCredentialsBody = """{"error":"The user name or password is incorrect."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;

    // The member, signed in on a laptop (the device every request below comes from) and on a phone.
    private AuthorizedUser _laptop = null!;
    private AuthorizedUser _phone = null!;

    public AuthPasswordHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _laptop = await _users.CreateAuthorized("member");
        _phone = await _users.CreateSession(_laptop.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ChangePassword_as_a_member_is_204_and_only_the_new_password_signs_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);
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
    public async Task ChangePassword_stores_a_hash_of_the_new_password_not_the_password()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var hasher = _factory.Resolve<IPasswordHasher>();

        //Act
        await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Users.SingleAsync(u => u.Id == _laptop.User.Id, cancellationToken);
            Assert.NotEqual(_laptop.User.PasswordHash, row.PasswordHash);
            Assert.DoesNotContain(NewPassword, row.PasswordHash, StringComparison.Ordinal);
            Assert.True(hasher.Verify(NewPassword, row.PasswordHash));
            Assert.False(hasher.Verify(UserSeeder.DefaultPassword, row.PasswordHash));
        });
    }

    // ADR 0016 — the decision itself: whoever else knew the old password is out on their next
    // request, and the device the change was made from carries on.
    [Fact]
    public async Task ChangePassword_keeps_the_calling_session_and_ends_the_users_other_sessions_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);
        var here = await _factory.GetHttpRequest(MeUrl, _laptop.AccessToken);
        var there = await _factory.GetHttpRequest(MeUrl, _phone.AccessToken);
        var refreshThere = await _factory.PostHttpRequest(RefreshUrl, cookie: _phone.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, there.StatusCode);
        Assert.Equal(SignInRequiredBody, await there.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshThere.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == _laptop.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(now, (await db.UserSessions.SingleAsync(s => s.Id == _phone.SessionId, cancellationToken)).RevokedUtc);
        });
    }

    [Fact]
    public async Task ChangePassword_leaves_the_calling_sessions_refresh_cookie_working()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);

        //Act
        // The web app refreshes every 15 minutes; staying signed in means this keeps working.
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: _laptop.RefreshCookie);
        var body = await refresh.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var me = await _factory.GetHttpRequest(MeUrl, body!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_leaves_other_users_sessions_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await _users.CreateAuthorizedOwner("owner");
        var bob = await _users.CreateAuthorized("bob");

        //Act
        await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);
        var ownersMe = await _factory.GetHttpRequest(MeUrl, owner.AccessToken);
        var bobsMe = await _factory.GetHttpRequest(MeUrl, bob.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, ownersMe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobsMe.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.UserSessions.CountAsync(s => s.UserId != _laptop.User.Id && s.RevokedUtc != null, cancellationToken)));
    }

    // MAM-5 — unlike the owner's reset, which is how a leaked password is shut out (UsersPasswordHttpTests): the user
    // just proved who they are, and their scripts carry on.
    [Fact]
    public async Task ChangePassword_keeps_the_users_personal_access_tokens_working()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        var script = await tokens.Create(_laptop.User, PersonalAccessTokenAccess.ReadAndDownload);
        var report = await tokens.Create(_laptop.User);

        //Act
        var response = await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);
        var scriptAfterwards = await _factory.GetHttpRequest(DownloadsUrl, script.Secret);
        var reportAfterwards = await _factory.GetHttpRequest(DownloadsUrl, report.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scriptAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reportAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(token => token.RevokedUtc != null, cancellationToken));
            // The change's own row, and no "revoked" row beside it.
            Assert.Equal(AuditAction.UserPasswordChanged, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task ChangePassword_records_a_user_password_changed_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.UserPasswordChanged, audit.Action);
            Assert.Equal(_laptop.User.Id, audit.ActorUserId);
            Assert.Equal("member", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            Assert.Equal(_laptop.User.Id, audit.TargetId);
            Assert.Equal("member", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    // A 400, never a 401: the web app reads a 401 as a session that ended, and would refresh and send
    // the change again.
    [Fact]
    public async Task ChangePassword_with_a_wrong_current_password_is_400_with_the_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await ChangePasswordAsync(_laptop, "not-the-password", NewPassword);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CurrentPasswordIncorrect, await response.ReadErrorAsync(cancellationToken));
        // Guards the "runs no hash" tests below: a current password that could be right is checked,
        // once, against the member's own hash.
        Assert.Equal(_laptop.User.PasswordHash, Assert.Single(_factory.PasswordHasher.VerifiedHashes));
        await AssertNothingChangedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("empty current password")]
    [InlineData("missing current password")]
    public async Task ChangePassword_with_a_blank_or_missing_current_password_is_400_with_the_sentence_and_runs_no_hash(string blank)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        object body = blank switch
        {
            "empty current password" => new ChangePasswordRequest("", NewPassword),
            "missing current password" => new { newPassword = NewPassword },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _laptop.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CurrentPasswordRequired, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.PasswordHasher.VerifiedHashes);
        await AssertNothingChangedAsync(cancellationToken);
    }

    // MAM-16 — no rule lets anyone set a password over 128 characters, so one can't be the current
    // password, and the server never runs PBKDF2 over whatever length someone posts.
    [Fact]
    public async Task ChangePassword_with_a_current_password_longer_than_128_is_400_incorrect_and_runs_no_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await ChangePasswordAsync(_laptop, new string('p', 129), NewPassword);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CurrentPasswordIncorrect, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.PasswordHasher.VerifiedHashes);
        await AssertNothingChangedAsync(cancellationToken);
    }

    [Theory]
    [InlineData(7, "A password must be at least 8 characters long.")]
    [InlineData(129, "A password can be at most 128 characters long.")]
    public async Task ChangePassword_with_a_new_password_of_the_wrong_length_is_400_and_changes_nothing(int length, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, new string('p', length));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        // The rules are checked before the current password, so a broken one costs no hash.
        Assert.Empty(_factory.PasswordHasher.VerifiedHashes);
        await AssertNothingChangedAsync(cancellationToken);
    }

    // A blank or missing new password reaches the rule and gets its sentence, which the Profile page
    // shows as is; a framework validation body has no "error" for it to show.
    [Theory]
    [InlineData("empty new password")]
    [InlineData("whitespace-only new password")]
    [InlineData("missing new password")]
    public async Task ChangePassword_with_a_blank_or_missing_new_password_is_400_with_the_rule_and_changes_nothing(string blank)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        object body = blank switch
        {
            "empty new password" => new ChangePasswordRequest(UserSeeder.DefaultPassword, ""),
            "whitespace-only new password" => new ChangePasswordRequest(UserSeeder.DefaultPassword, "   "),
            "missing new password" => new { currentPassword = UserSeeder.DefaultPassword },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _laptop.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A password must be at least 8 characters long.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingChangedAsync(cancellationToken);
    }

    [Fact]
    public async Task ChangePassword_without_a_token_is_401_with_the_sign_in_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The refresh cookie alone, as a browser sends it to every /api/auth URL: not a credential here.
        var response = await _factory.PostHttpRequest(
            BaseUrl, body: new ChangePasswordRequest(UserSeeder.DefaultPassword, NewPassword), cookie: _laptop.RefreshCookie);

        //Assert
        // A 401, not a 500: authorization challenges the request before the per-user rate limit,
        // which has no user to count it by, ever sees it.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.PasswordHasher.VerifiedHashes);
        await AssertNothingChangedAsync(cancellationToken);
    }

    [Fact]
    public async Task ChangePassword_with_the_token_of_a_disabled_user_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Disabled after signing in: the token is valid in itself, the session is live.
        var disabled = await _users.CreateAuthorized("disabled", isDisabled: true);

        //Act
        var response = await ChangePasswordAsync(disabled, UserSeeder.DefaultPassword, NewPassword);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(disabled.User.PasswordHash, (await db.Users.SingleAsync(u => u.Id == disabled.User.Id, cancellationToken)).PasswordHash);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // MAM-16 — the window between the current-password check and the save. The owner's reset is how
    // an account is taken back from whoever knows its old password, which is exactly who would be
    // changing it here; a reset, a disable or a delete that commits inside the window must win. The
    // hasher runs the owner's change inside this request's own password check, which is exactly where
    // a racing request would land.
    [Theory]
    [InlineData("password reset")]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task ChangePassword_overtaken_by_a_change_to_the_user_during_the_password_check_is_400_and_the_owners_change_stands(string change)
    {
        //Arrange
        const string ownersPassword = "the-owners-choice";
        var cancellationToken = TestContext.Current.CancellationToken;
        var hasher = _factory.Resolve<IPasswordHasher>();
        var owner = await _users.CreateAuthorizedOwner("owner");
        var memberId = _laptop.User.Id;
        HttpStatusCode? changeStatus = null;
        _factory.PasswordHasher.OnNextVerify(async () =>
        {
            var changed = change switch
            {
                "password reset" => await _factory.PostHttpRequest($"{UsersUrl}/{memberId}/password", owner.AccessToken, new ResetPasswordRequest(ownersPassword)),
                "disable" => await _factory.PostHttpRequest($"{UsersUrl}/{memberId}/disable", owner.AccessToken),
                "delete" => await _factory.DeleteHttpRequest($"{UsersUrl}/{memberId}", owner.AccessToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
            };
            changeStatus = changed.StatusCode;
        });

        //Act
        var response = await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);

        //Assert
        // The change went through, in the middle of a check of the password that was right when the
        // check began.
        Assert.True(changeStatus is HttpStatusCode.OK or HttpStatusCode.NoContent, $"The {change} answered {changeStatus}.");
        Assert.Equal(_laptop.User.PasswordHash, Assert.Single(_factory.PasswordHasher.VerifiedHashes));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CurrentPasswordIncorrect, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.AuditEvents.CountAsync(a => a.Action == AuditAction.UserPasswordChanged, cancellationToken));
            var row = await db.Users.SingleOrDefaultAsync(u => u.Id == memberId, cancellationToken);
            switch (change)
            {
                case "password reset":
                    Assert.True(hasher.Verify(ownersPassword, row!.PasswordHash));
                    break;
                case "disable":
                    Assert.True(row!.IsDisabled);
                    Assert.Equal(_laptop.User.PasswordHash, row.PasswordHash);
                    break;
                default:
                    Assert.Null(row);
                    break;
            }
        });
    }

    // MAM-16 — counted per user, not per address like sign-in: the guesses of someone holding
    // a user's token count together from wherever they come, and a user mistyping on Profile neither
    // limits another user behind the same address nor uses up that address's sign-ins.
    [Fact]
    public async Task ChangePassword_is_rate_limited_per_user_whatever_the_client_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bob = await _users.CreateAuthorized("bob");
        _factory.PinClientAddress();
        var allowed = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 10; attempt++)
            allowed.Add((await ChangePasswordAsync(_laptop, "not-the-password", NewPassword)).StatusCode);

        //Act
        var bobFromTheSameAddress = await ChangePasswordAsync(bob, UserSeeder.DefaultPassword, NewPassword);
        var signInFromTheSameAddress = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("member", UserSeeder.DefaultPassword));
        _factory.PinClientAddress();
        // The right password now, from another address: the limit counts the user's attempts, not
        // their failures and not where they come from.
        var limited = await ChangePasswordAsync(_laptop, UserSeeder.DefaultPassword, NewPassword);

        //Assert
        Assert.All(allowed, status => Assert.Equal(HttpStatusCode.BadRequest, status));
        Assert.Equal(HttpStatusCode.NoContent, bobFromTheSameAddress.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signInFromTheSameAddress.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many attempts. Wait a minute and try again.", await limited.ReadErrorAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(_laptop.User.PasswordHash, (await db.Users.SingleAsync(u => u.Id == _laptop.User.Id, cancellationToken)).PasswordHash));
    }

    private Task<HttpResponseMessage> ChangePasswordAsync(AuthorizedUser from, string currentPassword, string newPassword) =>
        _factory.PostHttpRequest(BaseUrl, from.AccessToken, new ChangePasswordRequest(currentPassword, newPassword));

    // What a refused change leaves as it was: the member's password, both of their sessions, and an
    // audit trail with nothing in it.
    private Task AssertNothingChangedAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            Assert.Equal(_laptop.User.PasswordHash, (await db.Users.SingleAsync(u => u.Id == _laptop.User.Id, cancellationToken)).PasswordHash);
            Assert.Equal(0, await db.UserSessions.CountAsync(s => s.UserId == _laptop.User.Id && s.RevokedUtc != null, cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
