using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace MediaAcquisitionManager.Api.Tests.Auth;

public sealed class AuthSignInHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/auth/sign-in";
    private const string MeUrl = "/api/auth/me";
    private const string UsersUrl = "/api/settings/users";
    private const string RefreshCookieName = "mam_refresh";

    // The whole body, byte for byte: every failed sign-in must answer exactly this, so the response
    // can't tell an unknown user from a wrong password or a disabled account.
    private const string InvalidCredentialsBody = """{"error":"The user name or password is incorrect."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;

    public AuthSignInHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SignIn_with_the_right_credentials_is_200_with_a_working_access_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var user = await _users.Create("alice");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var me = await _factory.GetHttpRequest(MeUrl, body!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CurrentUserResponse(user.Id, "alice", IsOwner: false, Language: null), body.User);
        Assert.Equal(now.AddMinutes(15), body.AccessTokenExpiresUtc);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    // MAM-46 — the web app switches to the account's language as soon as the session is set.
    [Fact]
    public async Task SignIn_carries_the_language_the_user_saved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await _users.Create("alice");
        await _users.SetLanguage(user, "uk");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new CurrentUserResponse(user.Id, "alice", IsOwner: false, Language: "uk"), body!.User);
    }

    [Fact]
    public async Task SignIn_as_the_owner_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.CreateOwner("owner");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("owner", UserSeeder.DefaultPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.True(body!.User.IsOwner);
    }

    [Fact]
    public async Task SignIn_sets_the_refresh_cookie_http_only_same_site_strict_on_the_auth_path()
    {
        //Arrange
        var expires = _factory.Time.GetUtcNow().AddDays(30);
        await _users.Create("alice");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        Assert.NotEmpty(cookie.Value);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/auth", cookie.Path);
        Assert.False(cookie.Secure);
        Assert.Equal(expires, cookie.Expires!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SignIn_over_https_sets_the_refresh_cookie_secure_http_only_same_site_strict_on_the_auth_path()
    {
        //Arrange
        await _users.Create("alice");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://localhost{BaseUrl}")
        {
            Content = JsonContent.Create(new SignInRequest("alice", UserSeeder.DefaultPassword), options: MediaAcquisitionManagerApiFactory.JsonOptions),
        };

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        // The other cookie tests speak plain HTTP, where Secure must stay off; over HTTPS the 30-day
        // credential must never be sent in the clear.
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/auth", cookie.Path);
    }

    [Fact]
    public async Task SignIn_records_the_sign_in_time_and_persists_a_session_for_the_cookie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var user = await _users.Create("alice");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        var cookie = response.ReadSetCookie(RefreshCookieName)!;
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.Users.SingleAsync(u => u.Id == user.Id, cancellationToken)).LastSignInUtc);
            var session = await db.UserSessions.SingleAsync(cancellationToken);
            Assert.Equal(user.Id, session.UserId);
            Assert.Equal(RefreshTokenHash.Of(cookie.Value), session.RefreshTokenHash);
            Assert.Equal(now, session.CreatedUtc);
            Assert.Equal(now, session.LastUsedUtc);
            Assert.Equal(now.AddDays(30), session.ExpiresUtc);
            Assert.Null(session.RevokedUtc);
        });
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("ALICE")]
    [InlineData("aLiCe")]
    [InlineData("  Alice  ")]
    public async Task SignIn_ignores_the_letter_case_and_surrounding_whitespace_of_the_user_name(string typed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await _users.Create("Alice");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest(typed, UserSeeder.DefaultPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        // The name as it was created, whatever the sign-in typed.
        Assert.Equal(new CurrentUserResponse(user.Id, "Alice", IsOwner: false, Language: null), body!.User);
    }

    [Theory]
    [InlineData("unknown user")]
    [InlineData("wrong password")]
    [InlineData("password in the wrong letter case")]
    [InlineData("disabled user")]
    [InlineData("blank user name")]
    [InlineData("blank password")]
    [InlineData("missing fields")]
    [InlineData("password longer than any allowed")]
    public async Task SignIn_failure_is_the_identical_401_and_starts_no_session(string failure)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.Create("alice");
        var bob = await _users.Create("bob", isDisabled: true);
        var request = failure switch
        {
            "unknown user" => new SignInRequest("nobody", UserSeeder.DefaultPassword),
            "wrong password" => new SignInRequest("alice", "not-the-password"),
            "password in the wrong letter case" => new SignInRequest("alice", UserSeeder.DefaultPassword.ToUpperInvariant()),
            "disabled user" => new SignInRequest("bob", UserSeeder.DefaultPassword),
            "blank user name" => new SignInRequest("", UserSeeder.DefaultPassword),
            "blank password" => new SignInRequest("alice", ""),
            "missing fields" => new SignInRequest(null, null),
            "password longer than any allowed" => new SignInRequest("alice", UserSeeder.DefaultPassword + new string('x', 200)),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: request);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InvalidCredentialsBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Null(response.ReadSetCookie(RefreshCookieName));
        // MAM-4 — the same work too, or the timing tells what the body doesn't: exactly one password
        // hash per attempt, at the cost of a real one. An unknown or blank name is checked against the
        // dummy hash, which must have been made at the stored hashes' iteration count.
        var verified = Assert.Single(_factory.PasswordHasher.VerifiedHashes);
        Assert.StartsWith(IterationPrefixOf(alice.PasswordHash), verified, StringComparison.Ordinal);
        var expectedHash = failure switch
        {
            "unknown user" or "blank user name" or "missing fields" => _factory.Resolve<UnknownUserPasswordHash>().Value,
            "disabled user" => bob.PasswordHash,
            _ => alice.PasswordHash,
        };
        Assert.Equal(expectedHash, verified);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.UserSessions.CountAsync(cancellationToken));
            Assert.All(await db.Users.ToListAsync(cancellationToken), user => Assert.Null(user.LastSignInUtc));
        });
    }

    // MAM-4 — the window between the password check and the new session. A reset (how the owner
    // locks out a leaked password), a disable or a delete that commits inside it must not leave a
    // session behind that outlives the change. The hasher runs the owner's change inside this
    // sign-in's own password check, which is exactly where a racing request would land.
    [Theory]
    [InlineData("password reset")]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task SignIn_overtaken_by_a_change_to_the_user_during_the_password_check_is_the_identical_401_and_starts_no_session(string change)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await _users.CreateAuthorizedOwner("owner");
        var alice = await _users.Create("alice");
        HttpStatusCode? changeStatus = null;
        _factory.PasswordHasher.OnNextVerify(async () =>
        {
            var changed = change switch
            {
                "password reset" => await _factory.PostHttpRequest($"{UsersUrl}/{alice.Id}/password", owner.AccessToken, new ResetPasswordRequest("a-brand-new-password")),
                "disable" => await _factory.PostHttpRequest($"{UsersUrl}/{alice.Id}/disable", owner.AccessToken),
                "delete" => await _factory.DeleteHttpRequest($"{UsersUrl}/{alice.Id}", owner.AccessToken),
                _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
            };
            changeStatus = changed.StatusCode;
        });

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        // The change went through, in the middle of a check of the password that was right when the
        // check began.
        Assert.True(changeStatus is HttpStatusCode.OK or HttpStatusCode.NoContent, $"The {change} answered {changeStatus}.");
        Assert.Equal(alice.PasswordHash, Assert.Single(_factory.PasswordHasher.VerifiedHashes));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(InvalidCredentialsBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Null(response.ReadSetCookie(RefreshCookieName));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.UserSessions.CountAsync(s => s.UserId == alice.Id, cancellationToken));
            Assert.Null((await db.Users.SingleOrDefaultAsync(u => u.Id == alice.Id, cancellationToken))?.LastSignInUtc);
        });
    }

    [Fact]
    public async Task SignIn_is_rate_limited_per_client_address_whatever_x_forwarded_for_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");
        _factory.PinClientAddress();
        var allowed = new List<HttpStatusCode>();
        // A new X-Forwarded-For on every attempt, as a guessing run would send: the limit must count
        // the connection's address, never take a header's word for who is asking.
        for (var attempt = 0; attempt < 10; attempt++)
            allowed.Add((await SignInForwardedForAsync($"203.0.113.{attempt}", "not-the-password")).StatusCode);

        //Act
        // The right password now: the limit counts attempts, not failures.
        var limited = await SignInForwardedForAsync("203.0.113.10", UserSeeder.DefaultPassword);
        _factory.PinClientAddress();
        var fromAnotherAddress = await _factory.PostHttpRequest(BaseUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        Assert.All(allowed, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many attempts. Wait a minute and try again.", await limited.ReadErrorAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        Assert.Null(limited.ReadSetCookie(RefreshCookieName));
        Assert.Equal(HttpStatusCode.OK, fromAnotherAddress.StatusCode);
    }

    private Task<HttpResponseMessage> SignInForwardedForAsync(string forwardedFor, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(new SignInRequest("alice", password), options: MediaAcquisitionManagerApiFactory.JsonOptions),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return _factory.SendHttpRequest(request);
    }

    // "pbkdf2-sha256$<iterations>$": the algorithm and the cost, without the salt and the hash.
    private static string IterationPrefixOf(string passwordHash) =>
        string.Join('$', passwordHash.Split('$')[..2]) + "$";
}
