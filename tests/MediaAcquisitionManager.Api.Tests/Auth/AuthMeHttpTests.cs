using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediaAcquisitionManager.Api.Tests.Auth;

// /api/auth/me is the simplest endpoint behind [Authorize], so these tests also pin what every
// protected endpoint does with the access token it gets.
public sealed class AuthMeHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/auth/me";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string RefreshUrl = "/api/auth/refresh";
    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");

    // The design's number, spelled out: a session ends 30 days after it was last used.
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(30);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;

    public AuthMeHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Me_without_a_token_is_401_with_the_sign_in_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task Me_with_a_valid_token_is_200_with_the_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // A session started without a password sign-in, so the user has none on record.
        var signedIn = await _users.CreateAuthorized("alice");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            new CurrentUserProfileResponse(
                signedIn.User.Id, "alice", IsOwner: false, CreatedUtc: now, LastSignInUtc: null, SessionExpiresUtc: now + IdleLifetime, Language: null),
            body);
    }

    [Fact]
    public async Task Me_as_the_owner_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var owner = await _users.CreateAuthorizedOwner("owner");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            new CurrentUserProfileResponse(
                owner.User.Id, "owner", IsOwner: true, CreatedUtc: now, LastSignInUtc: null, SessionExpiresUtc: now + IdleLifetime, Language: null),
            body);
    }

    // MAM-46 — the Profile page's Language card shows the saved language.
    [Fact]
    public async Task Me_carries_the_language_the_user_saved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized("alice");
        await _users.SetLanguage(signedIn.User, "uk");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("uk", document.RootElement.GetProperty("language").GetString());
    }

    // MAM-46 — "as in the browser" is a null the web app reads, not a missing property.
    [Fact]
    public async Task Me_of_a_user_who_saved_no_language_carries_a_null_language()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized("alice");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("language").ValueKind);
    }

    // MAM-16 — the Profile page's Created and Last sign-in rows.
    [Fact]
    public async Task Me_after_a_sign_in_says_when_the_user_was_created_and_last_signed_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var createdUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _users.Create("alice");
        _factory.Time.Advance(TimeSpan.FromDays(2));
        var signedInUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));
        var signedIn = await signIn.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var body = JsonSerializer.Deserialize<CurrentUserProfileResponse>(text, MediaAcquisitionManagerApiFactory.JsonOptions);
        Assert.Equal(createdUtc, body!.CreatedUtc);
        Assert.Equal(signedInUtc, body.LastSignInUtc);
        Assert.Equal(signedInUtc + IdleLifetime, body.SessionExpiresUtc);
        // Each with the trailing Z: DateTime equality above ignores the Kind, and a value sent without
        // it would show on the Profile page shifted by the browser's offset.
        using var document = JsonDocument.Parse(text);
        Assert.EndsWith("Z", document.RootElement.GetProperty("createdUtc").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", document.RootElement.GetProperty("lastSignInUtc").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", document.RootElement.GetProperty("sessionExpiresUtc").GetString(), StringComparison.Ordinal);
    }

    // MAM-16 — the Profile page's session end is the end of the session it asks from, read by the
    // token's sid; a user signed in on two devices has two different ends.
    [Fact]
    public async Task Me_says_when_the_calling_session_ends_not_another_session_of_the_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var laptopStartedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var laptop = await _users.CreateAuthorized("alice");
        // Less than the access token's 15 minutes, so the laptop's token still works.
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var phoneStartedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var phone = await _users.CreateSession(laptop.User);

        //Act
        var fromTheLaptop = await _factory.GetHttpRequest(BaseUrl, laptop.AccessToken);
        var fromThePhone = await _factory.GetHttpRequest(BaseUrl, phone.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, fromTheLaptop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromThePhone.StatusCode);
        var laptopBody = await fromTheLaptop.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var phoneBody = await fromThePhone.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(laptopStartedUtc + IdleLifetime, laptopBody!.SessionExpiresUtc);
        Assert.Equal(phoneStartedUtc + IdleLifetime, phoneBody!.SessionExpiresUtc);
    }

    [Fact]
    public async Task Me_after_a_refresh_says_the_session_ends_30_days_from_the_refresh()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedIn = await _users.CreateAuthorized("alice");
        _factory.Time.Advance(TimeSpan.FromDays(1));
        var refreshedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: signedIn.RefreshCookie);
        var refreshed = await refresh.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, refreshed!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(refreshedUtc + IdleLifetime, body!.SessionExpiresUtc);
    }

    [Fact]
    public async Task Me_with_a_token_that_is_not_a_jwt_is_401_with_the_sign_in_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, "not-a-token");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Me_with_a_token_signed_with_another_key_is_401()
    {
        //Arrange
        // Everything right — issuer, audience, lifetime, a live session's ids — except the key.
        var signedIn = await _users.CreateAuthorized();
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "media-acquisition-manager",
            Audience = "media-acquisition-manager",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = signedIn.User.Id.ToString(),
                [JwtRegisteredClaimNames.Sid] = signedIn.SessionId.ToString(),
                [JwtRegisteredClaimNames.Name] = signedIn.User.UserName,
            },
        });

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, forged);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_hand_made_token_like_the_apps_own_is_200()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized("alice");
        var token = new JsonWebTokenHandler().CreateToken(TokenLikeTheAppsOwn(signedIn));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, token);

        //Assert
        // Guards the guard: the tokens below are made the same way, so each is refused for the one
        // thing it gets wrong.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // MAM-4 — signed with this installation's own key, so the signature is fine and only the check
    // that each case spoils can refuse it: the sub/sid pairing, the expiry, the pinned algorithm, the
    // session id, the issuer and the audience.
    [Theory]
    [InlineData("another user's id with this session")]
    [InlineData("no expiry")]
    [InlineData("signed with HS512")]
    [InlineData("no session id")]
    [InlineData("a session id that is not a guid")]
    [InlineData("another issuer")]
    [InlineData("another audience")]
    public async Task Me_with_a_token_signed_with_the_installation_key_but_one_flaw_is_401_with_the_sign_in_sentence(string flaw)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.CreateAuthorized("alice");
        // Enabled, with a live session of their own: only pairing their id with alice's session is wrong.
        var bob = await _users.CreateAuthorized("bob");
        var handler = new JsonWebTokenHandler();
        var descriptor = TokenLikeTheAppsOwn(alice);
        switch (flaw)
        {
            case "another user's id with this session":
                descriptor.Claims[JwtRegisteredClaimNames.Sub] = bob.User.Id.ToString();
                break;
            case "no expiry":
                // Left out, the handler would add an expiry of its own.
                handler.SetDefaultTimesOnTokenCreation = false;
                descriptor.Expires = null;
                break;
            case "signed with HS512":
                descriptor.SigningCredentials = new SigningCredentials(InstallationKey(), SecurityAlgorithms.HmacSha512);
                break;
            case "no session id":
                descriptor.Claims.Remove(JwtRegisteredClaimNames.Sid);
                break;
            case "a session id that is not a guid":
                descriptor.Claims[JwtRegisteredClaimNames.Sid] = "not-a-session";
                break;
            case "another issuer":
                descriptor.Issuer = "someone-else";
                break;
            case "another audience":
                descriptor.Audience = "someone-else";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(flaw), flaw, null);
        }

        var token = handler.CreateToken(descriptor);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, token);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Me_with_a_token_older_than_15_minutes_is_401()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();
        // Past the lifetime and the 30 seconds of clock skew. The session itself is still active,
        // so only the token's age can refuse it.
        _factory.Time.Advance(TimeSpan.FromMinutes(16));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_token_14_minutes_old_is_200()
    {
        //Arrange
        var signedIn = await _users.CreateAuthorized();
        _factory.Time.Advance(TimeSpan.FromMinutes(14));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn.AccessToken);

        //Assert
        // Guards the guard: the lifetime is read from the injected clock, and 14 minutes is inside it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_the_token_of_a_disabled_user_is_401()
    {
        //Arrange
        // A user disabled after signing in: the token is valid in itself, the session is live.
        var signedIn = await _users.CreateAuthorized(isDisabled: true);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, signedIn.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // What AccessTokenService issues for this session: this installation's key, HS256, the app's
    // issuer and audience, and 15 minutes on the test clock. Spelled out rather than read from the
    // app, so a test spoils one thing of a token the app would accept.
    private SecurityTokenDescriptor TokenLikeTheAppsOwn(AuthorizedUser signedIn)
    {
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        return new SecurityTokenDescriptor
        {
            Issuer = "media-acquisition-manager",
            Audience = "media-acquisition-manager",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            SigningCredentials = new SigningCredentials(InstallationKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = signedIn.User.Id.ToString(),
                [JwtRegisteredClaimNames.Sid] = signedIn.SessionId.ToString(),
                [JwtRegisteredClaimNames.Name] = signedIn.User.UserName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        };
    }

    private static SymmetricSecurityKey InstallationKey() =>
        new(Convert.FromBase64String(MediaAcquisitionManagerApiFactory.TestJwtSecret));
}
