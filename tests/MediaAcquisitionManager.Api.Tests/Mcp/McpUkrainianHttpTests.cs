using System.Net;
using System.Reflection;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-47 WP14 (ADR 0042, design §4.5, §8.6) — an assistant that connects with Accept-Language uk reads MCP's answers in
// Ukrainian: get_status's summary with its tallies counted in the noun's own form, a missing job, arguments that don't
// fit with the binder's own words kept verbatim, and the endpoint's refusal of a web app session — while a connection
// that sends no language reads English, and the tools' descriptions stay English for the model. Each answer spelled
// here, never read from the locale files.
public sealed class McpUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public McpUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(0, "0 джерел медіатеки")]
    [InlineData(1, "1 джерело медіатеки")]
    [InlineData(2, "2 джерела медіатеки")]
    public async Task GetStatus_on_a_connection_asking_for_ukrainian_counts_the_library_sources_in_their_own_form(int sources, string tally)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        for (var source = 1; source <= sources; source++)
            await integrations.Create("jellyfin", $"Room {source}", apiKey: "0123456789abcdef0123456789abcdef");
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, name: "Скрипт");
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("get_status", cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(
            $"Ви дієте від імені користувача owner (власника) через токен «Скрипт» з доступом «Читання й завантаження» у застосунку MAM {Version}; {tally}, 0 джерел релізів із помилкою.",
            result.ReadRawText());
    }

    [Fact]
    public async Task GetStatus_on_a_connection_that_sends_no_language_answers_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly, name: "Script");
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync("get_status", cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            $"You act as member (a member) with the Read only token “Script”, on MAM {Version}; 0 library sources, 0 release sources failing.",
            result.ReadText());
    }

    [Fact]
    public async Task GetJob_of_a_job_that_isnt_the_callers_on_a_connection_asking_for_ukrainian_is_the_ukrainian_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("get_job", new Dictionary<string, object?> { ["jobId"] = Guid.NewGuid() }, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            (true, "Серед ваших завдань завантаження немає завдання з таким ID; інструмент list_jobs показує ваші завдання разом з їхніми ID."),
            (result.IsError, result.ReadRawText()));
    }

    [Fact]
    public async Task Call_with_arguments_that_dont_fit_on_a_connection_asking_for_ukrainian_keeps_the_binders_words_verbatim()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("get_job", new Dictionary<string, object?> { ["jobId"] = "not-a-job-id" }, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            (true, "Аргументи не підходять для інструмента get_job: The JSON value could not be converted to System.Guid. Path: $ | LineNumber: 0 | BytePositionInLine: 14. Звірте їх зі схемою вхідних даних інструмента й викличте його ще раз."),
            (result.IsError, result.ReadRawText()));
    }

    [Fact]
    public async Task Post_with_a_sessions_access_token_asked_in_ukrainian_is_403_pointing_to_the_readme_heading()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostMcpMessageAsync(_member.AccessToken, Initialize, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            ("Кінцева точка MCP приймає персональний токен доступу, а не сеанс вебзастосунку. Створіть токен на сторінці «Профіль» і підключіться за його допомогою; див. розділ «Connect an AI assistant (MCP)» у README.", "account.token.session-not-accepted"),
            await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task List_tools_on_a_connection_asking_for_ukrainian_keeps_the_descriptions_for_the_model_in_english()
    {
        //Arrange
        // AI-facing text stays English (ADR 0042): the model reads it, whatever its user speaks.
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var ukrainian = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");
        await using var english = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "en");

        //Act
        var inUkrainian = await ukrainian.ListToolsAsync(cancellationToken: cancellationToken);
        var inEnglish = await english.ListToolsAsync(cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            inEnglish.Select(tool => (tool.Name, tool.Description)).OrderBy(tool => tool.Name, StringComparer.Ordinal),
            inUkrainian.Select(tool => (tool.Name, tool.Description)).OrderBy(tool => tool.Name, StringComparer.Ordinal));
        Assert.DoesNotContain(inUkrainian, tool => tool.Description?.Any(letter => letter is >= 'А' and <= 'я') == true);
    }

    private static string Version => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
