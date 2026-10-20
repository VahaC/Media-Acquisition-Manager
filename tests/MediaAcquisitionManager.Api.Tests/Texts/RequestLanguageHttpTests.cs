using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — over real HTTP, the language of a server sentence is the request's Accept-Language and nothing
// else, read in the order a browser ranks it. Seen through the pseudo-language on a host that accepts it: a sentence
// comes back wrapped exactly when the pseudo-language won, so a range ranked above it that the server reads — uk, uk-UA,
// en-GB — shows by the answer being unwrapped, and one it doesn't read (de, q=0, a malformed range) by the answer being
// wrapped. WP14 — with the Ukrainian files in, the answer also shows which of en and uk won. WP5 — the
// owner-only 403, written by an authorization filter before the action runs, follows the same rule.
public sealed class RequestLanguageHttpTests :
    IClassFixture<PseudoLanguageApiFactory>, IClassFixture<PlainLanguageApiFactory>, IAsyncLifetime
{
    private const string UnknownApiUrl = "/api/nope";
    private const string English = "No API endpoint matches GET /api/nope.";
    private const string Ukrainian = "Жодна кінцева точка API не відповідає запиту GET /api/nope.";
    private const string Code = "common.request.unknown-endpoint";

    // A settings write, owner-only by convention: a member is refused before the body is read.
    private const string OwnerOnlyUrl = "/api/settings/users";
    private const string OwnerOnly = "Only the installation owner can do this.";
    private const string OwnerOnlyUkrainian = "Це може зробити лише власник.";
    private const string OwnerOnlyCode = "account.owner-only";

    private readonly PseudoLanguageApiFactory _factory;
    private readonly PlainLanguageApiFactory _production;

    public RequestLanguageHttpTests(PseudoLanguageApiFactory factory, PlainLanguageApiFactory production)
    {
        _factory = factory;
        _production = production;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        await _production.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("qps-ploc", "pseudo")]
    [InlineData("uk, qps;q=0.5", "uk")]
    [InlineData("uk-UA, qps;q=0.5", "uk")]
    [InlineData("en-GB, qps;q=0.5", "en")]
    [InlineData("de, uk;q=0.5, qps;q=0.4", "uk")]
    [InlineData("qps;q=0.5, uk;q=0.9", "uk")]
    [InlineData("de, qps;q=0.5", "pseudo")]
    [InlineData("uk;q=0, qps;q=0.5", "pseudo")]
    [InlineData("de", "en")]
    public async Task Unmatched_route_answers_in_the_language_the_header_ranks_first(string acceptLanguage, string answer)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(UnknownApiUrl, language: acceptLanguage);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((InLanguage(answer, English, Ukrainian), Code), await ReadRawErrorAsync(response, cancellationToken));
    }

    [Theory]
    [InlineData("@@@")]
    [InlineData(";q=;;,")]
    public async Task Unmatched_route_answers_english_for_a_malformed_header(string acceptLanguage)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(UnknownApiUrl, language: acceptLanguage);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((English, Code), await ReadRawErrorAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Unmatched_route_answers_english_without_the_header()
    {
        //Arrange
        // The server's own client: the factory's would ask for the pseudo-language in that pass.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Server.CreateClient();

        //Act
        var response = await client.GetAsync(UnknownApiUrl, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((English, Code), await ReadRawErrorAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Unmatched_route_ignores_the_language_a_user_saved()
    {
        //Arrange
        // Users.Language is never read: a request that names no language is answered in English for every user, one who
        // saved Ukrainian too.
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = new UserSeeder(_factory.Scope);
        var user = await users.CreateAuthorized();
        await users.SetLanguage(user.User, "uk");
        using var client = _factory.Server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, UnknownApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        //Act
        var response = await client.SendAsync(request, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((English, Code), await ReadRawErrorAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Unmatched_route_answers_english_to_the_pseudo_language_unless_the_server_accepts_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _production.GetHttpRequest(UnknownApiUrl, language: TestLanguage.PseudoTag);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((English, Code), await ReadRawErrorAsync(response, cancellationToken));
    }

    [Theory]
    [InlineData("qps-ploc", "pseudo")]
    [InlineData("uk, qps;q=0.5", "uk")]
    [InlineData("en-GB, qps;q=0.5", "en")]
    [InlineData("de, qps;q=0.5", "pseudo")]
    [InlineData("de", "en")]
    public async Task Owner_only_refusal_answers_in_the_language_the_header_ranks_first(string acceptLanguage, string answer)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized();

        //Act
        var response = await _factory.PostHttpRequest(OwnerOnlyUrl, member.AccessToken, language: acceptLanguage);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal((InLanguage(answer, OwnerOnly, OwnerOnlyUkrainian), OwnerOnlyCode), await ReadRawErrorAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Owner_only_refusal_ignores_the_language_the_member_saved()
    {
        //Arrange
        // The refused member saved Ukrainian; a request that names no language is still answered in English.
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = new UserSeeder(_factory.Scope);
        var member = await users.CreateAuthorized();
        await users.SetLanguage(member.User, "uk");
        using var client = _factory.Server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, OwnerOnlyUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);

        //Act
        var response = await client.SendAsync(request, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal((OwnerOnly, OwnerOnlyCode), await ReadRawErrorAsync(response, cancellationToken));
    }

    // The sentence the language that won writes: English, Ukrainian, or English wrapped by the pseudo-language.
    private static string InLanguage(string answer, string english, string ukrainian) => answer switch
    {
        "en" => english,
        "uk" => ukrainian,
        "pseudo" => $"⟦{english}⟧",
        _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, "en, uk or pseudo"),
    };

    // The body as it came, wrapped or not: what the test is about.
    private static async Task<(string? Error, string? Code)> ReadRawErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (document.RootElement.GetProperty("error").GetString(), document.RootElement.GetProperty("code").GetString());
    }
}
