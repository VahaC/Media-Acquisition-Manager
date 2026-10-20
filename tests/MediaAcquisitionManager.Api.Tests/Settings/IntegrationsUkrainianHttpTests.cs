using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-47 WP14 (ADR 0042, design §2.2, §8.6) — the Integrations page's connection test in Ukrainian, through the real
// adapters with only the network faked: what a service said stays its own words, verbatim (a server's name, a feed's
// error with the key masked), the labels of a product with a Ukrainian interface are its own Ukrainian ones (Jellyfin's
// «Панель керування» → «API-ключі»), those of one without stay English («Edit preferences» in Transmission's web
// interface), and a refused save counts its rule. The same test without the header answers English. Each answer spelled
// here, never read from the locale files.
public sealed class IntegrationsUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string JellyfinAddress = "http://jellyfin.test:8096";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string TorznabAddress = "http://prowlarr.test:9696/1/api";
    private const string TorznabApiKey = "prowlarr-api-key-0047";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Test_of_jellyfin_asked_in_ukrainian_keeps_the_version_and_the_servers_name_as_jellyfin_says_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("jellyfin", "Living room", JellyfinAddress, apiKey: JellyfinApiKey);
        _factory.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json; charset=utf-8");

        //Act
        var ukrainian = await TestAsync(integration.Id, "uk", cancellationToken);
        var english = await TestAsync(integration.Id, null, cancellationToken);

        //Assert
        Assert.Equal(new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Підключено до сервісу Jellyfin 12.1.0 (VahaC media)."), ukrainian);
        Assert.Equal(new ConnectionTestResponse(IntegrationCallStatus.Succeeded, "Connected to Jellyfin 12.1.0 (VahaC media)."), english);
    }

    [Fact]
    public async Task Test_of_jellyfin_with_a_refused_key_asked_in_ukrainian_names_jellyfins_own_ukrainian_labels()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("jellyfin", "Living room", JellyfinAddress, apiKey: JellyfinApiKey);
        _factory.JellyfinHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        //Act
        var answer = await TestAsync(integration.Id, "uk", cancellationToken);

        //Assert
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.AuthenticationFailed,
                "Сервіс Jellyfin відхилив ключ API. Створіть ключ у Jellyfin на сторінці «Панель керування» → «API-ключі» і введіть його тут."),
            answer);
    }

    [Fact]
    public async Task Test_of_torznab_whose_error_echoes_the_key_asked_in_ukrainian_nests_the_feeds_words_masked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("torznab", "Toloka", TorznabAddress, apiKey: TorznabApiKey);
        _factory.TorznabHttp.Handler = (_, _) => VendorResponses.Text(
            $"""<?xml version="1.0" encoding="UTF-8"?><error code="100" description="Invalid API key {TorznabApiKey}" />""",
            HttpStatusCode.Unauthorized,
            "application/xml");

        //Act
        var answer = await TestAsync(integration.Id, "uk", cancellationToken);

        //Assert
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.AuthenticationFailed,
                "Джерело Torznab відхилило ключ API (помилка 100: Invalid API key ***). Скопіюйте ключ API з панелі керування сервісу, який надає це джерело (наприклад Jackett чи Prowlarr), у цю інтеграцію на сторінці «Інтеграції»."),
            answer);
    }

    [Fact]
    public async Task Test_of_transmission_with_its_temporary_folder_on_asked_in_ukrainian_names_its_english_labels()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            "transmission", "Transmission", FakeTransmissionServer.DefaultBaseUrl,
            userName: FakeTransmissionServer.DefaultUserName, password: FakeTransmissionServer.DefaultPassword);
        new FakeTransmissionServer { IncompleteDirEnabled = true }.Arm(_factory.TransmissionHttp);

        //Act
        var answer = await TestAsync(integration.Id, "uk", cancellationToken);

        //Assert
        Assert.Equal(
            new ConnectionTestResponse(
                IntegrationCallStatus.Refused,
                "Клієнт Transmission зберігає незавершені завантаження у своїй тимчасовій теці «/downloads/incomplete», де застосунок не може їх відстежувати. У вебінтерфейсі Transmission відкрийте «Edit preferences» → «Torrents», зніміть позначку «Use temporary folder», а потім перевірте з’єднання ще раз."),
            answer);
    }

    [Fact]
    public async Task Create_with_a_short_api_key_asked_in_ukrainian_is_400_counting_the_characters_it_needs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new CreateIntegrationRequest("jellyfin", "Living room", JellyfinAddress, ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "1234567"));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ("Ключ API має містити щонайменше 8 символів.", "integrations.secret.api-key-too-short"),
            await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_with_a_name_another_integration_has_asked_in_ukrainian_is_409_quoting_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", JellyfinAddress, apiKey: JellyfinApiKey);
        var request = new CreateIntegrationRequest("jellyfin", "Living room", JellyfinAddress, ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, JellyfinApiKey));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            """{"error":"Інтеграція з назвою «Living room» уже існує.","code":"integrations.service.name-taken"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task<ConnectionTestResponse?> TestAsync(Guid integrationId, string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integrationId}/test", _owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return await response.Content.ReadFromJsonAsync<ConnectionTestResponse>(options, cancellationToken);
    }
}
