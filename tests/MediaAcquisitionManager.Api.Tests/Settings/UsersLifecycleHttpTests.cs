using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Disabling, enabling and deleting users. A user's sessions must end the moment they are disabled
// or deleted — not when their access token runs out. MAM-5 — and so must their personal access
// tokens: a disable revokes them, one "revoked" row each naming the owner, so an enable doesn't bring
// them back; a delete takes their rows with the user (ADR 0040).
public sealed class UsersLifecycleHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/users";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string RefreshUrl = "/api/auth/refresh";
    private const string MeUrl = "/api/auth/me";
    private const string DownloadsUrl = "/api/downloads";

    // Byte for byte what every failed sign-in answers: a disabled user must not be told apart.
    private static readonly string InvalidCredentialsBody =
        ErrorBody.Json("The user name or password is incorrect.", "account.sign-in.invalid-credentials");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IAuditEventSeeder _auditEvents = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;

    public UsersLifecycleHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _auditEvents = new AuditEventSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Disable ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Disable_is_200_with_the_user_disabled_and_persists_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new UserDto(member.Id, "member", IsOwner: false, IsDisabled: true, member.CreatedUtc, LastSignInUtc: null, ActiveTokenCount: 0), body);
        await _factory.WithDbAsync(async db =>
            Assert.True((await db.Users.SingleAsync(u => u.Id == member.Id, cancellationToken)).IsDisabled));
    }

    [Fact]
    public async Task Disable_ends_every_session_of_the_user_and_no_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var laptop = await _users.CreateAuthorized("member");
        var phone = await _users.CreateSession(laptop.User);

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{laptop.User.Id}/disable", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.UserSessions.SingleAsync(s => s.Id == laptop.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(now, (await db.UserSessions.SingleAsync(s => s.Id == phone.SessionId, cancellationToken)).RevokedUtc);
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == _owner.SessionId, cancellationToken)).RevokedUtc);
        });
    }

    [Fact]
    public async Task Disable_stops_the_users_access_token_and_refresh_cookie_at_once()
    {
        //Arrange
        var member = await _users.CreateAuthorized("member");

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{member.User.Id}/disable", _owner.AccessToken);
        var me = await _factory.GetHttpRequest(MeUrl, member.AccessToken);
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: member.RefreshCookie);

        //Assert
        // Not 15 minutes later: the token was issued a moment ago and is valid in itself.
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // MAM-5 — revoked, not only refused while the user is disabled: a token stops at once either way, and the
    // revocation is what keeps an enable from bringing a script back.
    [Fact]
    public async Task Disable_revokes_the_users_personal_access_tokens_at_once_with_a_revoked_audit_row_each()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var member = await _users.Create("member");
        var laptop = await _tokens.Create(member, PersonalAccessTokenAccess.ReadAndDownload, name: "Laptop");
        var nas = await _tokens.Create(member, name: "NAS script");
        var ownersOwn = await _tokens.Create(_owner.User, name: "Owner's script");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);
        var laptopAfterwards = await _factory.GetHttpRequest(DownloadsUrl, laptop.Secret);
        var nasAfterwards = await _factory.GetHttpRequest(DownloadsUrl, nas.Secret);
        var ownersAfterwards = await _factory.GetHttpRequest(DownloadsUrl, ownersOwn.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The user as the Users page shows them now: no active token left.
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal((true, 0), (body!.IsDisabled, body.ActiveTokenCount));
        Assert.Equal(HttpStatusCode.Unauthorized, laptopAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nasAfterwards.StatusCode);
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
                    (AuditAction.UserDisabled, (Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.User, (Guid?)member.Id, (string?)"member", now),
                ],
                rows.Select(audit => (audit.Action, audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc)));
        });
    }

    [Fact]
    public async Task Disable_makes_the_users_sign_in_the_identical_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);

        //Act
        var response = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("member", UserSeeder.DefaultPassword));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(InvalidCredentialsBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Disable_records_a_user_disabled_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var member = await _users.Create("member");

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.UserDisabled, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            Assert.Equal(member.Id, audit.TargetId);
            Assert.Equal("member", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Disable_of_a_disabled_user_is_200_and_records_no_second_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.True(body!.IsDisabled);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Disable_twice_at_once_is_200_twice_and_records_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");

        //Act
        // A double click: both requests may read the user as enabled, but only one may change it.
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken),
            _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken));

        //Assert
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await _factory.WithDbAsync(async db =>
        {
            Assert.True((await db.Users.SingleAsync(u => u.Id == member.Id, cancellationToken)).IsDisabled);
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Disable_of_the_owner_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{_owner.User.Id}/disable", _owner.AccessToken);
        var me = await _factory.GetHttpRequest(MeUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ("The installation owner can't be disabled.", "account.users.owner-cannot-be-disabled"),
            await response.ReadErrorBodyAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False((await db.Users.SingleAsync(u => u.Id == _owner.User.Id, cancellationToken)).IsDisabled);
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == _owner.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Disable_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/disable", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNoAuditRowsAsync(cancellationToken);
    }

    // ── Enable ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enable_is_200_with_the_user_enabled_and_lets_them_sign_in_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member", isDisabled: true);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/enable", _owner.AccessToken);
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("member", UserSeeder.DefaultPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new UserDto(member.Id, "member", IsOwner: false, IsDisabled: false, member.CreatedUtc, LastSignInUtc: null, ActiveTokenCount: 0), body);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.False((await db.Users.SingleAsync(u => u.Id == member.Id, cancellationToken)).IsDisabled));
    }

    [Fact]
    public async Task Enable_does_not_bring_back_the_sessions_the_disable_ended()
    {
        //Arrange
        var member = await _users.CreateAuthorized("member");
        await _factory.PostHttpRequest($"{BaseUrl}/{member.User.Id}/disable", _owner.AccessToken);

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{member.User.Id}/enable", _owner.AccessToken);
        var me = await _factory.GetHttpRequest(MeUrl, member.AccessToken);
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: member.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Enable_does_not_bring_back_the_personal_access_tokens_the_disable_revoked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");
        var script = await _tokens.Create(member, PersonalAccessTokenAccess.ReadAndDownload);
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/disable", _owner.AccessToken);
        var revokedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/enable", _owner.AccessToken);
        var scriptAfterwards = await _factory.GetHttpRequest(DownloadsUrl, script.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal((false, 0), (body!.IsDisabled, body.ActiveTokenCount));
        Assert.Equal(HttpStatusCode.Unauthorized, scriptAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(revokedUtc, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            // The disable's one "revoked" row: the enable writes none, nor undoes it.
            Assert.Equal(1, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRevoked, cancellationToken));
        });
    }

    [Fact]
    public async Task Enable_records_a_user_enabled_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var member = await _users.Create("member", isDisabled: true);

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/enable", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.UserEnabled, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            Assert.Equal(member.Id, audit.TargetId);
            Assert.Equal("member", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Enable_of_an_enabled_user_is_200_and_records_no_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/enable", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.False(body!.IsDisabled);
        await AssertNoAuditRowsAsync(cancellationToken);
    }

    [Fact]
    public async Task Enable_twice_at_once_is_200_twice_and_records_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member", isDisabled: true);

        //Act
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/enable", _owner.AccessToken),
            _factory.PostHttpRequest($"{BaseUrl}/{member.Id}/enable", _owner.AccessToken));

        //Assert
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await _factory.WithDbAsync(async db =>
        {
            Assert.False((await db.Users.SingleAsync(u => u.Id == member.Id, cancellationToken)).IsDisabled);
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Enable_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/enable", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNoAuditRowsAsync(cancellationToken);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_is_204_and_removes_the_user_with_their_sessions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var laptop = await _users.CreateAuthorized("member");
        await _users.CreateSession(laptop.User);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{laptop.User.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == laptop.User.Id, cancellationToken));
            Assert.Equal(0, await db.UserSessions.CountAsync(s => s.UserId == laptop.User.Id, cancellationToken));
            Assert.Null((await db.UserSessions.SingleAsync(s => s.Id == _owner.SessionId, cancellationToken)).RevokedUtc);
        });
    }

    [Fact]
    public async Task Delete_stops_the_users_access_token_and_refresh_cookie_at_once()
    {
        //Arrange
        var member = await _users.CreateAuthorized("member");

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{member.User.Id}", _owner.AccessToken);
        var me = await _factory.GetHttpRequest(MeUrl, member.AccessToken);
        var refresh = await _factory.PostHttpRequest(RefreshUrl, cookie: member.RefreshCookie);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // MAM-5 — the rows go by the database's cascade, with the user's sessions: nothing of a deleted user signs in.
    [Fact]
    public async Task Delete_removes_the_users_personal_access_tokens_with_them_and_stops_them_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");
        var script = await _tokens.Create(member, PersonalAccessTokenAccess.ReadAndDownload);
        await _tokens.CreateRevoked(member);
        var ownersOwn = await _tokens.Create(_owner.User);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{member.Id}", _owner.AccessToken);
        var scriptAfterwards = await _factory.GetHttpRequest(DownloadsUrl, script.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, scriptAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal([ownersOwn.Token.Id], await db.PersonalAccessTokens.AsNoTracking().Select(token => token.Id).ToListAsync(cancellationToken));
            // Gone with the user, not revoked one by one: the deletion is the one row.
            Assert.Equal(AuditAction.UserDeleted, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task Delete_records_a_user_deleted_audit_row_that_keeps_the_users_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var member = await _users.Create("member");

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{member.Id}", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.UserDeleted, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            // The row outlives the user it is about, so it names them itself.
            Assert.Equal(member.Id, audit.TargetId);
            Assert.Equal("member", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Delete_keeps_the_audit_rows_the_user_acted_in_with_their_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");
        var other = await _users.Create("other");
        var earlier = await _auditEvents.Create(AuditAction.UserCreated, actor: member, target: other);

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{member.Id}", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.AuditEvents.SingleAsync(e => e.Id == earlier.Id, cancellationToken);
            // SetNull: the id goes with the user, the name stays so the row still says who did it.
            Assert.Null(row.ActorUserId);
            Assert.Equal("member", row.ActorUserName);
            Assert.Equal(AuditAction.UserCreated, row.Action);
            Assert.Equal(other.Id, row.TargetId);
            Assert.Equal("other", row.TargetName);
        });
    }

    [Fact]
    public async Task Delete_of_the_owner_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_owner.User.Id}", _owner.AccessToken);
        var me = await _factory.GetHttpRequest(MeUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ("The installation owner can't be deleted.", "account.users.owner-cannot-be-deleted"),
            await response.ReadErrorBodyAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.True(await db.Users.AnyAsync(u => u.Id == _owner.User.Id, cancellationToken));
            Assert.Equal(_owner.User.Id, (await db.Installations.SingleAsync(cancellationToken)).OwnerUserId);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Delete_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNoAuditRowsAsync(cancellationToken);
    }

    [Fact]
    public async Task Delete_twice_at_once_deletes_once_and_records_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await _users.Create("member");

        //Act
        // A double click: the later request either finds the user gone or loses the delete itself.
        var responses = await Task.WhenAll(
            _factory.DeleteHttpRequest($"{BaseUrl}/{member.Id}", _owner.AccessToken),
            _factory.DeleteHttpRequest($"{BaseUrl}/{member.Id}", _owner.AccessToken));

        //Assert
        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.NotFound],
            responses.Select(response => response.StatusCode).Order().ToArray());
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == member.Id, cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    private async Task AssertNoAuditRowsAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
}
