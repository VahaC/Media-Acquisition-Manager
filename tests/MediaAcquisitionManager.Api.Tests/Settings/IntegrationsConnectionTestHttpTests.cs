using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-7 — what the API does around a connection test, with the fake adapter: what it hands the
// adapter (the saved settings, or the dialog's draft with a kept secret read from the row), what it
// answers with the adapter's outcome (200 whatever the service said, ADR 0005), and when it never
// calls the adapter at all. The real adapters through the API are IntegrationsRealAdaptersHttpTests.
public sealed class IntegrationsConnectionTestHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string Address = "http://127.0.0.1:9";
    private const string StoredPassword = "stored-password";
    private const string StoredApiKey = "stored-api-key-1234";
    private const string Connected = "Connected to the fake service.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsConnectionTestHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every outcome an adapter can answer a test with.</summary>
    public static TheoryData<IntegrationCallStatus> Statuses => new(Enum.GetValues<IntegrationCallStatus>());

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Test_is_200_with_the_adapters_status_and_message(IntegrationCallStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room");
        _factory.FakeAdapter.Outcome = _ => new ConnectionTestResult(status, $"The fake answered {status}.");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        // 200 for a failed test too: "no, because …" is the answer to "does it connect?", not a
        // failure of this API. The status travels by name, as the dialog reads it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"status":"{{status}}","message":"The fake answered {{status}}."}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
        var entry = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(IntegrationConnectionService).FullName
            && entry.Message.StartsWith("Tested integration", StringComparison.Ordinal));
        // Information, and never the address or the message: either may carry what a service said
        // back about a credential.
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal($"Tested integration {integration.Id} (fake): {status}.", entry.Message);
    }

    [Fact]
    public async Task Test_hands_the_adapter_the_saved_address_and_the_decrypted_secrets()
    {
        //Arrange
        var integration = await _integrations.Create(
            name: "Living room", baseUrl: "http://media.test:8096/fake", userName: "admin", password: StoredPassword, apiKey: StoredApiKey);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.FakeAdapter.Calls);
        Assert.Equal(integration.Id, call.ConnectionId);
        Assert.Equal("http://media.test:8096/fake", call.BaseUrl);
        Assert.Equal("admin", call.UserName);
        Assert.Equal(StoredPassword, call.Password);
        Assert.Equal(StoredApiKey, call.ApiKey);
    }

    [Fact]
    public async Task Test_of_a_disabled_integration_still_runs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", isEnabled: false);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        // Switching one off is how the owner parks it while fixing its settings.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadTestResponseAsync(response, cancellationToken);
        Assert.Equal(new ConnectionTestResponse(IntegrationCallStatus.Succeeded, Connected), body);
        Assert.Single(_factory.FakeAdapter.Calls);
    }

    [Fact]
    public async Task Test_changes_nothing_and_records_no_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            name: "Living room",
            userName: "admin",
            password: StoredPassword,
            apiKey: StoredApiKey,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertUnchangedAsync(integration, cancellationToken);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(integration.PathMappings[0].Id, Assert.Single(await db.PathMappings.ToListAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Test_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    [Fact]
    public async Task Test_of_an_integration_of_an_unregistered_type_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // What a row of a type an earlier build registered looks like to this one.
        var integration = await _integrations.Create("emby", "Old server", apiKey: StoredApiKey);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "This version of the app can't connect to “emby”, so this integration can only be removed.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
        Assert.Empty(_factory.JellyfinHttp.Requests);
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task Test_with_a_secret_sealed_under_another_key_is_409_and_never_calls_the_adapter()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.CreateWithUnreadableApiKey(FakeIntegrationAdapter.TypeId, "Living room");
        // Armed to succeed, so a call would show as a working service.
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        // Sending no key would read as the service refusing it, when the fix is to enter it again.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "The stored API key of “Living room” can't be read with this installation's encryption key. Enter the API key again and save.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
        // Never cleared: the old key may come back from a backup.
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task Test_with_a_secret_copied_from_another_integration_is_409_and_never_calls_the_adapter()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await _integrations.Create(name: "Living room", apiKey: StoredApiKey);
        var copy = await _integrations.CreateWithCopiedApiKey(source.Id);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{copy.Id}/test", _owner.AccessToken);

        //Assert
        // Sealed under this installation's own key, but for another row: the associated data refuses
        // it, so the source's key never reaches the copy's server (ADR 0007).
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"The stored API key of “{copy.Name}” can't be read with this installation's encryption key. Enter the API key again and save.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
        await AssertUnchangedAsync(copy, cancellationToken);
    }

    [Fact]
    public async Task Test_with_an_unreadable_secret_logs_a_warning_without_the_ciphertext()
    {
        //Arrange
        var integration = await _integrations.CreateWithUnreadableApiKey(FakeIntegrationAdapter.TypeId, "Living room");

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        // A Warning ships off the box: an operator must act on a changed key or an edited database.
        var entry = Assert.Single(_factory.Logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(typeof(IntegrationConnectionService).FullName, entry.Category);
        Assert.Equal(
            $"The stored API key of integration {integration.Id} could not be decrypted with the configured encryption key.",
            entry.Message);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(_factory.Logs.Entries, logged =>
            logged.Message.Contains(integration.ApiKeyEncrypted!, StringComparison.Ordinal)
            || logged.Message.Contains(IntegrationConnectionSeeder.UnreadableApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Test_masks_a_secret_the_adapter_echoed_in_its_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", userName: "admin", password: StoredPassword, apiKey: StoredApiKey);
        // The fake is no HTTP adapter, so only the service's own pass can mask what it repeats.
        _factory.FakeAdapter.Outcome = settings => ConnectionTestResult.Failed(
            IntegrationCallStatus.AuthenticationFailed, $"The service refused the key {settings.ApiKey} and the password {settings.Password}.");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/{integration.Id}/test", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadTestResponseAsync(response, cancellationToken);
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.AuthenticationFailed, "The service refused the key *** and the password ***."),
            body);
    }

    [Fact]
    public async Task TestDraft_of_a_new_integration_is_200_with_the_adapters_outcome_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(
            null, " fake ", " http://media.test:8096/Fake/ ", " admin ", Set("draft password "), Set(" draft-api-key-1234 ")));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadTestResponseAsync(response, cancellationToken);
        Assert.Equal(new ConnectionTestResponse(IntegrationCallStatus.Succeeded, Connected), body);
        // Normalised exactly as a save would store it: the address, a trimmed user name and API key,
        // and the password as typed.
        var call = Assert.Single(_factory.FakeAdapter.Calls);
        Assert.Null(call.ConnectionId);
        Assert.Equal("http://media.test:8096/Fake", call.BaseUrl);
        Assert.Equal("admin", call.UserName);
        Assert.Equal("draft password ", call.Password);
        Assert.Equal("draft-api-key-1234", call.ApiKey);
        await AssertNothingSavedAsync(cancellationToken);
    }

    [Fact]
    public async Task TestDraft_of_a_new_integration_treats_keep_as_no_secret()
    {
        //Arrange
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(null, FakeIntegrationAdapter.TypeId, Address, null, Keep(), Keep()));

        //Assert
        // A new integration has nothing stored to keep.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.FakeAdapter.Calls);
        Assert.Null(call.Password);
        Assert.Null(call.ApiKey);
    }

    [Fact]
    public async Task TestDraft_without_a_required_api_key_is_400_and_calls_no_service()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json");

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(null, "jellyfin", Address));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Jellyfin needs an API key.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Theory]
    [InlineData("ftp://media.test", "The address must be a full http:// or https:// address, such as http://192.168.1.10:8096.")]
    [InlineData("http://127.0.0.1:9/?apikey=draft-api-key-1234", "The address can't have a query (the part from ?). An API key has a field of its own.")]
    public async Task TestDraft_with_an_invalid_address_is_400(string address, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(null, FakeIntegrationAdapter.TypeId, address));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    [Fact]
    public async Task TestDraft_with_an_unknown_adapter_type_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(null, "emby", Address, ApiKey: Set(StoredApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("“emby” is not a service this app can connect to.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_with_a_kept_secret_uses_the_stored_one()
    {
        //Arrange
        var integration = await _integrations.Create(name: "Living room", userName: "admin", password: StoredPassword, apiKey: StoredApiKey);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        // The dialog leaves a secret field empty to keep it: Keep for one, left out for the other.
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(integration.Id, null, Address, "admin", Keep()));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.FakeAdapter.Calls);
        Assert.Equal(integration.Id, call.ConnectionId);
        Assert.Equal(StoredPassword, call.Password);
        Assert.Equal(StoredApiKey, call.ApiKey);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_with_a_set_secret_uses_the_new_one_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", userName: "admin", password: StoredPassword, apiKey: StoredApiKey);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(
            integration.Id, null, Address, "admin", Set("typed-password"), Set("typed-api-key-5678")));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.FakeAdapter.Calls);
        Assert.Equal("typed-password", call.Password);
        Assert.Equal("typed-api-key-5678", call.ApiKey);
        // Only the dialog's Save stores what was typed.
        await AssertUnchangedAsync(integration, cancellationToken);
        await AssertStoredSecretsAsync(integration.Id, StoredPassword, StoredApiKey, cancellationToken);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_with_a_cleared_optional_secret_sends_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", apiKey: StoredApiKey);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(integration.Id, null, Address, ApiKey: Clear()));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(_factory.FakeAdapter.Calls).ApiKey);
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_uses_the_draft_address_and_user_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", userName: "admin", password: StoredPassword);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(
            integration.Id, null, "http://media.test:8096/base/", " someone-else "));

        //Assert
        // What the dialog shows now, with the secret kept: decision 2.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.FakeAdapter.Calls);
        Assert.Equal("http://media.test:8096/base", call.BaseUrl);
        Assert.Equal("someone-else", call.UserName);
        Assert.Equal(StoredPassword, call.Password);
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Theory]
    [InlineData("jellyfin")]
    [InlineData("not-a-type")]
    [InlineData(null)]
    public async Task TestDraft_of_an_edit_uses_the_stored_type_whatever_the_body_says(string? adapterType)
    {
        //Arrange
        var integration = await _integrations.Create(name: "Living room", apiKey: StoredApiKey);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(integration.Id, adapterType, Address));

        //Assert
        // A type never changes, so the stored key can only ever go to an adapter of the stored type.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StoredApiKey, Assert.Single(_factory.FakeAdapter.Calls).ApiKey);
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_of_an_integration_of_an_unregistered_type_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // What a row of a type an earlier build registered looks like to this one.
        var integration = await _integrations.Create("emby", "Old server", apiKey: StoredApiKey);
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        // The body names a registered type; the stored one still decides, so the stored key can't
        // reach an adapter of another type.
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(integration.Id, "jellyfin", Address, ApiKey: Keep()));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "This version of the app can't connect to “emby”, so this integration can only be removed.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
        Assert.Empty(_factory.JellyfinHttp.Requests);
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_with_an_unreadable_kept_secret_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.CreateWithUnreadableApiKey(FakeIntegrationAdapter.TypeId, "Living room");
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(integration.Id, null, Address, ApiKey: Keep()));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "The stored API key of “Living room” can't be read with this installation's encryption key. Enter the API key again and save.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task TestDraft_of_an_edit_whose_integration_is_gone_is_404_with_a_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Connected);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(Guid.NewGuid(), FakeIntegrationAdapter.TypeId, Address));

        //Assert
        // The id is in the body, not the address, so the dialog is told what happened.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "The integration you are editing no longer exists. Close the dialog and reload the page.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    private static SecretValueUpsert Set(string value) => new(SecretUpsertMode.Set, value);

    private static SecretValueUpsert Keep() => new(SecretUpsertMode.Keep);

    private static SecretValueUpsert Clear() => new(SecretUpsertMode.Clear);

    private static async Task<ConnectionTestResponse?> ReadTestResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<ConnectionTestResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);

    // The row exactly as seeded, secrets byte for byte, and no audit row: a test changes nothing.
    private Task AssertUnchangedAsync(IntegrationConnectionEntity seeded, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(x => x.Id == seeded.Id, cancellationToken);
            Assert.Equal(seeded.Name, row.Name);
            Assert.Equal(seeded.BaseUrl, row.BaseUrl);
            Assert.Equal(seeded.UserName, row.UserName);
            Assert.Equal(seeded.IsEnabled, row.IsEnabled);
            Assert.Equal(seeded.PasswordEncrypted, row.PasswordEncrypted);
            Assert.Equal(seeded.ApiKeyEncrypted, row.ApiKeyEncrypted);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });

    private Task AssertStoredSecretsAsync(Guid integrationId, string password, string apiKey, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            var encryption = _factory.Resolve<IEncryptionService>();
            var row = await db.IntegrationConnections.SingleAsync(x => x.Id == integrationId, cancellationToken);
            Assert.True(encryption.TryDecrypt(
                row.PasswordEncrypted!, IntegrationSecretPurpose.For(integrationId, IntegrationSecretField.Password), out var storedPassword));
            Assert.Equal(password, storedPassword);
            Assert.True(encryption.TryDecrypt(
                row.ApiKeyEncrypted!, IntegrationSecretPurpose.For(integrationId, IntegrationSecretField.ApiKey), out var storedApiKey));
            Assert.Equal(apiKey, storedApiKey);
        });

    private Task AssertNothingSavedAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
