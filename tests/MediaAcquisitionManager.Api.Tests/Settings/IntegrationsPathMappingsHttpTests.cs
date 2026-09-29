using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// An integration's path mappings (ADR 0003), saved with the dialog's one Save: stored normalised in
// the order typed, replaced as a whole, and refused at save when they break a rule — overlaps that
// disagree above all, as resolving a path must never have to pick one of two answers.
public sealed class IntegrationsPathMappingsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string Address = "http://127.0.0.1:9";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsPathMappingsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>One case per save-time rule after the first (a type that reports no paths has a test
    /// of its own), with the sentence the owner gets. qBittorrent: it reports paths and needs no
    /// credentials, and its name reads naturally in the sentences.</summary>
    public static TheoryData<string, string> InvalidMappings => new()
    {
        { "21 mappings", "An integration can have at most 20 path mappings." },
        { "a blank service path", "Each path mapping needs the folder as qBittorrent sees it." },
        { "a blank app path", "Each path mapping needs the folder as this app sees it." },
        { "a path of 1025 characters", "A path in a path mapping can be at most 1024 characters long." },
        { "a line break", "A path in a path mapping can't contain line breaks or other control characters." },
        { "a relative path", "“downloads” is not a full path. Start it with / (Linux) or a drive letter such as D:\\ (Windows)." },
        { "a dot-dot folder", "“/downloads/../etc” contains . or .. as a folder name. Write the folder's full path without them." },
        {
            "Linux and Windows service paths",
            "All the paths of qBittorrent in one integration must be of one kind: Linux-style (/…) or Windows-style (D:\\…)."
        },
        { "one service path twice", "The path “/downloads” of qBittorrent is mapped twice. Keep one mapping for it." },
        {
            "nested Windows service paths onto Linux paths",
            "“D:\\Torrents\\movies” is inside “D:\\Torrents”. With Windows paths on the qBittorrent side and Linux paths in this app, keep only one of them."
        },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_with_path_mappings_stores_them_normalised_in_the_order_given()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // Not in alphabetical order, and the second app folder on a Windows host.
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "qbittorrent",
            "Downloads",
            Address,
            PathMappings: [new PathMappingRequest(" /downloads/ ", "/media//"), new PathMappingRequest("/archive", "d:/DL/")]));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal([new PathMappingDto("/downloads", "/media"), new PathMappingDto("/archive", @"D:\DL")], body!.PathMappings);
        Assert.Equal(
            [("/downloads", "/media", 0), ("/archive", @"D:\DL", 1)],
            await StoredMappingsAsync(body.Id, cancellationToken));
    }

    [Fact]
    public async Task Update_replaces_every_path_mapping()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            "qbittorrent",
            "Downloads",
            pathMappings: [new PathMappingPair("/downloads", "/media"), new PathMappingPair("/incomplete", "/media/incomplete")]);

        //Act
        // The same service folder with a new app folder: the old row must be gone before the new one
        // arrives, or the unique index on (integration, service folder) refuses the save.
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest(
            "Downloads",
            Address,
            PathMappings: [new PathMappingRequest("/seeding", "/data/seeding"), new PathMappingRequest("/downloads", "/data")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            [("/seeding", "/data/seeding", 0), ("/downloads", "/data", 1)],
            await StoredMappingsAsync(integration.Id, cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_with_an_empty_list_removes_every_path_mapping()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            "qbittorrent",
            "Downloads",
            pathMappings: [new PathMappingPair("/downloads", "/media"), new PathMappingPair("/incomplete", "/media/incomplete")]);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Downloads", Address, PathMappings: []));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await StoredMappingsAsync(integration.Id, cancellationToken));
    }

    [Fact]
    public async Task Update_without_path_mappings_keeps_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            "qbittorrent", "Downloads", pathMappings: [new PathMappingPair("/downloads", "/media")]);

        //Act
        // Left out, not empty: a client that doesn't know the mappings must not delete them.
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{integration.Id}", _owner.AccessToken, new { name = "Renamed", baseUrl = Address });

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var mapping = await db.PathMappings.SingleAsync(cancellationToken);
            Assert.Equal(integration.PathMappings[0].Id, mapping.Id);
            Assert.Equal(("/downloads", "/media"), (mapping.ServicePrefix, mapping.AppPrefix));
        });
    }

    [Fact]
    public async Task Create_with_path_mappings_for_a_type_that_reports_no_paths_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // An indexer's paths are never trusted, so a mapping for one would only look like it does
        // something.
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "torznab",
            "Toloka",
            Address,
            ApiKey: new SecretValueUpsert(SecretUpsertMode.Set, "torznab-api-key-1234"),
            PathMappings: [new PathMappingRequest("/downloads", "/media")]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Torznab reports no file paths, so it takes no path mappings.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [MemberData(nameof(InvalidMappings))]
    public async Task Create_with_an_invalid_path_mapping_is_400_with_the_rule_and_writes_nothing(string invalid, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyList<PathMappingRequest> mappings = invalid switch
        {
            "21 mappings" => [.. Enumerable.Range(1, 21).Select(number => new PathMappingRequest($"/downloads/{number}", $"/media/{number}"))],
            "a blank service path" => [new PathMappingRequest("   ", "/media")],
            "a blank app path" => [new PathMappingRequest("/downloads", "")],
            "a path of 1025 characters" => [new PathMappingRequest("/" + new string('d', 1024), "/media")],
            "a line break" => [new PathMappingRequest("/down\nloads", "/media")],
            "a relative path" => [new PathMappingRequest("downloads", "/media")],
            "a dot-dot folder" => [new PathMappingRequest("/downloads/../etc", "/media")],
            "Linux and Windows service paths" => [new PathMappingRequest("/downloads", "/media"), new PathMappingRequest(@"D:\Downloads", "/media/windows")],
            "one service path twice" => [new PathMappingRequest("/downloads", "/media"), new PathMappingRequest("/downloads/", "/media/other")],
            "nested Windows service paths onto Linux paths" =>
                [new PathMappingRequest(@"D:\Torrents", "/media"), new PathMappingRequest(@"d:\Torrents\movies", "/media/movies")],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateIntegrationRequest("qbittorrent", "Downloads", Address, PathMappings: mappings));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_with_nested_path_mappings_that_disagree_is_400_naming_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // /downloads/movies/x would be /media/movies/x by the first and /films/x by the second; the app
        // never picks the longer prefix for the owner (ADR 0003).
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "qbittorrent",
            "Downloads",
            Address,
            PathMappings: [new PathMappingRequest("/downloads", "/media"), new PathMappingRequest("/downloads/movies", "/films")]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "The mappings for “/downloads” and “/downloads/movies” send “/downloads/movies” to different places: " +
            "“/media/movies” and “/films”. Remove one, or make them agree.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_with_nested_path_mappings_that_agree_is_201()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            "qbittorrent",
            "Downloads",
            Address,
            PathMappings: [new PathMappingRequest("/downloads", "/media"), new PathMappingRequest("/downloads/movies", "/media/movies")]));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [("/downloads", "/media", 0), ("/downloads/movies", "/media/movies", 1)],
            await StoredMappingsAsync(body!.Id, cancellationToken));
    }

    [Fact]
    public async Task Update_with_path_mappings_that_disagree_is_400_and_keeps_the_old_ones()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var integration = await _integrations.Create(
            "qbittorrent", "Downloads", pathMappings: [new PathMappingPair("/downloads", "/media")]);

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, new UpdateIntegrationRequest(
            "Downloads",
            Address,
            PathMappings: [new PathMappingRequest("/downloads", "/data"), new PathMappingRequest("/downloads/movies", "/films")]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "The mappings for “/downloads” and “/downloads/movies” send “/downloads/movies” to different places: " +
            "“/data/movies” and “/films”. Remove one, or make them agree.",
            await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var mapping = await db.PathMappings.SingleAsync(cancellationToken);
            Assert.Equal(integration.PathMappings[0].Id, mapping.Id);
            Assert.Equal(("/downloads", "/media"), (mapping.ServicePrefix, mapping.AppPrefix));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // The integration's stored mappings, in order, with their positions.
    private async Task<List<(string ServicePrefix, string AppPrefix, int Position)>> StoredMappingsAsync(
        Guid integrationId, CancellationToken cancellationToken)
    {
        List<PathMappingEntity> rows = [];
        await _factory.WithDbAsync(async db =>
            rows = await db.PathMappings
                .Where(mapping => mapping.IntegrationConnectionId == integrationId)
                .OrderBy(mapping => mapping.Position)
                .ToListAsync(cancellationToken));
        return [.. rows.Select(mapping => (mapping.ServicePrefix, mapping.AppPrefix, mapping.Position))];
    }

    private Task AssertNothingWrittenAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.PathMappings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
