using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using MediaAcquisitionManager.Api.Controllers;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Http;

// MAM-47 (ADR 0042) — [UnreadableBodyError] names its sentence by a key, as an attribute takes only a constant: every
// argument must be a key of the English locale files whose template takes no value (the attribute gives it none), or the
// answer to an unreadable body would be a 500 (a malformed key) or a sentence missing its words. KeysInUseTests catches
// only a key-shaped argument without a template; an argument that is still a sentence would pass it. And the answer is the
// one error body: the sentence and its key as the code.
public sealed class UnreadableBodyErrorAttributeHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string LanguageUrl = "/api/profile/language";
    private const string LanguageUnreadableBody = "Send the language as JSON: language en, uk, or null to follow the browser.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _anna = null!;

    public UnreadableBodyErrorAttributeHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _anna = await new UserSeeder(_factory.Scope).CreateAuthorized("anna");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Every_argument_is_a_key_whose_english_template_takes_no_value()
    {
        //Arrange
        var arguments = typeof(UnknownApiController).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<UnreadableBodyErrorAttribute>()
                .Select(attribute => (Where: $"{method.DeclaringType!.FullName}.{method.Name}", attribute.Key)))
            .ToList();

        //Act
        var problems = arguments
            .Where(argument => !IsValueLessEnglishKey(argument.Key))
            .Select(argument => $"{argument.Key} (on {argument.Where})")
            .ToList();

        //Assert
        Assert.NotEmpty(arguments);
        Assert.True(
            problems.Count == 0,
            $"[UnreadableBodyError] arguments that aren't a key of Locales/en with a template taking no value:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Theory]
    // The guard of the guard: what the check above must refuse — a sentence left as the argument, a key with no English
    // template, and a key whose template needs a value the attribute can't give.
    [InlineData("account.language.unreadable-body", true)]
    [InlineData(LanguageUnreadableBody, false)]
    [InlineData("account.language.no-such-sentence", false)]
    [InlineData("integrations.adapter.credentials-refused", false)]
    public void IsValueLessEnglishKey_takes_a_key_without_values_and_refuses_a_sentence_an_unknown_key_and_one_with_values(
        string argument, bool expected)
    {
        //Arrange

        //Act
        var taken = IsValueLessEnglishKey(argument);

        //Assert
        Assert.Equal(expected, taken);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"language":1}""")]
    public async Task Update_language_with_a_body_mvc_cant_read_answers_the_sentence_with_its_key_as_the_code(string json)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Put, LanguageUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _anna.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json(LanguageUnreadableBody, "account.language.unreadable-body"),
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Whether <paramref name="key"/> is a well-formed key with an English template that names no value.</summary>
    private static bool IsValueLessEnglishKey(string key)
    {
        try
        {
            _ = Message.Of(key);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return TemplateCatalog.Embedded.TryFind(TextLanguage.English, key, out var template) && template.Slots.Count == 0;
    }
}
