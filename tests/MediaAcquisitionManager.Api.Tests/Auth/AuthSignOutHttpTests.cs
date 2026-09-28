using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Auth;

public sealed class AuthSignOutHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/auth/sign-out";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string RefreshUrl = "/api/auth/refresh";
    private const string MeUrl = "/api/auth/me";
    private const string RefreshCookieName = "mam_refresh";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;

    public AuthSignOutHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SignOut_is_204_and_deletes_the_cookie()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, signedIn.AccessToken, cookie: signedIn.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task SignOut_revokes_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.PostHttpRequest(BaseUrl, signedIn.AccessToken, cookie: signedIn.RefreshCookie);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var session = await db.UserSessions.SingleAsync(s => s.Id == signedIn.SessionId, cancellationToken);
            Assert.Equal(now, session.RevokedUtc);
        });
    }

    [Fact]
    public async Task SignOut_stops_the_access_token_at_once_and_the_cookie_can_no_longer_refresh()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();

        //Act
        await _factory.PostHttpRequest(BaseUrl, signedIn.AccessToken, cookie: signedIn.RefreshCookie);
        var me = await _factory.GetHttpRequest(MeUrl, signedIn.AccessToken);
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: signedIn.RefreshCookie);

        //Assert
        // Not 15 minutes later when the token expires: the session check refuses it on the next request.
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task SignOut_with_an_expired_access_token_still_ends_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized();
        _factory.Time.Advance(TimeSpan.FromMinutes(20));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, signedIn.AccessToken, cookie: signedIn.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.NotNull((await db.UserSessions.SingleAsync(s => s.Id == signedIn.SessionId, cancellationToken)).RevokedUtc));
    }

    [Fact]
    public async Task SignOut_without_a_cookie_is_204_and_deletes_the_cookie()
    {
        //Arrange

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task SignOut_ends_only_the_session_of_its_cookie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");
        var laptop = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));
        var phone = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));
        var phoneToken = (await phone.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.AccessToken;
        var laptopCookie = laptop.ReadSetCookie(RefreshCookieName)!;

        //Act
        await _factory.PostHttpRequest(BaseUrl, cookie: laptopCookie.RequestHeader);
        var meOnThePhone = await _factory.GetHttpRequest(MeUrl, phoneToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, meOnThePhone.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.UserSessions.CountAsync(s => s.RevokedUtc == null, cancellationToken)));
    }

    [Fact]
    public async Task SignOut_with_the_cookie_a_refresh_just_replaced_revokes_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));
        var firstCookie = signIn.ReadSetCookie(RefreshCookieName)!;
        // Another tab's refresh with the same cookie lands first; its answer (the rotated cookie) may
        // reach the browser after the sign-out's deletion, and must not keep the user signed in.
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: firstCookie.RequestHeader);
        var rotatedCookie = refresh.ReadSetCookie(RefreshCookieName)!;
        var rotatedAccessToken = (await refresh.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.AccessToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, cookie: firstCookie.RequestHeader);
        var me = await _factory.GetHttpRequest(MeUrl, rotatedAccessToken);
        var refreshWithTheRotatedCookie = await _factory.PostHttpRequest(RefreshUrl, cookie: rotatedCookie.RequestHeader);

        //Assert
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshWithTheRotatedCookie.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(now, (await db.UserSessions.SingleAsync(cancellationToken)).RevokedUtc));
    }

    [Fact]
    public async Task SignOut_over_https_deletes_the_cookie_as_secure()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://localhost{BaseUrl}");
        request.Headers.Add("Cookie", signedIn.RefreshCookie);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDeletesTheCookie(response);
        // The deletion is written with the cookie's own options, so over HTTPS it carries Secure like
        // the cookie an HTTPS sign-in set.
        Assert.True(response.ReadSetCookie(RefreshCookieName)!.Secure);
    }

    private void AssertDeletesTheCookie(HttpResponseMessage response)
    {
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        Assert.Empty(cookie.Value);
        Assert.Equal("/api/auth", cookie.Path);
        Assert.True(cookie.Expires < _factory.Time.GetUtcNow());
    }
}
