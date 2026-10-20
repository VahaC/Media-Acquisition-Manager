using System.Net;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Who may call the Integrations endpoints at all: a signed-in user who owns the installation — for
// the reads too, as the list names every service, address and account on the owner's network, and a
// test sends stored credentials off the box. Every case runs against every endpoint.
public sealed class IntegrationsAccessHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");
    private static readonly string OwnerOnlyBody = ErrorBody.Json("Only the installation owner can do this.", "account.owner-only");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IIntegrationConnectionSeeder _integrations = null!;
    private UserEntity _owner = null!;
    private AuthorizedUser _member = null!;

    public IntegrationsAccessHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every Integrations endpoint: its method, and its path below <see cref="BaseUrl"/> with
    /// <c>{id}</c> standing for an integration's id.</summary>
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "" },
        { "GET", "/adapter-types" },
        { "GET", "/{id}" },
        { "POST", "" },
        { "PUT", "/{id}" },
        { "DELETE", "/{id}" },
        { "POST", "/{id}/test" },
        { "POST", "/test" },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await _users.CreateOwner("owner");
        _member = await _users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_without_a_token_is_401_with_the_sign_in_sentence(string method, string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await _integrations.Create(name: "Living room", apiKey: "stored-api-key-1234");

        //Act
        var response = await SendAsync(method, path, target.Id, token: null);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_changes_nothing(string method, string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Armed to succeed, so a test that got through would show as a call.
        _factory.FakeAdapter.Outcome = _ => ConnectionTestResult.Succeeded(Message.Legacy("Connected to the fake service."));
        var target = await _integrations.Create(
            name: "Living room",
            userName: "admin",
            password: "stored-password",
            apiKey: "stored-api-key-1234",
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

        //Act
        var response = await SendAsync(method, path, target.Id, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.Include(x => x.PathMappings).SingleAsync(cancellationToken);
            Assert.Equal(target.Id, row.Id);
            Assert.Equal("Living room", row.Name);
            Assert.Equal("http://127.0.0.1:9", row.BaseUrl);
            Assert.True(row.IsEnabled);
            Assert.Equal(target.PasswordEncrypted, row.PasswordEncrypted);
            Assert.Equal(target.ApiKeyEncrypted, row.ApiKeyEncrypted);
            var mapping = Assert.Single(row.PathMappings);
            Assert.Equal(target.PathMappings[0].Id, mapping.Id);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.FakeAdapter.Calls);
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task Create_as_a_non_owner_with_an_invalid_body_is_403_not_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The owner check runs before the body is bound or validated, so a non-owner never learns
        // from a 400 what a valid request would look like.
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new CreateIntegrationRequest("", "", "ftp://x"));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task List_as_a_non_owner_logs_a_warning_naming_the_user()
    {
        //Arrange

        //Act
        await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        var entry = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(InstallationOwnerFilter).FullName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(_member.User.Id.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_as_the_owner_passes_the_gate_without_a_warning()
    {
        //Arrange
        var owner = await _users.CreateSession(_owner);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(InstallationOwnerFilter).FullName);
    }

    // The endpoint's URL with {id} filled in, and the body a real call would send — each one the owner
    // would get a 2xx for — so the only thing wrong with the request is who sends it.
    private Task<HttpResponseMessage> SendAsync(string method, string path, Guid id, string? token)
    {
        var url = BaseUrl + path.Replace("{id}", id.ToString(), StringComparison.Ordinal);
        return (method, path) switch
        {
            ("GET", _) => _factory.GetHttpRequest(url, token),
            ("POST", "") => _factory.PostHttpRequest(url, token, new CreateIntegrationRequest(
                "jellyfin", "Newcomer", "http://127.0.0.1:9", ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "new-api-key-1234"))),
            ("PUT", _) => _factory.PutHttpRequest(url, token, new UpdateIntegrationRequest(
                "Renamed",
                "http://127.0.0.1:10",
                ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "replacement-key-1234"),
                IsEnabled: false,
                PathMappings: [])),
            ("POST", "/test") => _factory.PostHttpRequest(url, token, new TestIntegrationRequest(
                null, "jellyfin", "http://127.0.0.1:9", ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "draft-api-key-1234"))),
            ("POST", _) => _factory.PostHttpRequest(url, token),
            ("DELETE", _) => _factory.DeleteHttpRequest(url, token),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }
}
