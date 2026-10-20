using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Auth;

// MAM-47 WP14 (ADR 0042, design §8.6) — the edge in Ukrainian, as a person reads it: a request that asks for uk gets the
// refusals of sign-in, of a credential and of a token in Ukrainian with the code that names them in every language,
// while the anti-probing rule holds — every refused sign-in, and every refused credential, is the same bytes in uk too.
// The same request without the header answers English. Each answer spelled here, never read from the locale files.
public sealed class AuthUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string SignInUrl = "/api/auth/sign-in";
    private const string DownloadsUrl = "/api/downloads";
    private const string LanguageUrl = "/api/profile/language";

    private const string InvalidCredentialsBody =
        """{"error":"Неправильне ім’я користувача або пароль.","code":"account.sign-in.invalid-credentials"}""";

    private const string SignInRequiredBody = """{"error":"Увійдіть, щоб продовжити.","code":"account.sign-in-required"}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;

    public AuthUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SignIn_failures_asked_in_ukrainian_are_one_ukrainian_body_for_every_cause()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");
        await _users.Create("bob", isDisabled: true);

        //Act
        var unknownUser = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("nobody", UserSeeder.DefaultPassword), language: "uk");
        var wrongPassword = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", "not-the-password"), language: "uk");
        var disabledUser = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("bob", UserSeeder.DefaultPassword), language: "uk");

        //Assert
        Assert.Equal(
            [
                (HttpStatusCode.Unauthorized, InvalidCredentialsBody),
                (HttpStatusCode.Unauthorized, InvalidCredentialsBody),
                (HttpStatusCode.Unauthorized, InvalidCredentialsBody),
            ],
            [
                (unknownUser.StatusCode, await unknownUser.Content.ReadAsStringAsync(cancellationToken)),
                (wrongPassword.StatusCode, await wrongPassword.Content.ReadAsStringAsync(cancellationToken)),
                (disabledUser.StatusCode, await disabledUser.Content.ReadAsStringAsync(cancellationToken)),
            ]);
    }

    [Fact]
    public async Task Request_with_a_refused_credential_asked_in_ukrainian_is_one_ukrainian_body_for_every_cause()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.CreateAuthorized("alice");
        var dora = await _users.Create("dora", isDisabled: true);
        var revoked = (await _tokens.CreateRevoked(alice.User)).Secret;
        var expired = (await _tokens.CreateExpired(alice.User)).Secret;
        var disabledUsers = (await _tokens.Create(dora)).Secret;
        // Past the access token's 15 minutes and the clock skew: alice's session is live, her access token isn't.
        _factory.Time.Advance(TimeSpan.FromMinutes(16));

        //Act
        // MAM-47 WP14 review — no credential and a bearer that is no JWT reach the same writer by other roads (the
        // challenge, the session check), so they are causes a prober could tell apart too.
        var anonymous = await _factory.GetHttpRequest(DownloadsUrl, language: "uk");
        var notAJwt = await _factory.GetHttpRequest(DownloadsUrl, "not-a-jwt", language: "uk");
        var expiredSession = await _factory.GetHttpRequest(DownloadsUrl, alice.AccessToken, language: "uk");
        var unknownToken = await _factory.GetHttpRequest(DownloadsUrl, NewSecret(), language: "uk");
        var revokedToken = await _factory.GetHttpRequest(DownloadsUrl, revoked, language: "uk");
        var expiredToken = await _factory.GetHttpRequest(DownloadsUrl, expired, language: "uk");
        var disabledUsersToken = await _factory.GetHttpRequest(DownloadsUrl, disabledUsers, language: "uk");

        //Assert
        Assert.Equal(
            [
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
                (HttpStatusCode.Unauthorized, SignInRequiredBody),
            ],
            [
                (anonymous.StatusCode, await anonymous.Content.ReadAsStringAsync(cancellationToken)),
                (notAJwt.StatusCode, await notAJwt.Content.ReadAsStringAsync(cancellationToken)),
                (expiredSession.StatusCode, await expiredSession.Content.ReadAsStringAsync(cancellationToken)),
                (unknownToken.StatusCode, await unknownToken.Content.ReadAsStringAsync(cancellationToken)),
                (revokedToken.StatusCode, await revokedToken.Content.ReadAsStringAsync(cancellationToken)),
                (expiredToken.StatusCode, await expiredToken.Content.ReadAsStringAsync(cancellationToken)),
                (disabledUsersToken.StatusCode, await disabledUsersToken.Content.ReadAsStringAsync(cancellationToken)),
            ]);
    }

    [Fact]
    public async Task SignIn_failure_without_the_header_answers_english_with_the_same_code()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");

        //Act
        var response = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", "not-the-password"));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json("The user name or password is incorrect.", "account.sign-in.invalid-credentials"),
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Token_refusal_is_english_without_the_header_and_ukrainian_with_it()
    {
        //Arrange
        // A script with a token that may not do this: its answer follows the header it sends, and English without one.
        var cancellationToken = TestContext.Current.CancellationToken;
        var anna = await _users.CreateAuthorized("anna");
        var token = await _tokens.Create(anna.User, PersonalAccessTokenAccess.ReadAndDownload);

        //Act
        var english = await _factory.PutHttpRequest(LanguageUrl, token.Secret, new UserLanguageRequest("uk"));
        var ukrainian = await _factory.PutHttpRequest(LanguageUrl, token.Secret, new UserLanguageRequest("uk"), language: "uk");

        //Assert
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (english.StatusCode, ukrainian.StatusCode));
        Assert.Equal(
            ("An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.", "account.token.not-for-tokens"),
            await english.ReadErrorBodyAsync(cancellationToken));
        Assert.Equal(
            ("Токен доступу не може цього зробити. Токен читає дані, а з доступом «Читання й завантаження» також надсилає завантаження, припиняє відстеження й позначає «Не цікавить»; усе інше робиться у вебзастосунку. Див. розділ «Scripts and the API» у README.", "account.token.not-for-tokens"),
            await ukrainian.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task Read_only_token_sending_a_download_asked_in_ukrainian_is_403_naming_the_access_it_needs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var anna = await _users.CreateAuthorized("anna");
        var token = await _tokens.Create(anna.User, PersonalAccessTokenAccess.ReadOnly);

        //Act
        var response = await _factory.PostHttpRequest(DownloadsUrl, token.Secret, new { releaseId = Guid.NewGuid() }, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            ("Цей токен доступу лише для читання. Щоб надсилати завантаження, припиняти відстеження або позначати «Не цікавить» зі скрипта, створіть на сторінці «Профіль» токен із доступом «Читання й завантаження»; див. розділ «Scripts and the API» у README.", "account.token.read-only"),
            await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task Unknown_endpoint_asked_in_ukrainian_names_the_method_and_path_it_was_asked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest("/api/nothing-here", language: "uk-UA");

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            """{"error":"Жодна кінцева точка API не відповідає запиту POST /api/nothing-here.","code":"common.request.unknown-endpoint"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static string NewSecret() => "mam_pat_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
