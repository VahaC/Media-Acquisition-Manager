using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Auth;

// MAM-5 — a personal access token signs a request in as its user (ADR 0040): it reads what its user reads, on any plain
// [Authorize] GET, whatever its access; every way of being refused — wrong, unknown, malformed, revoked, expired, its
// user disabled or deleted — is the one 401 an expired session gets, byte for byte; a session's JWT is untouched, and a
// bearer that isn't a token still goes to the session check; "last used" is written at most once a minute, and a token
// used within it reads past another connection's write; failed tokens use up their address's sign-in attempts — sent
// one after the other or all at once — and then every token from it, a valid one too, is 429, while a token let in
// costs nothing; anonymous endpoints and unknown /api paths answer as they do without one.
public sealed class PersonalAccessTokenAuthenticationHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // The plainest [Authorize] GET a token may call: the caller's own download jobs.
    private const string BaseUrl = "/api/downloads";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string HealthUrl = "/api/health";
    private const string UnknownApiUrl = "/api/nothing-lives-here";

    // Spelled out rather than read from the product code: what every refused credential and every limited address gets.
    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");
    private const string TooManyAttempts = "Too many attempts. Wait a minute and try again.";

    // A bearer the session check refuses for not being a JWT at all: the reference the token's 401 is compared with.
    private const string NotAJwt = "not-a-jwt";

    // Far longer than a read takes, far shorter than the 30 s a request waiting for SQLite's write lock would give up
    // after.
    private static readonly TimeSpan WriteLockWait = TimeSpan.FromSeconds(10);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public PersonalAccessTokenAuthenticationHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(PersonalAccessTokenAccess.ReadOnly)]
    [InlineData(PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Get_with_a_token_of_either_access_is_200(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, access);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(0, list!.TotalCount);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Get_with_a_members_token_reads_only_that_members_jobs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var membersJob = await PlantJobAsync(_member.User);
        await PlantJobAsync(_owner.User);
        var other = await _users.Create("other");
        await PlantJobAsync(other);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, token.Secret);
        var job = await _factory.GetHttpRequest($"{BaseUrl}/{membersJob.Id}", token.Secret);

        //Assert
        // The token is its user, nothing more: the owner's and another member's jobs stay theirs (ADR 0011).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal((1, membersJob.Id), (list!.TotalCount, Assert.Single(list.Items).Id));
        Assert.Equal(HttpStatusCode.OK, job.StatusCode);
    }

    [Fact]
    public async Task Get_of_another_users_job_with_a_token_is_a_bare_404()
    {
        //Arrange
        var ownersJob = await PlantJobAsync(_owner.User);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{ownersJob.Id}", token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ADR 0040 — one answer for every refusal, so a caller probing tokens learns nothing about which check failed. Each
    // case refuses for one reason only: the user of "a revoked token" exists and is enabled, the deleted user's token
    // was otherwise active, and so on.
    [Theory]
    [InlineData("an unknown token")]
    [InlineData("a wrong token with a real token's prefix")]
    [InlineData("a mam_pat_ value that is too short")]
    [InlineData("a mam_pat_ value that is too long")]
    [InlineData("a mam_pat_ value with a character base64url doesn't use")]
    [InlineData("a revoked token")]
    [InlineData("a token at the instant it expires")]
    [InlineData("a token of a disabled user")]
    [InlineData("a token of a deleted user")]
    public async Task Get_with_a_refused_token_is_the_same_401_a_refused_session_gets(string refusal)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.Create("alice");
        string presented;
        switch (refusal)
        {
            case "an unknown token":
                presented = NewSecret();
                break;
            case "a wrong token with a real token's prefix":
                // The same first characters, so a lookup by the shown prefix would find the row: only the hash of the
                // whole value may decide.
                var real = (await _tokens.Create(alice)).Secret;
                presented = real[..^1] + (real[^1] == 'A' ? 'B' : 'A');
                break;
            case "a mam_pat_ value that is too short":
                presented = "mam_pat_abc";
                break;
            case "a mam_pat_ value that is too long":
                presented = (await _tokens.Create(alice)).Secret + "A";
                break;
            case "a mam_pat_ value with a character base64url doesn't use":
                presented = (await _tokens.Create(alice)).Secret[..^1] + "+";
                break;
            case "a revoked token":
                presented = (await _tokens.CreateRevoked(alice)).Secret;
                break;
            case "a token at the instant it expires":
                // Active while ExpiresUtc is later than now: at the instant itself it is expired.
                var now = _factory.Time.GetUtcNow().UtcDateTime;
                presented = (await _tokens.Create(alice, expiresUtc: now.AddDays(30))).Secret;
                _factory.Time.Advance(TimeSpan.FromDays(30));
                break;
            case "a token of a disabled user":
                var dora = await _users.Create("dora", isDisabled: true);
                presented = (await _tokens.Create(dora)).Secret;
                break;
            case "a token of a deleted user":
                var bob = await _users.Create("bob");
                presented = (await _tokens.Create(bob)).Secret;
                await _factory.WithDbAsync(db => db.Users.Where(user => user.Id == bob.Id).ExecuteDeleteAsync(cancellationToken));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null);
        }

        var sessionRefused = await _factory.GetHttpRequest(BaseUrl, NotAJwt);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, presented);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, sessionRefused.StatusCode);
        Assert.Equal(SignInRequiredBody, await sessionRefused.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(sessionRefused.Headers.WwwAuthenticate.ToString(), response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(sessionRefused.Content.Headers.ContentType?.ToString(), response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            await sessionRefused.Content.ReadAsByteArrayAsync(cancellationToken),
            await response.Content.ReadAsByteArrayAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // A refused token is never marked used, and a 401 writes no audit row.
            Assert.False(await db.PersonalAccessTokens.AnyAsync(token => token.LastUsedUtc != null, cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // MAM-47 (ADR 0042) — the 401's words follow the request's own Accept-Language, never the account behind the
    // credential, so asked in Ukrainian an expired session, an unknown token, a revoked token and a disabled user's token
    // still answer the same bytes, with the one code. The accounts saved English, which an answer read from
    // Users.Language would show instead of the Ukrainian asked for.
    [Fact]
    public async Task Get_with_any_refused_credential_asked_in_ukrainian_is_the_identical_401_with_the_sign_in_code()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.CreateAuthorized("alice");
        var dora = await _users.Create("dora", isDisabled: true);
        await _users.SetLanguage(alice.User, "en");
        await _users.SetLanguage(dora, "en");
        var revoked = (await _tokens.CreateRevoked(alice.User)).Secret;
        var disabledUsers = (await _tokens.Create(dora)).Secret;
        // Past the access token's 15 minutes and the clock skew: alice's session is live, her access token isn't.
        _factory.Time.Advance(TimeSpan.FromMinutes(16));

        //Act
        var expiredSession = await _factory.GetHttpRequest(BaseUrl, alice.AccessToken, language: "uk");
        var unknownToken = await _factory.GetHttpRequest(BaseUrl, NewSecret(), language: "uk");
        var revokedToken = await _factory.GetHttpRequest(BaseUrl, revoked, language: "uk");
        var disabledUsersToken = await _factory.GetHttpRequest(BaseUrl, disabledUsers, language: "uk");

        //Assert
        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized],
            [expiredSession.StatusCode, unknownToken.StatusCode, revokedToken.StatusCode, disabledUsersToken.StatusCode]);
        var expiredSessionBody = await expiredSession.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(expiredSessionBody, await unknownToken.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal(expiredSessionBody, await revokedToken.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal(expiredSessionBody, await disabledUsersToken.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal("account.sign-in-required", await expiredSession.ReadErrorCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Get_with_a_token_a_second_before_it_expires_is_200()
    {
        //Arrange
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_member.User, expiresUtc: now.AddDays(30));
        _factory.Time.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, token.Secret);

        //Assert
        // Guards the guard: the expiry above is read from the test clock, and a second short of it the token still works.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_a_token_and_the_bearer_word_in_lower_case_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_member.User);
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.TryAddWithoutValidation("Authorization", "bearer " + token.Secret);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        // The scheme word in any letter case, as the session check reads it: the two never disagree about whose header it is.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(now, (await db.PersonalAccessTokens.SingleAsync(row => row.Id == token.Token.Id, cancellationToken)).LastUsedUtc));
    }

    [Fact]
    public async Task Get_with_a_sessions_access_token_is_200_as_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _tokens.Create(_member.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.False(await db.PersonalAccessTokens.AnyAsync(token => token.LastUsedUtc != null, cancellationToken)));
    }

    // ADR 0040 — only a mam_pat_ bearer goes to the token check: anything else is the session check's, whose failures
    // count against nobody's sign-in attempts. Were one of these taken for a token, the sign-in after them would be 429.
    [Fact]
    public async Task Bearers_that_are_not_tokens_go_to_the_session_check_and_use_no_sign_in_attempt()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");
        var secret = NewSecret();
        string[] bearers =
        [
            NotAJwt,
            // The prefix is matched exactly: in capitals, or with a dash, it is no token's.
            "MAM_PAT_" + secret["mam_pat_".Length..],
            "mam-pat_" + secret["mam_pat_".Length..],
            "mam_pat" + secret["mam_pat_".Length..],
        ];
        _factory.PinClientAddress();
        var refused = new List<(HttpStatusCode Status, string Body)>();
        for (var attempt = 0; attempt < SignInRateLimitPolicy.PermitLimit + 2; attempt++)
        {
            var response = await _factory.GetHttpRequest(BaseUrl, bearers[attempt % bearers.Length]);
            refused.Add((response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        //Act
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        Assert.All(refused, answer => Assert.Equal((HttpStatusCode.Unauthorized, SignInRequiredBody), answer));
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    // ADR 0040 — written on the first use, then at most once a minute: a script calling every second would otherwise
    // write the row on every request.
    [Fact]
    public async Task Gets_with_a_token_write_last_used_on_the_first_use_then_at_most_once_a_minute()
    {
        //Arrange
        var firstUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_member.User);

        //Act
        var first = await _factory.GetHttpRequest(BaseUrl, token.Secret);
        var afterFirst = await LastUsedAsync(token);
        _factory.Time.Advance(TimeSpan.FromSeconds(59));
        var second = await _factory.GetHttpRequest(BaseUrl, token.Secret);
        var afterSecond = await LastUsedAsync(token);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var thirdUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var third = await _factory.GetHttpRequest(BaseUrl, token.Secret);
        var afterThird = await LastUsedAsync(token);

        //Assert
        Assert.All(new[] { first, second, third }, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(firstUtc, afterFirst);
        Assert.Equal(firstUtc, afterSecond);
        Assert.Equal(thirdUtc, afterThird);
    }

    // ADR 0040 — whether "last used" is due is asked of the row the lookup read, before any update is sent: even an
    // update that matches nothing takes SQLite's write lock. So a token used within the minute reads past another
    // connection's write, as a session's request does (WAL), instead of waiting for that writer.
    [Fact]
    public async Task Get_with_a_token_used_within_the_minute_answers_while_another_connection_holds_the_write_lock()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var token = await _tokens.Create(_member.User);
        var firstUse = await _factory.GetHttpRequest(BaseUrl, token.Secret);
        _factory.Time.Advance(TimeSpan.FromSeconds(30));

        //Act
        HttpResponseMessage? whileLocked = null;
        await _factory.WithDbAsync(async db =>
        {
            // BEGIN IMMEDIATE: the write lock, held until the request has answered.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            whileLocked = await _factory.GetHttpRequest(BaseUrl, token.Secret).WaitAsync(WriteLockWait, cancellationToken);
        });

        //Assert
        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, whileLocked?.StatusCode);
        Assert.Equal(firstUtc, await LastUsedAsync(token));
    }

    // ADR 0040 — every failed token, whatever failed, takes one of its address's ten sign-in attempts a minute; with
    // them used up, a token from that address isn't even looked up — a valid one included, or guessing would go on — and
    // a password sign-in from it is refused as well. Another address is untouched.
    [Fact]
    public async Task Failed_tokens_use_up_the_sign_in_attempts_of_their_address_then_every_token_from_it_is_429()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.Create("alice");
        var valid = await _tokens.Create(alice, PersonalAccessTokenAccess.ReadAndDownload);
        var revoked = (await _tokens.CreateRevoked(alice)).Secret;
        var expired = (await _tokens.CreateExpired(alice)).Secret;
        string[] failures = [NewSecret(), "mam_pat_abc", revoked, expired, valid.Secret + "A"];
        _factory.PinClientAddress();
        var failed = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < SignInRateLimitPolicy.PermitLimit; attempt++)
            failed.Add((await _factory.GetHttpRequest(BaseUrl, failures[attempt % failures.Length])).StatusCode);

        //Act
        var validToken = await _factory.GetHttpRequest(BaseUrl, valid.Secret);
        var unknownToken = await _factory.GetHttpRequest(BaseUrl, NewSecret());
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));
        var lastUsedWhileLimited = await LastUsedAsync(valid);
        _factory.PinClientAddress();
        var fromAnotherAddress = await _factory.GetHttpRequest(BaseUrl, valid.Secret);

        //Assert
        Assert.All(failed, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, validToken.StatusCode);
        Assert.Equal("application/json", validToken.Content.Headers.ContentType?.MediaType);
        Assert.Equal((TooManyAttempts, "account.too-many-attempts"), await validToken.ReadErrorBodyAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), validToken.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.TooManyRequests, unknownToken.StatusCode);
        Assert.Equal(TooManyAttempts, await unknownToken.ReadErrorAsync(cancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(60), unknownToken.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.TooManyRequests, signIn.StatusCode);
        Assert.Equal(TooManyAttempts, await signIn.ReadErrorAsync(cancellationToken));
        // Not looked up while limited: its row was never touched.
        Assert.Null(lastUsedWhileLimited);
        Assert.Equal(HttpStatusCode.OK, fromAnotherAddress.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.UserSessions.CountAsync(session => session.UserId == alice.Id, cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // ADR 0040 — guesses sent all at once get no more than guesses sent one after the other: a lookup under way counts
    // against the address's attempts until it ends, so no more than ten are looked up, the rest are 429 without a lookup,
    // and the ten failures leave the address used up for a valid token too.
    [Fact]
    public async Task Failed_tokens_sent_at_once_get_no_more_lookups_than_their_address_has_sign_in_attempts()
    {
        //Arrange
        var valid = await _tokens.Create(_member.User);
        var guesses = Enumerable.Range(0, 3 * SignInRateLimitPolicy.PermitLimit).Select(_ => NewSecret()).ToList();
        _factory.PinClientAddress();

        //Act
        var answers = await Task.WhenAll(guesses.Select(guess => _factory.GetHttpRequest(BaseUrl, guess)));
        var validToken = await _factory.GetHttpRequest(BaseUrl, valid.Secret);

        //Assert
        var statuses = answers.Select(answer => answer.StatusCode).ToList();
        Assert.All(statuses, status => Assert.Contains(status, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
        // Looked up, and refused as unknown: never more than the address's attempts, however many arrived together.
        Assert.InRange(statuses.Count(status => status == HttpStatusCode.Unauthorized), 1, SignInRateLimitPolicy.PermitLimit);
        Assert.Equal(HttpStatusCode.TooManyRequests, validToken.StatusCode);
        Assert.Null(await LastUsedAsync(valid));
    }

    [Fact]
    public async Task Failed_tokens_and_failed_sign_ins_from_one_address_share_its_ten_attempts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.Create("alice");
        var valid = await _tokens.Create(alice);
        _factory.PinClientAddress();
        var failed = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < SignInRateLimitPolicy.PermitLimit / 2; attempt++)
            failed.Add((await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", "not-the-password"))).StatusCode);
        for (var attempt = SignInRateLimitPolicy.PermitLimit / 2; attempt < SignInRateLimitPolicy.PermitLimit; attempt++)
            failed.Add((await _factory.GetHttpRequest(BaseUrl, NewSecret())).StatusCode);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, valid.Secret);

        //Assert
        // One bucket per address: a run guessing passwords and tokens together gets the ten attempts either alone would.
        Assert.All(failed, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TooManyAttempts, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Gets_with_a_valid_token_use_none_of_the_sign_in_attempts_of_their_address()
    {
        //Arrange
        var token = await _tokens.Create(_member.User);
        await _users.Create("alice");
        _factory.PinClientAddress();

        //Act
        var answers = new List<HttpStatusCode>();
        for (var request = 0; request < SignInRateLimitPolicy.PermitLimit + 2; request++)
            answers.Add((await _factory.GetHttpRequest(BaseUrl, token.Secret)).StatusCode);
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Assert
        // Had each request let in taken an attempt, the eleventh would already be 429, and the sign-in too.
        Assert.All(answers, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    // An anonymous endpoint stays anonymous with a token on it, valid or not: the token is never what decides it.
    [Fact]
    public async Task Health_with_a_token_valid_or_not_answers_as_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        var anonymous = await _factory.GetHttpRequest(HealthUrl);

        //Act
        var withValid = await _factory.GetHttpRequest(HealthUrl, token.Secret);
        var withUnknown = await _factory.GetHttpRequest(HealthUrl, NewSecret());

        //Assert
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withValid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withUnknown.StatusCode);
        Assert.Equal(await anonymous.Content.ReadAsStringAsync(cancellationToken), await withValid.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(await anonymous.Content.ReadAsStringAsync(cancellationToken), await withUnknown.Content.ReadAsStringAsync(cancellationToken));
    }

    // MAM-4's 404 for a path nothing claims, to everyone: a token, valid or not, and a write method included — a token's
    // refusal would read as an endpoint that exists.
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Unknown_api_path_with_a_token_valid_or_not_is_the_same_404_as_without_one(string method)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var anonymous = await SendAsync(method, UnknownApiUrl, token: null);

        //Act
        var withValid = await SendAsync(method, UnknownApiUrl, token.Secret);
        var withUnknown = await SendAsync(method, UnknownApiUrl, NewSecret());

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
        Assert.Equal($"No API endpoint matches {method} {UnknownApiUrl}.", await anonymous.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, withValid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withUnknown.StatusCode);
        Assert.Equal(await anonymous.Content.ReadAsStringAsync(cancellationToken), await withValid.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(await anonymous.Content.ReadAsStringAsync(cancellationToken), await withUnknown.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    // A well-formed token nobody holds: the format spelled out, as PersonalAccessTokenDataFactory spells it.
    private static string NewSecret() => "mam_pat_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private async Task<DateTime?> LastUsedAsync(IssuedPersonalAccessToken token)
    {
        DateTime? lastUsedUtc = null;
        await _factory.WithDbAsync(async db =>
            lastUsedUtc = (await db.PersonalAccessTokens.AsNoTracking()
                .SingleAsync(row => row.Id == token.Token.Id, TestContext.Current.CancellationToken)).LastUsedUtc);
        return lastUsedUtc;
    }

    // A job sent by user, from a release of a fake source to a fake client: enough for the Jobs list to show it as theirs.
    private async Task<DownloadJobEntity> PlantJobAsync(UserEntity user)
    {
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var source = await integrations.Create(name: $"Source of {user.UserName}");
        var client = await integrations.Create(name: $"Client of {user.UserName}");
        var release = await new ReleaseSeeder(_factory.Scope).Create(source);
        return await new DownloadJobSeeder(_factory.Scope).Create(client, release.Id, requestedBy: user);
    }

    private Task<HttpResponseMessage> SendAsync(string method, string url, string? token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _factory.SendHttpRequest(request);
    }
}
