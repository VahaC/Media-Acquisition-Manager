using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Adding an integration: what is stored, how its secrets are sealed, its audit row, and every rule a
// new integration is checked against. Path mappings are IntegrationsPathMappingsHttpTests.
public sealed class IntegrationsCreateHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string Address = "http://127.0.0.1:9";
    private const string ApiKey = "new-api-key-1234";

    private const string AddressNotHttpError =
        "The address must be a full http:// or https:// address, such as http://192.168.1.10:8096.";
    private const string ApiKeyCharactersError =
        "An API key can contain only visible characters, without spaces, quotes or backslashes.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsCreateHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> UnknownAdapterTypes => new()
    {
        { "plex", "“plex” is not a service this app can connect to." },
        // Exact: the id is what every stored row carries, compared byte for byte.
        { "Jellyfin", "“Jellyfin” is not a service this app can connect to." },
        // A pasted page of text doesn't come back whole.
        { new string('x', 100), $"“{new string('x', 64)}…” is not a service this app can connect to." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_is_201_with_the_integration_and_the_location_that_reads_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "jellyfin", "Living room", Address, ApiKey: Set(ApiKey), PathMappings: [new PathMappingRequest("/data/movies", "/media/movies")]));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new IntegrationDto(
                body!.Id, "Living room", "jellyfin", "Jellyfin", IntegrationKind.MediaLibrary, IsEnabled: true, Address,
                UserName: null, HasPassword: false, HasApiKey: true, [new PathMappingDto("/data/movies", "/media/movies")], now),
            body,
            strict: true);
        Assert.Equal($"/api/settings/integrations/{body.Id}", response.Headers.Location?.AbsolutePath);
        var read = await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, _owner.AccessToken);
        Assert.Equivalent(body, await read.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken), strict: true);
    }

    [Fact]
    public async Task Create_of_a_download_client_answers_what_its_destinations_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("qbittorrent", "Downloads", Address, "admin", Password: Set("qbittorrent-password")));

        //Assert
        // MAM-30 — asked of the type's capabilities (ADR 0005): qBittorrent's destinations are its categories.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new IntegrationDto(
                body!.Id, "Downloads", "qbittorrent", "qBittorrent", IntegrationKind.DownloadClient, IsEnabled: true, Address,
                "admin", HasPassword: true, HasApiKey: false, [], now, DownloadDestinationKind.Category),
            body,
            strict: true);
        var read = await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, _owner.AccessToken);
        Assert.Equivalent(body, await read.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
            Assert.Equal("qbittorrent", (await db.IntegrationConnections.SingleAsync(cancellationToken)).AdapterType));
    }

    [Fact]
    public async Task Create_persists_the_name_trimmed_the_address_normalised_and_the_kind_of_its_type()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "torznab", "  Toloka via Prowlarr  ", "HTTP://127.0.0.1:9696/1/api/", ApiKey: Set(ApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.Include(x => x.PathMappings).SingleAsync(cancellationToken);
            Assert.Equal("Toloka via Prowlarr", row.Name);
            Assert.Equal("TOLOKA VIA PROWLARR", row.NormalizedName);
            Assert.Equal("torznab", row.AdapterType);
            Assert.Equal(IntegrationKind.ReleaseSearch, row.Kind);
            Assert.Equal("http://127.0.0.1:9696/1/api", row.BaseUrl);
            Assert.True(row.IsEnabled);
            Assert.Null(row.UserName);
            Assert.Null(row.PasswordEncrypted);
            Assert.NotNull(row.ApiKeyEncrypted);
            Assert.Empty(row.PathMappings);
            Assert.Equal(now, row.CreatedUtc);
        });
    }

    [Theory]
    [InlineData("http://Media.TEST:8096/", "http://media.test:8096")]
    [InlineData("http://media.test/jellyfin/", "http://media.test/jellyfin")]
    [InlineData("http://127.0.0.1:9696/1/api", "http://127.0.0.1:9696/1/api")]
    // The scheme's own port says nothing.
    [InlineData("https://media.test:443/", "https://media.test")]
    [InlineData("  http://127.0.0.1:9  ", "http://127.0.0.1:9")]
    public async Task Create_normalises_the_address(string typed, string stored)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(FakeIntegrationAdapter.TypeId, "Living room", typed));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(stored, body!.BaseUrl);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(stored, (await db.IntegrationConnections.SingleAsync(cancellationToken)).BaseUrl));
    }

    [Fact]
    public async Task Create_stores_each_secret_encrypted_for_its_integration_and_field()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string password = "fake-service-password";
        var encryption = _factory.Resolve<IEncryptionService>();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            FakeIntegrationAdapter.TypeId, "Living room", Address, "admin", Password: Set(password), ApiKey: Set(ApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            var passwordPurpose = IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.Password);
            var apiKeyPurpose = IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.ApiKey);

            Assert.StartsWith("v1:", row.PasswordEncrypted, StringComparison.Ordinal);
            Assert.DoesNotContain(password, row.PasswordEncrypted!, StringComparison.Ordinal);
            Assert.True(encryption.TryDecrypt(row.PasswordEncrypted!, passwordPurpose, out var storedPassword));
            Assert.Equal(password, storedPassword);
            // Sealed for its field: the other column's purpose can't open it.
            Assert.False(encryption.TryDecrypt(row.PasswordEncrypted!, apiKeyPurpose, out _));

            Assert.StartsWith("v1:", row.ApiKeyEncrypted, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, row.ApiKeyEncrypted!, StringComparison.Ordinal);
            Assert.True(encryption.TryDecrypt(row.ApiKeyEncrypted!, apiKeyPurpose, out var storedApiKey));
            Assert.Equal(ApiKey, storedApiKey);
            Assert.False(encryption.TryDecrypt(row.ApiKeyEncrypted!, passwordPurpose, out _));
        });
    }

    [Fact]
    public async Task Create_answers_has_flags_and_no_secret()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string password = "fake-service-password";

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            FakeIntegrationAdapter.TypeId, "Living room", Address, "admin", Password: Set(password), ApiKey: Set(ApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"hasPassword\":true", raw, StringComparison.Ordinal);
        Assert.Contains("\"hasApiKey\":true", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(password, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, raw, StringComparison.Ordinal);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.DoesNotContain(row.PasswordEncrypted!, raw, StringComparison.Ordinal);
            Assert.DoesNotContain(row.ApiKeyEncrypted!, raw, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Create_records_an_integration_created_audit_row_with_the_owner_and_the_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("jellyfin", "Living room", Address, ApiKey: Set(ApiKey)));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.IntegrationCreated, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.Integration, audit.TargetKind);
            Assert.Equal(body!.Id, audit.TargetId);
            Assert.Equal("Living room", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Create_stores_the_enabled_switch(bool? isEnabled, bool stored)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(FakeIntegrationAdapter.TypeId, "Living room", Address, IsEnabled: isEnabled));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(stored, body!.IsEnabled);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(stored, (await db.IntegrationConnections.SingleAsync(cancellationToken)).IsEnabled));
    }

    [Theory]
    [InlineData("Living room")]
    [InlineData("LIVING ROOM")]
    [InlineData("  living room  ")]
    public async Task Create_with_a_name_that_exists_in_any_letter_case_is_409_and_writes_nothing(string name)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create(name: "Living room");

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("jellyfin", name, Address, ApiKey: Set(ApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"An integration named “{name.Trim()}” already exists.", await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(1, cancellationToken);
    }

    [Fact]
    public async Task Create_twice_at_once_with_one_name_creates_one_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // Both may pass the "name is free" check; the unique index on the normalised name must let only
        // one of them commit.
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(FakeIntegrationAdapter.TypeId, "Twin", Address)),
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(FakeIntegrationAdapter.TypeId, "TWIN", Address)));

        //Assert
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.IntegrationConnections.CountAsync(x => x.NormalizedName == "TWIN", cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // MAM-7 — a blank or missing field reaches the rules and gets their sentence, which the Add
    // integration dialog shows as is; a framework validation body has no "error" for it to show.
    [Theory]
    [InlineData("empty adapter type", "Choose the service to connect to.")]
    [InlineData("whitespace-only adapter type", "Choose the service to connect to.")]
    [InlineData("missing adapter type", "Choose the service to connect to.")]
    [InlineData("empty name", "An integration needs a name.")]
    [InlineData("whitespace-only name", "An integration needs a name.")]
    [InlineData("missing name", "An integration needs a name.")]
    [InlineData("empty address", "An integration needs the address of the service, such as http://192.168.1.10:8096.")]
    [InlineData("whitespace-only address", "An integration needs the address of the service, such as http://192.168.1.10:8096.")]
    [InlineData("missing address", "An integration needs the address of the service, such as http://192.168.1.10:8096.")]
    [InlineData("empty body", "Choose the service to connect to.")]
    public async Task Create_with_a_blank_or_missing_field_is_400_with_the_rule_and_writes_nothing(string blank, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string fake = FakeIntegrationAdapter.TypeId;
        object body = blank switch
        {
            "empty adapter type" => new CreateIntegrationRequest("", "Living room", Address),
            "whitespace-only adapter type" => new CreateIntegrationRequest("   ", "Living room", Address),
            "missing adapter type" => new { name = "Living room", baseUrl = Address },
            "empty name" => new CreateIntegrationRequest(fake, "", Address),
            "whitespace-only name" => new CreateIntegrationRequest(fake, "   ", Address),
            "missing name" => new { adapterType = fake, baseUrl = Address },
            "empty address" => new CreateIntegrationRequest(fake, "Living room", ""),
            "whitespace-only address" => new CreateIntegrationRequest(fake, "Living room", "   "),
            "missing address" => new { adapterType = fake, name = "Living room" },
            "empty body" => new { },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Theory]
    [InlineData("65 characters", "An integration name can be at most 64 characters long.")]
    [InlineData("a line break", "An integration name can't contain line breaks or other control characters.")]
    public async Task Create_with_an_invalid_name_is_400_with_the_rule(string invalid, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = invalid switch
        {
            "65 characters" => new string('n', 65),
            "a line break" => "Living\nroom",
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(FakeIntegrationAdapter.TypeId, name, Address));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Theory]
    [InlineData("ftp://media.test", AddressNotHttpError)]
    [InlineData("192.168.1.10:8096", AddressNotHttpError)]
    [InlineData("media.test/jellyfin", AddressNotHttpError)]
    [InlineData("http://user:secret@media.test", "Leave the user name and password out of the address; they have fields of their own.")]
    [InlineData("http://media.test/?apikey=1", "The address can't have a query (the part from ?). An API key has a field of its own.")]
    [InlineData("http://media.test/#x", "The address can't have a fragment (the part from #).")]
    [InlineData("2049 characters", "The address can be at most 2048 characters long.")]
    public async Task Create_with_an_invalid_address_is_400_with_the_rule_and_writes_nothing(string address, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var baseUrl = address == "2049 characters" ? "http://media.test/" + new string('a', 2049 - "http://media.test/".Length) : address;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(FakeIntegrationAdapter.TypeId, "Living room", baseUrl));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Fact]
    public async Task Create_with_an_address_carrying_an_api_key_answers_without_repeating_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string pastedKey = "pasted-into-the-address-1234";

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "torznab", "Toloka", $"http://127.0.0.1:9696/1/api?apikey={pastedKey}", ApiKey: Set(ApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(pastedKey, raw, StringComparison.Ordinal);
        Assert.Equal(
            "The address can't have a query (the part from ?). An API key has a field of its own.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.DoesNotContain(_factory.Logs.Entries, entry =>
            entry.Message.Contains(pastedKey, StringComparison.Ordinal)
            || entry.Exception?.ToString().Contains(pastedKey, StringComparison.Ordinal) == true);
    }

    [Theory]
    [MemberData(nameof(UnknownAdapterTypes))]
    public async Task Create_with_an_unknown_adapter_type_is_400(string adapterType, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(adapterType, "Living room", Address, ApiKey: Set(ApiKey)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Theory]
    [InlineData("jellyfin", "Jellyfin needs an API key.")]
    [InlineData("torznab", "Torznab needs an API key.")]
    public async Task Create_of_a_type_that_needs_an_api_key_without_one_is_400(string adapterType, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(adapterType, "Living room", Address));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Theory]
    [InlineData("a user name on jellyfin", "Jellyfin doesn't use a user name.")]
    [InlineData("an API key on qbittorrent", "qBittorrent doesn't use an API key.")]
    [InlineData("a password on torznab", "Torznab doesn't use a password.")]
    public async Task Create_with_a_credential_the_type_does_not_use_is_400(string credential, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = credential switch
        {
            "a user name on jellyfin" => new CreateIntegrationRequest("jellyfin", "Living room", Address, "admin", ApiKey: Set(ApiKey)),
            "an API key on qbittorrent" => new CreateIntegrationRequest("qbittorrent", "Downloads", Address, ApiKey: Set(ApiKey)),
            "a password on torznab" => new CreateIntegrationRequest(
                "torznab", "Toloka", Address, Password: Set("torznab-password"), ApiKey: Set(ApiKey)),
            _ => throw new ArgumentOutOfRangeException(nameof(credential), credential, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Theory]
    [InlineData(
        "a user name only",
        "Enter the password that goes with the user name, or leave both empty if qBittorrent lets this app in without signing in.")]
    [InlineData("a password only", "Enter the user name that goes with the password.")]
    public async Task Create_with_only_one_of_user_name_and_password_is_400(string half, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = half == "a user name only"
            ? new CreateIntegrationRequest("qbittorrent", "Downloads", Address, "admin")
            : new CreateIntegrationRequest("qbittorrent", "Downloads", Address, Password: Set("qbittorrent-password"));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Fact]
    public async Task Create_without_credentials_where_they_are_optional_is_201()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // qBittorrent can let the app's address in without signing in.
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("qbittorrent", "Downloads", "http://127.0.0.1:8080"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Null(body!.UserName);
        Assert.False(body.HasPassword);
        Assert.False(body.HasApiKey);
        Assert.Equal(IntegrationKind.DownloadClient, body.Kind);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Null(row.UserName);
            Assert.Null(row.PasswordEncrypted);
            Assert.Null(row.ApiKeyEncrypted);
        });
    }

    [Theory]
    [InlineData("   ", "A new API key can't be blank.")]
    [InlineData("1234567", "An API key must be at least 8 characters long.")]
    [InlineData("abcd efgh", ApiKeyCharactersError)]
    [InlineData("abcd\"efgh", ApiKeyCharactersError)]
    [InlineData("abcd\\efgh", ApiKeyCharactersError)]
    [InlineData("257 characters", "An API key can be at most 256 characters long.")]
    public async Task Create_with_an_invalid_api_key_is_400_with_the_rule(string apiKey, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var value = apiKey == "257 characters" ? new string('k', 257) : apiKey;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("jellyfin", "Living room", Address, ApiKey: Set(value)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Theory]
    [InlineData("", "A new password can't be blank.")]
    [InlineData("pass\nword", "A password can't contain line breaks or other control characters.")]
    [InlineData("257 characters", "A password can be at most 256 characters long.")]
    public async Task Create_with_an_invalid_password_is_400_with_the_rule(string password, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var value = password == "257 characters" ? new string('p', 257) : password;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("qbittorrent", "Downloads", Address, "admin", Password: Set(value)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    [Fact]
    public async Task Create_keeps_the_spaces_of_a_password_and_trims_an_api_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string password = "  spaced password  ";
        var encryption = _factory.Resolve<IEncryptionService>();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            FakeIntegrationAdapter.TypeId, "Living room", Address, "  admin  ", Password: Set(password), ApiKey: Set($"  {ApiKey}\n")));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Equal("admin", row.UserName);
            Assert.True(encryption.TryDecrypt(
                row.PasswordEncrypted!, IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.Password), out var storedPassword));
            Assert.Equal(password, storedPassword);
            Assert.True(encryption.TryDecrypt(
                row.ApiKeyEncrypted!, IntegrationSecretPurpose.For(row.Id, IntegrationSecretField.ApiKey), out var storedApiKey));
            Assert.Equal(ApiKey, storedApiKey);
        });
    }

    [Theory]
    [InlineData(SecretUpsertMode.Keep)]
    [InlineData(SecretUpsertMode.Clear)]
    public async Task Create_with_keep_or_clear_stores_no_secret(SecretUpsertMode mode)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A value only counts with Set; here it is ignored.
        var change = new SecretValueUpsert(mode, "ignored-secret-1234");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            FakeIntegrationAdapter.TypeId, "Living room", Address, Password: change, ApiKey: change));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.False(body!.HasPassword);
        Assert.False(body.HasApiKey);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.SingleAsync(cancellationToken);
            Assert.Null(row.PasswordEncrypted);
            Assert.Null(row.ApiKeyEncrypted);
        });
    }

    [Fact]
    public async Task Create_with_an_undefined_secret_mode_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // A number outside the enum binds as that number, so it reaches the rules.
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new
        {
            adapterType = FakeIntegrationAdapter.TypeId,
            name = "Living room",
            baseUrl = Address,
            apiKey = new { mode = 7, value = ApiKey },
        });

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A secret change must be Keep, Set or Clear.", await response.ReadErrorAsync(cancellationToken));
        await AssertIntegrationCountAsync(0, cancellationToken);
    }

    private static SecretValueUpsert Set(string value) => new(SecretUpsertMode.Set, value);

    // Nothing written: no integration beyond the expected ones, and no audit row.
    private Task AssertIntegrationCountAsync(int expected, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            Assert.Equal(expected, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.PathMappings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
