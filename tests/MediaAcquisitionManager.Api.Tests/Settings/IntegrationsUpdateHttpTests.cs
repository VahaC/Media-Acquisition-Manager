using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Changing an integration with the edit dialog's one Save: every field, the secrets' Keep / Set /
// Clear, the switch, and when a save records an audit row. Path mappings are
// IntegrationsPathMappingsHttpTests.
public sealed class IntegrationsUpdateHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string Address = "http://127.0.0.1:9";
    private const string StoredPassword = "stored-password";
    private const string StoredApiKey = "stored-api-key-1234";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsUpdateHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Update_is_204_and_persists_every_field()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            name: "Old name",
            userName: "old-user",
            password: StoredPassword,
            apiKey: StoredApiKey,
            pathMappings: [new PathMappingPair("/old", "/media/old")]);
        var encryption = _factory.Resolve<IEncryptionService>();

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest(
            "  New name  ",
            "http://127.0.0.1:10/base/",
            " new-user ",
            Set("new-password"),
            Set(" new-api-key-5678 "),
            IsEnabled: false,
            PathMappings: [new PathMappingRequest("/downloads/", "/media/")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.Include(x => x.PathMappings).SingleAsync(cancellationToken);
            Assert.Equal("New name", row.Name);
            Assert.Equal("NEW NAME", row.NormalizedName);
            Assert.Equal("http://127.0.0.1:10/base", row.BaseUrl);
            Assert.Equal("new-user", row.UserName);
            Assert.False(row.IsEnabled);
            Assert.True(encryption.TryDecrypt(
                row.PasswordEncrypted!, IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.Password), out var password));
            Assert.Equal("new-password", password);
            Assert.True(encryption.TryDecrypt(
                row.ApiKeyEncrypted!, IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.ApiKey), out var apiKey));
            Assert.Equal("new-api-key-5678", apiKey);
            var mapping = Assert.Single(row.PathMappings);
            Assert.Equal(("/downloads", "/media", 0), (mapping.ServicePrefix, mapping.AppPrefix, mapping.Position));
            // What a type is, and when it was added, never change.
            Assert.Equal(integration.AdapterType, row.AdapterType);
            Assert.Equal(IntegrationKind.MediaLibrary, row.Kind);
            Assert.Equal(integration.CreatedUtc, row.CreatedUtc);
        });
    }

    [Fact]
    public async Task Update_records_an_integration_updated_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Old name");
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("New name", Address));

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.IntegrationUpdated, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.Integration, audit.TargetKind);
            Assert.Equal(integration.Id, audit.TargetId);
            Assert.Equal("New name", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Update_that_changes_nothing_is_204_and_records_no_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            name: "Living room",
            userName: "admin",
            password: StoredPassword,
            apiKey: StoredApiKey,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

        //Act
        // The dialog saved as it opened: the same values, typed a little differently, and the secrets
        // left empty (kept).
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest(
            " Living room ", $"{Address}/", "admin", IsEnabled: true, PathMappings: [new PathMappingRequest("/downloads/", "/media")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            var row = await db.IntegrationConnections.Include(x => x.PathMappings).SingleAsync(cancellationToken);
            Assert.Equal(integration.PasswordEncrypted, row.PasswordEncrypted);
            Assert.Equal(integration.ApiKeyEncrypted, row.ApiKeyEncrypted);
            // Not deleted and inserted again.
            Assert.Equal(integration.PathMappings[0].Id, Assert.Single(row.PathMappings).Id);
        });
    }

    [Theory]
    [InlineData("password", false)]
    [InlineData("password", true)]
    [InlineData("apiKey", false)]
    [InlineData("apiKey", true)]
    public async Task Update_that_leaves_a_secret_out_or_keeps_it_keeps_the_ciphertext_byte_for_byte(string field, bool sendsKeep)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", userName: "admin", password: StoredPassword, apiKey: StoredApiKey);
        // A dictionary, so "left out" really is absent from the JSON rather than null.
        var body = new Dictionary<string, object?> { ["name"] = "Renamed", ["baseUrl"] = Address, ["userName"] = "admin" };
        if (sendsKeep)
            body[field] = new { mode = "Keep" };

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Equal("Renamed", row.Name);
            Assert.Equal(integration.PasswordEncrypted, row.PasswordEncrypted);
            Assert.Equal(integration.ApiKeyEncrypted, row.ApiKeyEncrypted);
        });
    }

    [Fact]
    public async Task Update_with_set_replaces_the_secret()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("jellyfin", "Living room", apiKey: StoredApiKey);
        var encryption = _factory.Resolve<IEncryptionService>();

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Living room", Address, ApiKey: Set("replacement-key-5678")));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.NotEqual(integration.ApiKeyEncrypted, row.ApiKeyEncrypted);
            Assert.True(encryption.TryDecrypt(
                row.ApiKeyEncrypted!, IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.ApiKey), out var apiKey));
            Assert.Equal("replacement-key-5678", apiKey);
            // A new secret is a change, recorded like any other.
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_with_clear_removes_an_optional_api_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", apiKey: StoredApiKey);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Living room", Address, ApiKey: Clear()));
        var read = await _factory.GetHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await read.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.False(body!.HasApiKey);
        await _factory.WithDbAsync(async db =>
            Assert.Null((await db.IntegrationConnections.SingleAsync(cancellationToken)).ApiKeyEncrypted));
    }

    [Fact]
    public async Task Update_clearing_both_optional_credentials_removes_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("qbittorrent", "Downloads", userName: "admin", password: StoredPassword);

        //Act
        // qBittorrent now lets the app's address in without signing in.
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Downloads", Address, UserName: "", Password: Clear()));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Null(row.UserName);
            Assert.Null(row.PasswordEncrypted);
        });
    }

    [Fact]
    public async Task Update_with_clear_of_a_required_api_key_is_400_and_keeps_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("jellyfin", "Living room", apiKey: StoredApiKey);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Living room", Address, ApiKey: Clear()));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Jellyfin needs an API key.", await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task Update_with_a_blank_new_secret_is_400_and_keeps_the_stored_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create("jellyfin", "Living room", apiKey: StoredApiKey);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Living room", Address, ApiKey: Set("   ")));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A new API key can't be blank.", await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task Update_keeping_an_unreadable_secret_is_204_and_keeps_the_ciphertext()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Sealed under another key: a key changed since, which may come back from a backup.
        var integration = await _integrations.CreateWithUnreadableApiKey("jellyfin", "Living room");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Renamed", Address));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Equal("Renamed", row.Name);
            Assert.Equal(integration.ApiKeyEncrypted, row.ApiKeyEncrypted);
        });
    }

    [Fact]
    public async Task Update_without_is_enabled_keeps_the_switch()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", isEnabled: false);

        //Act
        // A client that doesn't know the switch must not turn the integration back on.
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new { name = "Renamed", baseUrl = Address });

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Equal("Renamed", row.Name);
            Assert.False(row.IsEnabled);
        });
    }

    [Fact]
    public async Task Update_switches_the_integration_off_and_on_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room");
        var url = $"{BaseUrl}/{integration.Id}";

        //Act
        var off = await _factory.PutHttpRequest(url, _owner.AccessToken, new UpdateIntegrationRequest("Living room", Address, IsEnabled: false));
        var whileOff = await _factory.GetHttpRequest(url, _owner.AccessToken);
        var on = await _factory.PutHttpRequest(url, _owner.AccessToken, new UpdateIntegrationRequest("Living room", Address, IsEnabled: true));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.False((await whileOff.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.IsEnabled);
        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.True((await db.IntegrationConnections.SingleAsync(cancellationToken)).IsEnabled);
            Assert.Equal(2, await db.AuditEvents.CountAsync(a => a.Action == AuditAction.IntegrationUpdated, cancellationToken));
        });
    }

    [Fact]
    public async Task Update_to_a_name_another_integration_has_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create(name: "Living room");
        var bedroom = await _integrations.Create(name: "Bedroom");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{bedroom.Id}", _owner.AccessToken, new UpdateIntegrationRequest("LIVING ROOM", "http://127.0.0.1:10"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("An integration named “LIVING ROOM” already exists.", await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(bedroom, cancellationToken);
    }

    [Fact]
    public async Task Update_to_its_own_name_in_another_letter_case_is_204()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("LIVING ROOM", Address));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal("LIVING ROOM", (await db.IntegrationConnections.SingleAsync(cancellationToken)).Name));
    }

    [Fact]
    public async Task Update_ignores_an_adapter_type_in_the_body()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room");

        //Act
        // A type never changes: the update contract has no field for it.
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new { adapterType = "qbittorrent", name = "Renamed", baseUrl = Address });

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Equal("Renamed", row.Name);
            Assert.Equal(integration.AdapterType, row.AdapterType);
            Assert.Equal(IntegrationKind.MediaLibrary, row.Kind);
        });
    }

    [Fact]
    public async Task Update_with_an_invalid_address_is_400_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(name: "Living room", apiKey: StoredApiKey);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Renamed", "ftp://media.test", ApiKey: Set("replacement-key-5678")));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "The address must be a full http:// or https:// address, such as http://192.168.1.10:8096.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(integration, cancellationToken);
    }

    [Fact]
    public async Task Update_of_an_integration_of_an_unregistered_type_is_409_with_the_remove_only_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var retired = await _integrations.Create("retired-service", "Old library", apiKey: StoredApiKey);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{retired.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Renamed", Address));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "This version of the app can't connect to “retired-service”, so this integration can only be removed.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(retired, cancellationToken);
    }

    [Fact]
    public async Task Update_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken, new UpdateIntegrationRequest("Renamed", Address));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    private static SecretValueUpsert Set(string value) => new(SecretUpsertMode.Set, value);

    private static SecretValueUpsert Clear() => new(SecretUpsertMode.Clear);

    // The row exactly as seeded, and no audit row.
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
}
