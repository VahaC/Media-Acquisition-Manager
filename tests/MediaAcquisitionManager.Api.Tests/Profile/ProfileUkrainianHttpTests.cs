using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-47 WP14 (ADR 0042, design §8.6) — accounts, the language and the personal access tokens in Ukrainian, as a person
// reads them: a refusal names its value in «…», a counted rule writes its number with the noun's Ukrainian form (8
// символів, 32 символів after «до»), the token's example name is the web app's own, and the same request without the
// header answers English with the same code. Each answer spelled here, never read from the locale files.
public sealed class ProfileUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string LanguageUrl = "/api/profile/language";
    private const string TokensUrl = "/api/profile/tokens";
    private const string PasswordUrl = "/api/auth/password";
    private const string UsersUrl = "/api/settings/users";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _anna = null!;

    public ProfileUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _anna = await users.CreateAuthorized("anna");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Update_language_to_one_the_app_lacks_asked_in_ukrainian_is_400_quoting_the_value()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(LanguageUrl, _anna.AccessToken, new UserLanguageRequest("de"), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «de» не є мовою застосунку. Вкажіть en, uk або null (як у браузері).","code":"account.language.unsupported"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Update_language_to_one_the_app_lacks_without_the_header_answers_english_with_the_same_code()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(LanguageUrl, _anna.AccessToken, new UserLanguageRequest("de"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json("“de” is not a language of the app. Use en, uk, or null to follow the browser.", "account.language.unsupported"),
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Change_password_to_a_short_one_asked_in_ukrainian_counts_the_characters_it_needs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(PasswordUrl, _anna.AccessToken, new ChangePasswordRequest(UserSeeder.DefaultPassword, "short"), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ("Пароль має містити щонайменше 8 символів.", "account.password.too-short"),
            await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_user_with_a_short_name_asked_in_ukrainian_counts_the_range_in_its_own_form()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(UsersUrl, _owner.AccessToken, new CreateUserRequest("al", UserSeeder.DefaultPassword), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ("Ім’я користувача має містити від 3 до 32 символів.", "account.user-name.length"),
            await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_user_with_a_taken_name_asked_in_ukrainian_is_409_naming_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(UsersUrl, _owner.AccessToken, new CreateUserRequest("anna", UserSeeder.DefaultPassword), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(("Користувач anna уже існує.", "account.users.name-taken"), await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_token_without_a_name_asked_in_ukrainian_gives_the_web_apps_example_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new CreatePersonalAccessTokenRequest("  ", PersonalAccessTokenAccess.ReadOnly, PersonalAccessTokenLifetime.Days30);

        //Act
        var response = await _factory.PostHttpRequest(TokensUrl, _anna.AccessToken, request, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Дайте токену назву, наприклад «Claude на ноутбуці».","code":"account.token.name-required"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_token_with_a_long_name_asked_in_ukrainian_counts_the_characters_it_may_have()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new CreatePersonalAccessTokenRequest(new string('n', 101), PersonalAccessTokenAccess.ReadOnly, PersonalAccessTokenLifetime.Days30);

        //Act
        var response = await _factory.PostHttpRequest(TokensUrl, _anna.AccessToken, request, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ("Назва токена може містити щонайбільше 100 символів.", "account.token.name-too-long"),
            await response.ReadErrorBodyAsync(cancellationToken));
    }
}
