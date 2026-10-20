using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-5 — the owner's view of everyone's personal access tokens on the Users page (ADR 0040): each user's count of active
// tokens — neither revoked nor expired, as the sign-in check counts them — and Revoke tokens, which revokes every token of
// that user not revoked yet at once, one audit row per token naming the owner, and nobody else's. A user with none is 204
// with nothing recorded; an unknown user is a bare 404; like every Users endpoint it is the owner's (a member is 403 with
// the owner sentence), and like every settings action it refuses any token, the owner's too.
public sealed class UsersTokensHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/users";
    private const string DownloadsUrl = "/api/downloads";
    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");
    private static readonly string OwnerOnlyBody = ErrorBody.Json("Only the installation owner can do this.", "account.owner-only");

    // Spelled out rather than read from PersonalAccessTokenErrors: this is what a script's author reads.
    private static readonly string NotForTokensBody = ErrorBody.Json(
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.",
        "account.token.not-for-tokens");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private UserEntity _member = null!;

    public UsersTokensHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<PersonalAccessTokenAccess> Accesses => new()
    {
        PersonalAccessTokenAccess.ReadOnly,
        PersonalAccessTokenAccess.ReadAndDownload,
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.Create("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Revoke tokens ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeTokens_is_204_and_stops_every_token_of_the_user_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var laptop = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload, name: "Laptop");
        var nas = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadOnly, _factory.Time.GetUtcNow().UtcDateTime.AddDays(30), "NAS script");
        _factory.Time.Advance(TimeSpan.FromMinutes(2));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken);
        var laptopAfterwards = await _factory.GetHttpRequest(DownloadsUrl, laptop.Secret);
        var nasAfterwards = await _factory.GetHttpRequest(DownloadsUrl, nas.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, laptopAfterwards.StatusCode);
        Assert.Equal(SignInRequiredBody, await laptopAfterwards.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, nasAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.All(
                await db.PersonalAccessTokens.AsNoTracking().ToListAsync(cancellationToken),
                token => Assert.Equal(now, token.RevokedUtc)));
    }

    [Fact]
    public async Task RevokeTokens_records_a_revoked_audit_row_per_token_naming_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var laptop = await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload, name: "Laptop");
        var nas = await _tokens.Create(_member, name: "NAS script");

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.AuditEvents.AsNoTracking().OrderBy(audit => audit.TargetName).ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    (AuditAction.PersonalAccessTokenRevoked, (Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)laptop.Token.Id, (string?)"Laptop", now),
                    (AuditAction.PersonalAccessTokenRevoked, (Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)nas.Token.Id, (string?)"NAS script", now),
                ],
                rows.Select(audit => (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc)));
        });
    }

    [Fact]
    public async Task RevokeTokens_touches_no_other_users_tokens_and_keeps_when_a_revoked_one_was_revoked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlier = _factory.Time.GetUtcNow().UtcDateTime;
        var tokens = Tokens();
        var revokedBefore = await tokens.Create(_member.Id, PersonalAccessTokenAccess.ReadOnly, "Old script", expiresUtc: null, revokedUtc: earlier);
        var active = await _tokens.Create(_member, name: "Laptop");
        var ownersOwn = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, name: "Owner's Claude");
        var bob = await _users.Create("bob");
        var bobs = await _tokens.Create(bob, name: "Bob's script");
        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken);
        var ownersStillWorks = await _factory.GetHttpRequest(DownloadsUrl, ownersOwn.Secret);
        var bobsStillWorks = await _factory.GetHttpRequest(DownloadsUrl, bobs.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownersStillWorks.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobsStillWorks.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var revokedUtc = await db.PersonalAccessTokens.AsNoTracking().ToDictionaryAsync(token => token.Id, token => token.RevokedUtc, cancellationToken);
            Assert.Equal(earlier, revokedUtc[revokedBefore.Token.Id]);
            Assert.Equal(now, revokedUtc[active.Token.Id]);
            Assert.Null(revokedUtc[ownersOwn.Token.Id]);
            Assert.Null(revokedUtc[bobs.Token.Id]);
            // One row, for the one token this revoked: none for the one revoked before.
            Assert.Equal(active.Token.Id, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).TargetId);
        });
    }

    // ADR 0040 — every token not revoked yet, an expired one included: Revoke tokens leaves the user's list on their
    // Profile page empty, and an expired token there is one more thing to explain.
    [Fact]
    public async Task RevokeTokens_revokes_an_expired_token_not_revoked_yet_with_its_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var expired = await _tokens.CreateExpired(_member, name: "Ran out");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((AuditAction.PersonalAccessTokenRevoked, (Guid?)expired.Token.Id), (audit.Action, audit.TargetId));
        });
    }

    [Fact]
    public async Task RevokeTokens_of_a_user_without_tokens_is_204_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.CreateRevoked(_member);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task RevokeTokens_twice_at_once_is_204_twice_and_records_one_row_per_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member, name: "Laptop");
        await _tokens.Create(_member, name: "NAS script");

        //Act
        // A double click: each token is claimed once, by whichever request gets to it first.
        var responses = await Task.WhenAll(
            _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken),
            _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken));

        //Assert
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(token => token.RevokedUtc == null, cancellationToken));
            Assert.Equal(
                ["Laptop", "NAS script"],
                await db.AuditEvents.AsNoTracking().OrderBy(audit => audit.TargetName).Select(audit => audit.TargetName).ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task RevokeTokens_of_the_owners_own_tokens_is_204_and_keeps_the_owners_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownersOwn = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_owner.User.Id}/tokens", _owner.AccessToken);
        var tokenAfterwards = await _factory.GetHttpRequest(DownloadsUrl, ownersOwn.Secret);
        var sessionAfterwards = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sessionAfterwards.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == _owner.SessionId, cancellationToken)).RevokedUtc);
            Assert.Equal(_owner.User.Id, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).ActorUserId);
        });
    }

    [Fact]
    public async Task RevokeTokens_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/tokens", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task RevokeTokens_as_a_member_is_403_with_the_owner_sentence_and_revokes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bob = await _users.CreateAuthorized("bob");
        var token = await _tokens.Create(_member);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", bob.AccessToken);
        var stillWorks = await _factory.GetHttpRequest(DownloadsUrl, token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
        await AssertNothingRevokedAsync(cancellationToken);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task RevokeTokens_without_a_token_is_401_and_revokes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingRevokedAsync(cancellationToken);
    }

    [Theory]
    [MemberData(nameof(Accesses))]
    public async Task RevokeTokens_with_the_owners_personal_access_token_is_403_with_the_token_sentence_and_revokes_nothing(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member);
        var ownersToken = await _tokens.Create(_owner.User, access, name: "Owner's Claude");

        //Act
        // The owner's own token: the owner check would let it through, so only the token rule can be refusing.
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", ownersToken.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotForTokensBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingRevokedAsync(cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((AuditAction.PersonalAccessTokenRefused, (Guid?)ownersToken.Token.Id), (audit.Action, audit.TargetId));
        });
    }

    // ── Active token counts ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_shows_each_users_active_tokens_counting_neither_revoked_nor_expired_ones()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        // A second short of expiring: still active.
        await _tokens.Create(_member, expiresUtc: now.AddSeconds(1));
        await _tokens.CreateRevoked(_member);
        await _tokens.CreateExpired(_member);
        var bob = await _users.Create("bob");
        await _tokens.Create(bob);
        await _tokens.CreateExpired(_owner.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            [("bob", 1), ("member", 2), ("owner", 0)],
            body.Items.Select(user => (user.UserName, user.ActiveTokenCount)));
    }

    [Fact]
    public async Task List_counts_the_tokens_of_the_users_on_the_page_asked_for()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member);
        await _tokens.Create(_member);
        await _tokens.Create(_member);
        await _tokens.Create(_owner.User);

        //Act
        // One user a page, the second page: "owner", after "member" by name.
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=1", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(("owner", 1), (Assert.Single(body.Items).UserName, body.Items[0].ActiveTokenCount));
    }

    [Fact]
    public async Task Get_shows_the_users_active_token_count()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member);
        await _tokens.Create(_member, PersonalAccessTokenAccess.ReadAndDownload);
        await _tokens.CreateRevoked(_member);
        await _tokens.CreateExpired(_member);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{_member.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new UserDto(_member.Id, "member", IsOwner: false, IsDisabled: false, _member.CreatedUtc, LastSignInUtc: null, ActiveTokenCount: 2), user);
    }

    [Fact]
    public async Task Get_after_RevokeTokens_shows_no_active_tokens_for_the_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member);
        await _tokens.Create(_member);
        await _factory.DeleteHttpRequest($"{BaseUrl}/{_member.Id}/tokens", _owner.AccessToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{_member.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.ActiveTokenCount);
    }

    [Fact]
    public async Task Get_counts_a_token_as_expired_from_the_instant_it_expires()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _tokens.Create(_member, expiresUtc: now.AddMinutes(1));
        var before = await _factory.GetHttpRequest($"{BaseUrl}/{_member.Id}", _owner.AccessToken);
        var countBefore = (await before.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.ActiveTokenCount;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{_member.Id}", _owner.AccessToken);

        //Assert
        // The same instant the sign-in check stops letting it in.
        Assert.Equal(1, countBefore);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.ActiveTokenCount);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private IPersonalAccessTokenDataFactory Tokens() =>
        new PersonalAccessTokenDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private Task AssertNothingRevokedAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(token => token.RevokedUtc != null, cancellationToken)));
}
