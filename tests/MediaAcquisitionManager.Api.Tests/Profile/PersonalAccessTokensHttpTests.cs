using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-5 — the signed-in user's own personal access tokens behind the Access tokens card of their Profile page (ADR 0040):
// create answers 201 with the secret, the one time it is ever given out, while the row keeps only its SHA-256 and the first
// 14 characters; the lifetimes are exactly 30, 90 and 365 days or never; every field missing or wrong is a 400 with the
// sentence the dialog shows under it, storing nothing; an owner's reset, disable or delete of the user that commits while
// a create is under way wins, and the create is the one 401 with nothing stored. The list is the caller's tokens not
// revoked, newest first, the expired ones flagged, and never a secret or a hash. Revoke is 204 and stops the token with
// its next request; another user's token, one already revoked and one that never was are the same bare 404; a double
// click records once. Every change has its audit row, the user as actor. Who may call (a session, never a token) is
// PersonalAccessTokensAccessHttpTests.
public sealed class PersonalAccessTokensHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/profile/tokens";

    // The plainest [Authorize] GET a token may call: whether a secret signs in.
    private const string DownloadsUrl = "/api/downloads";

    // The owner's Users page, whose reset, disable and delete a create can race.
    private const string UsersUrl = "/api/settings/users";
    private const string NewPassword = "brand-new-password";

    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");

    // Spelled out rather than read from the product code: these are what the Create token dialog shows.
    private const string NameRequired = "Give the token a name, such as “Claude on the laptop”.";
    private const string NameTooLong = "A token's name can be at most 100 characters.";
    private const string AccessRequired = "Choose the token's access: Read only or Read and download.";
    private const string LifetimeRequired = "Choose when the token expires.";
    private const string UnreadableBody =
        "Send the token as JSON: a name, access ReadOnly or ReadAndDownload, and lifetime Days30, Days90, OneYear or Never.";

    // ADR 0040 — the format a script's author and a secret scanner rely on: mam_pat_ and 32 random bytes in base64url.
    private const string SecretPattern = "^mam_pat_[A-Za-z0-9_-]{43}$";

    private const string TokenName = "Claude on the laptop";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private UserEntity _owner = null!;
    private AuthorizedUser _member = null!;

    public PersonalAccessTokensHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await _users.CreateOwner("owner");
        _member = await _users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Create ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_is_201_with_the_token_and_its_secret_and_stores_only_its_hash_and_prefix()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await CreateAsync(TokenName, "ReadAndDownload", "Days90");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The list is where a token lives: there is no read of one token to point at.
        Assert.Equal(BaseUrl, response.Headers.Location?.AbsolutePath);
        var created = (await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Matches(SecretPattern, created.Secret);
        Assert.Equal(
            new PersonalAccessTokenDto(
                created.Token.Id, TokenName, created.Secret[..14], PersonalAccessTokenAccess.ReadAndDownload, now, LastUsedUtc: null, now.AddDays(90), IsExpired: false),
            created.Token);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (created.Token.Id, _member.User.Id, TokenName, created.Secret[..14], PersonalAccessTokenHash.Of(created.Secret), PersonalAccessTokenAccess.ReadAndDownload),
                (row.Id, row.UserId, row.Name, row.Prefix, row.TokenHash, row.Access));
            Assert.Equal(
                ((DateTime?)now.AddDays(90), (DateTime?)null, (DateTime?)null, (DateTime?)null, now),
                (row.ExpiresUtc, row.LastUsedUtc, row.LastRefusedUtc, row.RevokedUtc, row.CreatedUtc));
            // Only a hash of it: a copy of the database signs nobody in.
            Assert.DoesNotContain(created.Secret[14..], row.TokenHash, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Create_records_a_created_audit_row_with_the_user_as_actor_and_never_logs_the_secret()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await CreateAsync(TokenName, "ReadOnly", "Days30");

        //Assert
        var created = (await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenCreated, (Guid?)_member.User.Id, "member", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)created.Token.Id, (string?)TokenName, now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
        // The log is read by more people than the Profile page: not the secret, not even the part a list shows.
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(created.Secret[8..], StringComparison.Ordinal));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(created.Token.Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_answers_a_secret_that_signs_in_as_the_user_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await CreateAsync(TokenName, "ReadOnly", "Days90");
        var created = (await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

        //Act
        var downloads = await _factory.GetHttpRequest(DownloadsUrl, created.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, downloads.StatusCode);
    }

    [Fact]
    public async Task Create_twice_with_one_name_makes_two_tokens_with_different_secrets()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await CreateAsync(TokenName, "ReadOnly", "Days90");
        var second = await CreateAsync(TokenName, "ReadOnly", "Days90");

        //Assert
        // A POST: every call makes a new token, and a name is a label, not a key.
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, second.StatusCode));
        var firstSecret = (await first.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Secret;
        var secondSecret = (await second.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Secret;
        Assert.NotEqual(firstSecret, secondSecret);
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.PersonalAccessTokens.AsNoTracking().Select(token => token.TokenHash).ToListAsync(cancellationToken);
            Assert.Equal(
                new[] { PersonalAccessTokenHash.Of(firstSecret), PersonalAccessTokenHash.Of(secondSecret) }.Order(StringComparer.Ordinal),
                stored.Order(StringComparer.Ordinal));
            Assert.Equal(2, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenCreated, cancellationToken));
        });
    }

    [Theory]
    [InlineData("Days30", 30)]
    [InlineData("Days90", 90)]
    [InlineData("OneYear", 365)]
    [InlineData("Never", null)]
    public async Task Create_sets_the_expiry_the_lifetime_names_from_now(string lifetime, int? days)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        DateTime? expected = days is { } count ? now.AddDays(count) : null;

        //Act
        var response = await CreateAsync(TokenName, "ReadOnly", lifetime);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(expected, created.Token.ExpiresUtc);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(expected, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).ExpiresUtc));
    }

    [Theory]
    [InlineData("ReadOnly", PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("ReadAndDownload", PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Create_stores_the_access_asked_for(string access, PersonalAccessTokenAccess expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await CreateAsync(TokenName, access, "Days90");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(expected, created.Token.Access);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(expected, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).Access));
    }

    [Fact]
    public async Task Create_trims_the_name_and_takes_one_of_100_characters_after_trimming()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var longest = new string('n', 100);

        //Act
        // 102 characters as sent, 100 once trimmed: the limit counts what is kept.
        var response = await CreateAsync($" {longest} ", "ReadOnly", "Days90");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(longest, created.Token.Name);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(longest, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).Name);
            Assert.Equal(longest, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).TargetName);
        });
    }

    [Theory]
    [InlineData("an empty name", NameRequired, "account.token.name-required")]
    [InlineData("a whitespace-only name", NameRequired, "account.token.name-required")]
    [InlineData("a missing name", NameRequired, "account.token.name-required")]
    [InlineData("a name of 101 characters", NameTooLong, "account.token.name-too-long")]
    [InlineData("a missing access", AccessRequired, "account.token.access-required")]
    [InlineData("an access number that names no access", AccessRequired, "account.token.access-required")]
    [InlineData("a missing lifetime", LifetimeRequired, "account.token.lifetime-required")]
    [InlineData("a lifetime number that names no lifetime", LifetimeRequired, "account.token.lifetime-required")]
    [InlineData("an access that isn't one of the names", UnreadableBody, "account.token.unreadable-body")]
    [InlineData("a lifetime that isn't one of the names", UnreadableBody, "account.token.unreadable-body")]
    public async Task Create_with_a_field_missing_or_wrong_is_400_with_its_sentence_and_stores_nothing(
        string wrong, string expectedError, string expectedCode)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        object body = wrong switch
        {
            "an empty name" => new { name = "", access = "ReadOnly", lifetime = "Days90" },
            "a whitespace-only name" => new { name = "   ", access = "ReadOnly", lifetime = "Days90" },
            "a missing name" => new { access = "ReadOnly", lifetime = "Days90" },
            "a name of 101 characters" => new { name = new string('n', 101), access = "ReadOnly", lifetime = "Days90" },
            "a missing access" => new { name = TokenName, lifetime = "Days90" },
            "an access number that names no access" => new { name = TokenName, access = 7, lifetime = "Days90" },
            "a missing lifetime" => new { name = TokenName, access = "ReadOnly" },
            "a lifetime number that names no lifetime" => new { name = TokenName, access = "ReadOnly", lifetime = 0 },
            "an access that isn't one of the names" => new { name = TokenName, access = "Admin", lifetime = "Days90" },
            "a lifetime that isn't one of the names" => new { name = TokenName, access = "ReadOnly", lifetime = "Forever" },
            _ => throw new ArgumentOutOfRangeException(nameof(wrong), wrong, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((expectedError, expectedCode), await response.ReadErrorBodyAsync(cancellationToken));
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_with_no_body_is_400_asking_for_a_name_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameRequired, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_with_a_body_that_is_not_json_is_400_with_what_to_send_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent("{name: Claude", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _member.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UnreadableBody, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_without_a_token_is_401_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new CreatePersonalAccessTokenRequest(TokenName, PersonalAccessTokenAccess.ReadOnly, PersonalAccessTokenLifetime.Days90));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingStoredAsync(cancellationToken);
    }

    // MAM-5 — the create has been let in on its session and is about to store the token when the owner's reset, disable
    // or delete of the user commits (ADR 0040, "What ends a token"). The owner's change wins, as it does over a sign-in
    // (MAM-4): no token outlives the revocation it raced — none an Enable could bring back — and the create gets the
    // very 401 the session check gives the next request, never a 500 for a user deleted meanwhile.
    [Theory]
    [InlineData("reset")]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task Create_racing_the_owners_reset_disable_or_delete_of_the_user_is_401_and_stores_no_token(string ownersChange)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await _users.CreateSession(_owner);
        HttpResponseMessage? change = null;
        // Past the session check, just before the create takes SQLite's write lock: the owner's change commits first.
        _factory.TransactionHook.BeforeNextTransaction(_ => true, async _ => change = await OwnersChangeAsync(ownersChange, owner));

        //Act
        var response = await CreateAsync(TokenName, "ReadAndDownload", "Never");
        var nextRequest = await CreateAsync(TokenName, "ReadAndDownload", "Never");

        //Assert
        Assert.True(change?.IsSuccessStatusCode, $"The owner's {ownersChange} answered {(int?)change?.StatusCode}.");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        // ADR 0002 — the same answer the session check gives once the change is in.
        Assert.Equal(HttpStatusCode.Unauthorized, nextRequest.StatusCode);
        Assert.Equal(nextRequest.Headers.WwwAuthenticate.ToString(), response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(await nextRequest.Content.ReadAsStringAsync(cancellationToken), await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenCreated, cancellationToken));
        });
    }

    // ── List ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_is_200_with_the_callers_tokens_not_revoked_newest_first_the_expired_ones_flagged()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var tokens = Tokens();
        var oldest = await tokens.Create(
            _member.User.Id, PersonalAccessTokenAccess.ReadOnly, "Nightly report", expiresUtc: now.AddDays(-1), createdUtc: now.AddDays(-31));
        var middle = await tokens.Create(
            _member.User.Id, PersonalAccessTokenAccess.ReadAndDownload, "Claude", expiresUtc: null, lastUsedUtc: now.AddMinutes(-5), createdUtc: now.AddDays(-2));
        // Expired at this instant: a token is expired from its ExpiresUtc on, as the sign-in check has it.
        var expiringNow = await tokens.Create(
            _member.User.Id, PersonalAccessTokenAccess.ReadOnly, "Just ran out", expiresUtc: now, createdUtc: now.AddDays(-1));
        var newest = await tokens.Create(_member.User.Id, PersonalAccessTokenAccess.ReadOnly, "Laptop", expiresUtc: now.AddSeconds(1));
        // Revoked: gone from the list, though the row stays for the audit trail.
        await tokens.Create(_member.User.Id, PersonalAccessTokenAccess.ReadOnly, "Revoked", expiresUtc: null, revokedUtc: now.AddHours(-1), createdUtc: now.AddHours(-2));
        // Another user's, the owner's too: never listed.
        var bob = await _users.Create("bob");
        await _tokens.Create(bob, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<PersonalAccessTokenListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            [
                new PersonalAccessTokenDto(newest.Token.Id, "Laptop", newest.Secret[..14], PersonalAccessTokenAccess.ReadOnly, now, null, now.AddSeconds(1), IsExpired: false),
                new PersonalAccessTokenDto(expiringNow.Token.Id, "Just ran out", expiringNow.Secret[..14], PersonalAccessTokenAccess.ReadOnly, now.AddDays(-1), null, now, IsExpired: true),
                new PersonalAccessTokenDto(middle.Token.Id, "Claude", middle.Secret[..14], PersonalAccessTokenAccess.ReadAndDownload, now.AddDays(-2), now.AddMinutes(-5), null, IsExpired: false),
                new PersonalAccessTokenDto(oldest.Token.Id, "Nightly report", oldest.Secret[..14], PersonalAccessTokenAccess.ReadOnly, now.AddDays(-31), null, now.AddDays(-1), IsExpired: true),
            ],
            list.Items);
        // A read writes nothing.
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task List_never_carries_a_secret_or_a_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await CreateAsync(TokenName, "ReadAndDownload", "Never");
        var secret = (await created.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Secret;
        var planted = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // Read as text: a typed read would drop a property the contract doesn't name.
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var whole in new[] { secret, planted.Secret })
        {
            Assert.DoesNotContain(whole[14..], text, StringComparison.Ordinal);
            Assert.DoesNotContain(PersonalAccessTokenHash.Of(whole), text, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(text);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(
            ["access", "createdUtc", "expiresUtc", "id", "isExpired", "lastUsedUtc", "name", "prefix"],
            item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        Assert.Equal(["items"], document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task List_without_tokens_is_200_and_empty()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"items":[]}""", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task List_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── Revoke ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_is_204_and_the_token_stops_working_with_its_next_request()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var other = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly, name: "Other script");
        var workedBefore = await _factory.GetHttpRequest(DownloadsUrl, token.Secret);
        _factory.Time.Advance(TimeSpan.FromMinutes(3));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{token.Token.Id}", _member.AccessToken);
        var afterwards = await _factory.GetHttpRequest(DownloadsUrl, token.Secret);
        var otherAfterwards = await _factory.GetHttpRequest(DownloadsUrl, other.Secret);
        var list = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, workedBefore.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
        Assert.Equal(SignInRequiredBody, await afterwards.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, otherAfterwards.StatusCode);
        var listed = (await list.Content.ReadFromJsonAsync<PersonalAccessTokenListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal([other.Token.Id], listed.Items.Select(item => item.Id));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(row => row.Id == token.Token.Id, cancellationToken)).RevokedUtc);
            Assert.Null((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(row => row.Id == other.Token.Id, cancellationToken)).RevokedUtc);
        });
    }

    [Fact]
    public async Task Revoke_records_a_revoked_audit_row_with_the_user_as_actor()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_member.User, name: TokenName);

        //Act
        await _factory.DeleteHttpRequest($"{BaseUrl}/{token.Token.Id}", _member.AccessToken);

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.PersonalAccessTokenRevoked, (Guid?)_member.User.Id, "member", (AuditTargetKind?)AuditTargetKind.PersonalAccessToken, (Guid?)token.Token.Id, (string?)TokenName, now),
                (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        });
    }

    [Fact]
    public async Task Revoke_of_an_expired_token_is_204_and_takes_it_off_the_list()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var expired = await _tokens.CreateExpired(_member.User);

        //Act
        // The list shows an expired token with Revoke beside it until it is revoked.
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{expired.Token.Id}", _member.AccessToken);
        var list = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await list.Content.ReadFromJsonAsync<PersonalAccessTokenListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(now, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            Assert.Equal(AuditAction.PersonalAccessTokenRevoked, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task Revoke_of_another_users_token_is_a_bare_404_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bob = await _users.Create("bob");
        var bobs = await _tokens.Create(bob, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{bobs.Token.Id}", _member.AccessToken);
        var bobsStillWorks = await _factory.GetHttpRequest(DownloadsUrl, bobs.Secret);

        //Assert
        // The same answer as a token that never was: whose ids exist is nobody else's business.
        await AssertBare404Async(response, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, bobsStillWorks.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Revoke_of_a_token_already_revoked_is_a_bare_404_and_keeps_when_it_was_revoked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var revoked = await _tokens.CreateRevoked(_member.User);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{revoked.Token.Id}", _member.AccessToken);

        //Assert
        await AssertBare404Async(response, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(revoked.Token.RevokedUtc, (await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Revoke_of_a_token_that_never_was_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _member.AccessToken);

        //Assert
        await AssertBare404Async(response, cancellationToken);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Revoke_twice_at_once_revokes_once_and_records_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);

        //Act
        // A double click: both may find the token not revoked, but only one may claim it.
        var responses = await Task.WhenAll(
            _factory.DeleteHttpRequest($"{BaseUrl}/{token.Token.Id}", _member.AccessToken),
            _factory.DeleteHttpRequest($"{BaseUrl}/{token.Token.Id}", _member.AccessToken));

        //Assert
        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.NotFound],
            responses.Select(response => response.StatusCode).Order().ToArray());
        await _factory.WithDbAsync(async db =>
        {
            Assert.NotNull((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            Assert.Equal(1, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRevoked, cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Revoke_without_a_token_is_401_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{token.Token.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken)).RevokedUtc);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // As the Create token dialog sends it: names for the access and the lifetime, the way the enums serialize.
    private Task<HttpResponseMessage> CreateAsync(string name, string access, string lifetime) =>
        _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new { name, access, lifetime });

    // What the owner does on the Users page to the member: reset their password, disable them, or delete them.
    private Task<HttpResponseMessage> OwnersChangeAsync(string change, AuthorizedUser owner) => change switch
    {
        "reset" => _factory.PostHttpRequest($"{UsersUrl}/{_member.User.Id}/password", owner.AccessToken, new { password = NewPassword }),
        "disable" => _factory.PostHttpRequest($"{UsersUrl}/{_member.User.Id}/disable", owner.AccessToken),
        "delete" => _factory.DeleteHttpRequest($"{UsersUrl}/{_member.User.Id}", owner.AccessToken),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Not a change the owner makes on the Users page."),
    };

    private IPersonalAccessTokenDataFactory Tokens() =>
        new PersonalAccessTokenDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    // What a refused create leaves: no token and no audit row.
    private Task AssertNothingStoredAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });

    /// <summary>A 404 with nothing in it: the same answer a token that never was gets.</summary>
    private static async Task AssertBare404Async(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
