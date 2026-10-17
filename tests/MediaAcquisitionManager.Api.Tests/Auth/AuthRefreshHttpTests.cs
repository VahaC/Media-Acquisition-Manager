using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace MediaAcquisitionManager.Api.Tests.Auth;

public sealed class AuthRefreshHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/auth/refresh";
    private const string MeUrl = "/api/auth/me";
    private const string RefreshCookieName = "mam_refresh";
    private const string SessionEnded = "Your session has ended. Sign in again.";

    // The design's numbers, spelled out: the session lives 30 days from its last use, and a replaced
    // token still counts as a race for 30 seconds.
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ReplayGrace = TimeSpan.FromSeconds(30);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;

    public AuthRefreshHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_issues_a_working_access_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorizedOwner("owner");
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var me = await _factory.GetHttpRequest(MeUrl, body!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CurrentUserResponse(signedIn.User.Id, "owner", IsOwner: true, Language: null), body.User);
        Assert.Equal(now.AddMinutes(15), body.AccessTokenExpiresUtc);
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        Assert.NotEmpty(cookie.Value);
        Assert.NotEqual(signedIn.RefreshToken, cookie.Value);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    // MAM-46 — every start of the web app goes through a refresh, so the account's language comes with it, not only with
    // the Profile's /me.
    [Fact]
    public async Task Refresh_carries_the_language_the_user_saved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorizedOwner("owner");
        await _users.SetLanguage(signedIn.User, "en");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new CurrentUserResponse(signedIn.User.Id, "owner", IsOwner: true, Language: "en"), body!.User);
    }

    [Fact]
    public async Task Refresh_sets_the_rotated_cookie_with_the_same_attributes_and_a_later_expiry()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();
        _factory.Time.Advance(TimeSpan.FromDays(3));
        var expires = _factory.Time.GetUtcNow().Add(IdleLifetime);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);

        //Assert
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/auth", cookie.Path);
        Assert.False(cookie.Secure);
        Assert.Equal(expires, cookie.Expires!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Refresh_stores_the_new_hash_keeps_the_replaced_one_and_extends_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();
        _factory.Time.Advance(TimeSpan.FromDays(3));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);

        //Assert
        var cookie = response.ReadSetCookie(RefreshCookieName)!;
        await _factory.WithDbAsync(async db =>
        {
            var session = await db.UserSessions.SingleAsync(cancellationToken);
            Assert.Equal(signedIn.SessionId, session.Id);
            Assert.Equal(RefreshTokenHash.Of(cookie.Value), session.RefreshTokenHash);
            Assert.Equal(RefreshTokenHash.Of(signedIn.RefreshToken), session.PreviousRefreshTokenHash);
            Assert.Equal(now, session.RotatedUtc);
            Assert.Equal(now, session.LastUsedUtc);
            Assert.Equal(now.Add(IdleLifetime), session.ExpiresUtc);
            Assert.Null(session.RevokedUtc);
        });
    }

    [Fact]
    public async Task Refresh_with_the_replaced_cookie_within_the_grace_is_409_and_revokes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();
        var rotated = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);
        var newestCookie = rotated.ReadSetCookie(RefreshCookieName)!;
        _factory.Time.Advance(ReplayGrace);

        //Act
        // A second tab that sent the same cookie a moment after the first one's refresh.
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);
        var withNewestCookie = await _factory.PostHttpRequest(BaseUrl, cookie: newestCookie.RequestHeader);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("This session was refreshed a moment ago. Try again.", await response.ReadErrorAsync(cancellationToken));
        // The browser already holds the rotated cookie; deleting it here would sign the tab out.
        Assert.Null(response.ReadSetCookie(RefreshCookieName));
        Assert.Equal(HttpStatusCode.OK, withNewestCookie.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Null((await db.UserSessions.SingleAsync(cancellationToken)).RevokedUtc));
    }

    [Fact]
    public async Task Refresh_with_the_replaced_cookie_after_the_grace_is_401_and_revokes_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();
        var rotated = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);
        var rotatedBody = await rotated.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var newestCookie = rotated.ReadSetCookie(RefreshCookieName)!;
        _factory.Time.Advance(ReplayGrace + TimeSpan.FromSeconds(1));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        // The old cookie comes back long after it was replaced: someone else holds a copy.
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);
        var withNewestCookie = await _factory.PostHttpRequest(BaseUrl, cookie: newestCookie.RequestHeader);
        var me = await _factory.GetHttpRequest(MeUrl, rotatedBody!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionEnded, await response.ReadErrorAsync(cancellationToken));
        AssertDeletesTheCookie(response);
        // Revoked for both holders: even the newest cookie and its access token stop working.
        Assert.Equal(HttpStatusCode.Unauthorized, withNewestCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(now, (await db.UserSessions.SingleAsync(cancellationToken)).RevokedUtc));
    }

    [Fact]
    public async Task Refresh_twice_at_once_with_one_cookie_rotates_it_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();

        //Act
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie),
            _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie));

        //Assert
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var winnerCookie = responses.Single(response => response.IsSuccessStatusCode).ReadSetCookie(RefreshCookieName)!;
        await _factory.WithDbAsync(async db =>
        {
            var session = await db.UserSessions.SingleAsync(cancellationToken);
            Assert.Equal(RefreshTokenHash.Of(winnerCookie.Value), session.RefreshTokenHash);
            Assert.Null(session.RevokedUtc);
        });
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_401_and_deletes_the_cookie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionEnded, await response.ReadErrorAsync(cancellationToken));
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task Refresh_with_an_unknown_cookie_is_401_and_deletes_the_cookie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.CreateAuthorized();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: $"{RefreshCookieName}=not-a-token-anyone-was-given");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionEnded, await response.ReadErrorAsync(cancellationToken));
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task Refresh_of_a_session_unused_for_30_days_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();
        _factory.Time.Advance(IdleLifetime + TimeSpan.FromSeconds(1));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionEnded, await response.ReadErrorAsync(cancellationToken));
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task Refresh_of_a_session_unused_for_29_days_is_200()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();
        _factory.Time.Advance(TimeSpan.FromDays(29));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);

        //Assert
        // Guards the guard: the 30-day test fails on the session's age, not on anything else.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_of_a_disabled_user_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized(isDisabled: true);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: signedIn.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionEnded, await response.ReadErrorAsync(cancellationToken));
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task Refresh_is_not_rate_limited()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();
        // One address for every request, like a household's tabs behind one router.
        _factory.PinClientAddress();
        var cookie = signedIn.RefreshCookie;
        var statuses = new List<HttpStatusCode>();

        //Act
        // Past the sign-in limit of 10 a minute, each with the cookie the previous refresh set, so
        // none of them is a replay.
        for (var refresh = 0; refresh < SignInRateLimitPolicy.PermitLimit + 2; refresh++)
        {
            var response = await _factory.PostHttpRequest(BaseUrl, cookie: cookie);
            statuses.Add(response.StatusCode);
            cookie = response.ReadSetCookie(RefreshCookieName)?.RequestHeader ?? cookie;
        }

        //Assert
        // MAM-4 — the cookie is 256 random bits, and a limit would only sign out tabs that refresh
        // together from one address.
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
    }

    // Deleting a cookie is setting it again, empty, with a date in the past, on the same path.
    private void AssertDeletesTheCookie(HttpResponseMessage response)
    {
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        Assert.Empty(cookie.Value);
        Assert.Equal("/api/auth", cookie.Path);
        Assert.True(cookie.Expires < _factory.Time.GetUtcNow());
    }
}
