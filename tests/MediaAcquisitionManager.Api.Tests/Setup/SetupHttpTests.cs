using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace MediaAcquisitionManager.Api.Tests.Setup;

public sealed class SetupHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/setup";
    private const string MeUrl = "/api/auth/me";

    // The cookie's name and path are a contract with every signed-in browser; spelled out so a
    // change fails here first.
    private const string RefreshCookieName = "mam_refresh";
    private const string RefreshCookiePath = "/api/auth";

    private const string OwnerName = "Owner.One";
    private const string OwnerPassword = "owner-password-1";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;

    public SetupHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Get_on_a_fresh_install_is_200_with_setup_required()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SetupStatusResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.True(body!.IsRequired);
    }

    [Fact]
    public async Task Get_once_an_owner_exists_is_200_with_setup_not_required()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.CreateOwner();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SetupStatusResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.False(body!.IsRequired);
    }

    [Fact]
    public async Task Complete_closes_setup()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<SetupStatusResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.False(body!.IsRequired);
    }

    [Fact]
    public async Task Complete_on_a_fresh_install_is_200_with_the_owner_signed_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var me = await _factory.GetHttpRequest(MeUrl, body!.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(body.AccessToken);
        Assert.Equal(now.AddMinutes(15), body.AccessTokenExpiresUtc);
        Assert.Equal(OwnerName, body.User.UserName);
        Assert.True(body.User.IsOwner);
        // MAM-46 — a new owner has saved no language: the web app follows the browser's.
        Assert.Null(body.User.Language);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var current = await me.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        // The setup is the owner's first sign-in, and its session is the one /me asks from.
        Assert.Equal(new CurrentUserProfileResponse(body.User.Id, OwnerName, IsOwner: true, now, now, now.AddDays(30), Language: null), current);
    }

    [Fact]
    public async Task Complete_sets_the_refresh_cookie_http_only_same_site_strict_on_the_auth_path()
    {
        //Arrange
        var expires = _factory.Time.GetUtcNow().AddDays(30);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Assert
        var cookie = response.ReadSetCookie(RefreshCookieName);
        Assert.NotNull(cookie);
        Assert.NotEmpty(cookie.Value);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal(RefreshCookiePath, cookie.Path);
        // The test server speaks plain HTTP, like a LAN install: a Secure cookie would never be stored.
        Assert.False(cookie.Secure);
        Assert.Equal(expires, cookie.Expires!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Complete_persists_the_owner_with_a_hashed_password()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var hasher = _factory.Resolve<IPasswordHasher>();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var owner = await db.Users.SingleAsync(cancellationToken);
            Assert.Equal(OwnerName, owner.UserName);
            Assert.Equal("OWNER.ONE", owner.NormalizedUserName);
            Assert.NotEqual(OwnerPassword, owner.PasswordHash);
            Assert.DoesNotContain(OwnerPassword, owner.PasswordHash, StringComparison.Ordinal);
            Assert.True(hasher.Verify(OwnerPassword, owner.PasswordHash));
            Assert.False(owner.IsDisabled);
            Assert.Equal(now, owner.CreatedUtc);
            // The owner is signed in by the setup itself.
            Assert.Equal(now, owner.LastSignInUtc);
        });
    }

    [Fact]
    public async Task Complete_stores_the_user_name_trimmed_as_typed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest("  Alice  ", OwnerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var owner = await db.Users.SingleAsync(cancellationToken);
            Assert.Equal("Alice", owner.UserName);
            Assert.Equal("ALICE", owner.NormalizedUserName);
        });
    }

    [Fact]
    public async Task Complete_persists_the_installation_row_naming_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var installation = await db.Installations.SingleAsync(cancellationToken);
            Assert.Equal(new Guid("c0000000-0000-0000-0000-000000000001"), installation.Id);
            Assert.Equal(body!.User.Id, installation.OwnerUserId);
            Assert.Equal(now, installation.SetupCompletedUtc);
        });
    }

    [Fact]
    public async Task Complete_persists_a_session_that_stores_only_the_hash_of_the_refresh_cookie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var cookie = response.ReadSetCookie(RefreshCookieName)!;
        await _factory.WithDbAsync(async db =>
        {
            var session = await db.UserSessions.SingleAsync(cancellationToken);
            Assert.Equal(body!.User.Id, session.UserId);
            Assert.NotEqual(cookie.Value, session.RefreshTokenHash);
            Assert.Equal(RefreshTokenHash.Of(cookie.Value), session.RefreshTokenHash);
            Assert.Null(session.PreviousRefreshTokenHash);
            Assert.Equal(now, session.CreatedUtc);
            Assert.Equal(now, session.LastUsedUtc);
            Assert.Equal(now.AddDays(30), session.ExpiresUtc);
            Assert.Null(session.RevokedUtc);
        });
    }

    [Fact]
    public async Task Complete_records_a_setup_completed_audit_row_with_the_owner_as_actor_and_target()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var ownerId = body!.User.Id;
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.SetupCompleted, audit.Action);
            Assert.Equal(ownerId, audit.ActorUserId);
            Assert.Equal(OwnerName, audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            Assert.Equal(ownerId, audit.TargetId);
            Assert.Equal(OwnerName, audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);

            // Stored by name, so reordering the enum can never change what the row says.
            var stored = await db.Database
                .SqlQuery<string>($"SELECT \"Action\" AS \"Value\" FROM \"AuditEvents\"")
                .SingleAsync(cancellationToken);
            Assert.Equal("SetupCompleted", stored);
        });
    }

    [Fact]
    public async Task Complete_a_second_time_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));
        var owner = await first.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest("intruder", "intruder-password"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("This installation is already set up. Sign in instead.", await response.ReadErrorAsync(cancellationToken));
        Assert.Null(response.ReadSetCookie(RefreshCookieName));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(owner!.User.Id, (await db.Users.SingleAsync(cancellationToken)).Id);
            Assert.Equal(owner.User.Id, (await db.Installations.SingleAsync(cancellationToken)).OwnerUserId);
            Assert.Equal(1, await db.UserSessions.CountAsync(cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Complete_twice_at_once_creates_exactly_one_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // Both may pass the "not set up yet" check; the installation row's fixed key must let only
        // one of them commit.
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, body: new SetupRequest("first-owner", OwnerPassword)),
            _factory.PostHttpRequest(BaseUrl, body: new SetupRequest("second-owner", OwnerPassword)));

        //Assert
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var winner = await responses.Single(response => response.IsSuccessStatusCode).Content
            .ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(winner!.User.Id, (await db.Users.SingleAsync(cancellationToken)).Id);
            Assert.Equal(winner.User.Id, (await db.Installations.SingleAsync(cancellationToken)).OwnerUserId);
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("ab", "A user name must be 3 to 32 characters long.")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", "A user name must be 3 to 32 characters long.")]
    [InlineData("two words", "A user name can contain only letters, digits, dots, hyphens and underscores.")]
    [InlineData("owner!", "A user name can contain only letters, digits, dots, hyphens and underscores.")]
    public async Task Complete_with_an_invalid_user_name_is_400_with_the_rule_and_writes_nothing(string userName, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(userName, OwnerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [InlineData(7, "A password must be at least 8 characters long.")]
    [InlineData(129, "A password can be at most 128 characters long.")]
    public async Task Complete_with_a_password_of_the_wrong_length_is_400_with_the_rule_and_writes_nothing(int length, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, new string('p', length)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    // MAM-4 — a blank or missing field reaches the rules and gets their sentence, which the setup page
    // shows as is; a framework validation body has no "error" for it to show.
    [Theory]
    [InlineData("empty user name", "A user name must be 3 to 32 characters long.")]
    [InlineData("whitespace-only user name", "A user name must be 3 to 32 characters long.")]
    [InlineData("missing user name", "A user name must be 3 to 32 characters long.")]
    [InlineData("empty password", "A password must be at least 8 characters long.")]
    [InlineData("whitespace-only password", "A password must be at least 8 characters long.")]
    [InlineData("missing password", "A password must be at least 8 characters long.")]
    [InlineData("empty body", "A user name must be 3 to 32 characters long.")]
    public async Task Complete_with_a_blank_or_missing_field_is_400_with_the_rule_and_writes_nothing(string blank, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        object body = blank switch
        {
            "empty user name" => new SetupRequest("", OwnerPassword),
            "whitespace-only user name" => new SetupRequest("   ", OwnerPassword),
            "missing user name" => new { password = OwnerPassword },
            "empty password" => new SetupRequest(OwnerName, ""),
            "whitespace-only password" => new SetupRequest(OwnerName, "   "),
            "missing password" => new { userName = OwnerName },
            "empty body" => new { },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Complete_with_blank_fields_once_an_owner_exists_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await _users.CreateOwner("owner");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest("", ""));

        //Assert
        // Closed before the body is looked at: once there is an owner, the answer is the same
        // whatever the request carries.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("This installation is already set up. Sign in instead.", await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(owner.Id, (await db.Users.SingleAsync(cancellationToken)).Id);
            Assert.Equal(0, await db.UserSessions.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Complete_is_rate_limited_per_client_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.PinClientAddress();
        var allowed = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 10; attempt++)
            allowed.Add((await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest("ab", OwnerPassword))).StatusCode);

        //Act
        var limited = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));
        _factory.PinClientAddress();
        var fromAnotherAddress = await _factory.PostHttpRequest(BaseUrl, body: new SetupRequest(OwnerName, OwnerPassword));

        //Assert
        Assert.All(allowed, status => Assert.Equal(HttpStatusCode.BadRequest, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many attempts. Wait a minute and try again.", await limited.ReadErrorAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.OK, fromAnotherAddress.StatusCode);
    }

    private async Task AssertNothingWrittenAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Users.CountAsync(cancellationToken));
            Assert.Equal(0, await db.Installations.CountAsync(cancellationToken));
            Assert.Equal(0, await db.UserSessions.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
