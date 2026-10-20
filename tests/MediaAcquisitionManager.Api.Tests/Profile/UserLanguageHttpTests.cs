using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-46 — the signed-in user's own language for the web app (PUT /api/profile/language): en, uk, or null for the
// browser's; the property required, so a body without it never switches anyone back to the browser's language; any other
// value, and a body that isn't the language, answering { error } with the sentence, never a validation body; the
// caller's own row only, the owner's and a member's alike, no audit row, and no personal access token.
public sealed class UserLanguageHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/profile/language";
    private const string MeUrl = "/api/auth/me";
    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");
    private const string UnreadableBodyError = "Send the language as JSON: language en, uk, or null to follow the browser.";

    // Spelled out rather than read from PersonalAccessTokenErrors: this is what a script's author reads.
    private static readonly string NotForTokensBody = ErrorBody.Json(
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.",
        "account.token.not-for-tokens");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _anna = null!;

    public UserLanguageHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<PersonalAccessTokenAccess> Accesses => new()
    {
        PersonalAccessTokenAccess.ReadOnly,
        PersonalAccessTokenAccess.ReadAndDownload,
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _anna = await _users.CreateAuthorized("anna");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Update_saves_the_callers_language_and_answers_it_without_an_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new UserLanguageRequest("uk"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"language":"uk"}""", await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("uk", await LanguageOfAsync(db, _anna.User.Id, cancellationToken));
            // Nobody else's.
            Assert.Null(await LanguageOfAsync(db, _owner.User.Id, cancellationToken));
            // A per-user preference: the README's Audit trail lists what writes one, and this doesn't.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_by_the_owner_and_by_a_member_saves_each_their_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var owners = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UserLanguageRequest("en"));
        var annas = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new UserLanguageRequest("uk"));

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, annas.StatusCode));
        Assert.Equal("""{"language":"en"}""", await owners.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("""{"language":"uk"}""", await annas.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("en", await LanguageOfAsync(db, _owner.User.Id, cancellationToken));
            Assert.Equal("uk", await LanguageOfAsync(db, _anna.User.Id, cancellationToken));
        });
    }

    [Fact]
    public async Task Update_with_null_goes_back_to_the_browsers_language()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.SetLanguage(_anna.User, "uk");

        //Act
        var response = await PutJsonTextAsync("""{"language":null}""");

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"language":null}""", await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Null(await LanguageOfAsync(db, _anna.User.Id, cancellationToken)));
    }

    [Fact]
    public async Task Update_is_what_the_profile_and_the_next_sign_in_then_carry()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new UserLanguageRequest("uk"));
        var me = await _factory.GetHttpRequest(MeUrl, _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("uk", document.RootElement.GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("fr", "fr")]
    // Exactly as the web app sends it: a region or another letter case is no locale it has.
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("UK", "UK")]
    [InlineData(" uk", " uk")]
    [InlineData("", "")]
    [InlineData("a-language-code-longer-than-anything-real", "a-language-code-longer-than-anyt…")]
    public async Task Update_with_a_value_that_is_no_language_of_the_app_is_400_with_the_sentence_and_saves_nothing(string language, string echoed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.SetLanguage(_anna.User, "en");

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _anna.AccessToken, new UserLanguageRequest(language));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ($"“{echoed}” is not a language of the app. Use en, uk, or null to follow the browser.", "account.language.unsupported"),
            await response.ReadErrorBodyAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal("en", await LanguageOfAsync(db, _anna.User.Id, cancellationToken)));
    }

    [Theory]
    // Without the property: null is a choice, so leaving it out can't mean it.
    [InlineData("{}")]
    [InlineData("""{"lang":"uk"}""")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("\"uk\"")]
    [InlineData("""{"language":1}""")]
    [InlineData("""{"language":["uk"]}""")]
    public async Task Update_with_a_body_that_isnt_the_language_is_400_with_the_sentence_never_a_validation_body(string json)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.SetLanguage(_anna.User, "uk");

        //Act
        var response = await PutJsonTextAsync(json);

        //Assert
        // The { error } shape the Profile page shows: never [ApiController]'s problem details.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(UnreadableBodyError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal("uk", await LanguageOfAsync(db, _anna.User.Id, cancellationToken)));
    }

    [Theory]
    [MemberData(nameof(Accesses))]
    public async Task Update_with_a_personal_access_token_is_403_with_the_sentence_and_saves_nothing(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_anna.User, access);

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, token.Secret, new UserLanguageRequest("uk"));

        //Assert
        // A write no list opens to tokens (ADR 0040): the language is the person's, set in the web app.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotForTokensBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Null(await LanguageOfAsync(db, _anna.User.Id, cancellationToken)));
    }

    [Fact]
    public async Task Update_without_a_token_is_401_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, body: new UserLanguageRequest("uk"));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.Users.CountAsync(user => user.Language != null, cancellationToken)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Anna's save with <paramref name="json"/> as the body as it is — JSON the helpers can't write, such as a
    /// property left out or a malformed body; an empty JSON body when it is empty.</summary>
    private Task<HttpResponseMessage> PutJsonTextAsync(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _anna.AccessToken);
        return _factory.SendHttpRequest(request);
    }

    private static Task<string?> LanguageOfAsync(ApplicationDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().Where(user => user.Id == userId).Select(user => user.Language).SingleAsync(cancellationToken);
}
