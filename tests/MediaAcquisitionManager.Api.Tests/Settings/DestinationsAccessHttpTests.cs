using System.Net;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11 — who may call the Destinations endpoints at all: a signed-in user who owns the installation —
// for the reads too, as a row quotes the client's folders and the owner's path mappings, and a read asks
// the download client with its stored credentials. Every case runs against every endpoint.
public sealed class DestinationsAccessHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private UserEntity _owner = null!;
    private AuthorizedUser _member = null!;

    public DestinationsAccessHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every Destinations endpoint: its method, and its path below <see cref="BaseUrl"/> with
    /// <c>{id}</c> standing for a destination's id and <c>{clientId}</c> for its download client's.</summary>
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "" },
        { "GET", "/{id}" },
        { "GET", "/client-destinations/{clientId}" },
        { "POST", "" },
        { "PUT", "/{id}" },
        { "DELETE", "/{id}" },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await _users.CreateOwner("owner");
        _member = await _users.CreateAuthorized("member");
        // Armed to answer, so a request that got through would show as a call.
        new FakeQBittorrentServer().Arm(_factory.QBittorrentHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_without_a_token_is_401_with_the_sign_in_sentence(string method, string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await CreateDestinationAsync();

        //Act
        var response = await SendAsync(method, path, target, token: null);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_changes_nothing(string method, string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await CreateDestinationAsync();

        //Act
        var response = await SendAsync(method, path, target, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DestinationMappings.SingleAsync(cancellationToken);
            Assert.Equal(target.Id, row.Id);
            Assert.Equal("Movies", row.Name);
            Assert.Equal("Movies", row.ClientDestinationKey);
            Assert.True(row.IsDefault);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        // Not asked either: a read sends the stored credentials to the client.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_as_a_non_owner_with_an_invalid_body_is_403_not_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The owner check runs before the body is bound or validated, so a non-owner never learns from a
        // 400 what a valid request would look like.
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new CreateDestinationRequest("", null, ""));

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

    // The installation's default destination "Movies" on a qBittorrent that has the category.
    private async Task<DestinationMappingEntity> CreateDestinationAsync()
    {
        var client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            FakeQBittorrentServer.DefaultBaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        return await _destinations.Create(client, "Movies", "Movies", isDefault: true);
    }

    // The endpoint's URL with its ids filled in, and the body a real call would send — each one the owner
    // would get a 2xx for — so the only thing wrong with the request is who sends it.
    private Task<HttpResponseMessage> SendAsync(string method, string path, DestinationMappingEntity target, string? token)
    {
        var url = BaseUrl + path
            .Replace("{id}", target.Id.ToString(), StringComparison.Ordinal)
            .Replace("{clientId}", target.DownloadClientConnectionId.ToString(), StringComparison.Ordinal);
        return method switch
        {
            "GET" => _factory.GetHttpRequest(url, token),
            "POST" => _factory.PostHttpRequest(url, token, new CreateDestinationRequest("Cartoons", target.DownloadClientConnectionId, "Cartoons", IsDefault: true)),
            "PUT" => _factory.PutHttpRequest(url, token, new UpdateDestinationRequest("Renamed", target.DownloadClientConnectionId, "TV Shows", IsDefault: false)),
            "DELETE" => _factory.DeleteHttpRequest(url, token),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }
}
