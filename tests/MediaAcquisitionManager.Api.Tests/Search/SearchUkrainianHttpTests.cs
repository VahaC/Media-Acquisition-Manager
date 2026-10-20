using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-47 WP14 (ADR 0042, design §8.6) — the Search page and search_releases in Ukrainian: a source's stored answer is
// rendered in the reader's language when the search is read — a feed's refusal with its own words verbatim, an error an
// older build stored in English converted at start, words the converter couldn't read shown as they are —, a refused
// search quotes its value, and the assistant's summary counts releases and sources in their own forms («у 2 джерелах
// релізів»). The same request without the header answers English. Each answer spelled here, never read from the files.
public sealed class SearchUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0047";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _other = null!;

    public SearchUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey);
        _other = new FakeTorznabServer(OtherAddress, ApiKey);
        FakeTorznabServer.Arm(_factory.TorznabHttp, _toloka, _other);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_asked_in_ukrainian_shows_a_refusing_feeds_error_with_the_feeds_own_words()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = request => request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal)
            ? VendorResponses.Text(
                """<error code="429" description="Indexer is disabled till 01/10/2026 14:05:00 due to recent failures." />""",
                HttpStatusCode.TooManyRequests,
                "application/xml")
            : null;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var toloka = Assert.Single((await ReadAsync(response, "uk", cancellationToken)).Sources);
        Assert.Equal(
            (IntegrationCallStatus.Refused, "Джерело Torznab поки що відхиляє запити (помилка 429: Indexer is disabled till 01/10/2026 14:05:00 due to recent failures.). Спробуйте ще раз за кілька хвилин."),
            (toloka.Status!.Value, toloka.Error));
    }

    [Fact]
    public async Task Get_asked_in_ukrainian_reads_a_sources_error_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var search = await CreateAsync(cancellationToken);
        await _factory.ConvertStoredEnglishAsync(
            "SearchSources",
            "Error",
            Assert.Single(search.Sources).Id,
            $"Couldn't reach Torznab at {TolokaAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.");

        //Act
        var ukrainian = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _owner.AccessToken, language: "uk");
        var english = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(
            $"Не вдалося зв’язатися із сервісом Torznab за адресою {TolokaAddress}: з’єднання ніхто не прийняв. Перевірте адресу й порт і переконайтеся, що сервіс Torznab запущено.",
            Assert.Single((await ReadAsync(ukrainian, "uk", cancellationToken)).Sources).Error);
        Assert.Equal(
            $"Couldn't reach Torznab at {TolokaAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.",
            Assert.Single((await ReadAsync(english, null, cancellationToken)).Sources).Error);
    }

    [Fact]
    public async Task Get_asked_in_ukrainian_shows_words_the_converter_couldnt_read_as_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var search = await CreateAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"""UPDATE "SearchSources" SET "Error" = {"Torznab couldn't be reached."}""", cancellationToken));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal("Torznab couldn't be reached.", Assert.Single((await ReadAsync(response, "uk", cancellationToken)).Sources).Error);
    }

    [Fact]
    public async Task Create_with_a_year_that_isnt_one_asked_in_ukrainian_is_400_quoting_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar", Year: "MMXIV"), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «MMXIV» не є роком.","code":"releases.search.year-invalid"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task SearchReleases_on_a_connection_asking_for_ukrainian_counts_one_source_in_its_own_form()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync(
            "search_releases", new Dictionary<string, object?> { ["text"] = "Interstellar", ["pageSize"] = 2 }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(
            "Знайдено 16 релізів за запитом «Interstellar» у 1 джерелі релізів; на першій сторінці — 2; у медіатеці немає відповідних творів.",
            result.ReadRawText());
    }

    [Fact]
    public async Task SearchReleases_on_a_connection_asking_for_ukrainian_names_a_failed_source_after_the_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _other.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync(
            "search_releases", new Dictionary<string, object?> { ["text"] = "Interstellar", ["pageSize"] = 2 }, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            "Знайдено 16 релізів за запитом «Interstellar» у 2 джерелах релізів; на першій сторінці — 2; пошук у джерелі «Other» не вдався (Unreachable); у медіатеці немає відповідних творів.",
            result.ReadRawText());
    }

    // Arrange's search: a failure throws rather than asserts, so nothing asserts before the Act.
    private async Task<SearchResponse> CreateAsync(CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await ReadAsync(response, null, cancellationToken);
    }

    // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
    private static async Task<SearchResponse> ReadAsync(HttpResponseMessage response, string? language, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchResponse>(
            language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions,
            cancellationToken))!;
}
