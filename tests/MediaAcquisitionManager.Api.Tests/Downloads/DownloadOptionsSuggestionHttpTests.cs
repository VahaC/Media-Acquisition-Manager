using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-32 — the destination the Download dialog picks first (ADR 0020): the one the release's tracker
// section is mapped to, by the categories the release was stored with and its source; the source's own
// section wins over a standard category, two destinations at the rank that decides are no answer, and with
// no section of it mapped to a destination the dialog offers, the installation's default, as before. For
// every signed-in user, read from the database alone. The rest of the dialog's choices are
// DownloadOptionsHttpTests.
public sealed class DownloadOptionsSuggestionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string SeedboxAddress = "http://seedbox.test:8080";

    // A cartoon series as Toloka lists it through Prowlarr: the standard TV/HD and its own «Мультсеріали».
    private const string CartoonSeriesCategories = "5040,100125";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IDestinationSectionSeeder _sections = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private DestinationMappingEntity _cartoons = null!;
    private DestinationMappingEntity _tvShows = null!;

    public DownloadOptionsSuggestionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _sections = new DestinationSectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Armed, so a feed or a client the options asked would be recorded.
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        new FakeQBittorrentServer().Arm(_factory.QBittorrentHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _client = await CreateClientAsync("qBittorrent", FakeQBittorrentServer.DefaultBaseUrl);
        _movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
        _cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons");
        _tvShows = await _destinations.Create(_client, "TV Shows", "TV Shows");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Options_suggests_the_destination_the_releases_own_section_is_mapped_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _sections.Map(_cartoons, _toloka, "Мультсеріали", true, "100125", "100044", "100195");
        var release = await _releases.CreateFound(_member.User, _toloka, "Mickey Mouse Clubhouse (S1-4) (2006-2016)", categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(_cartoons.Id, body.SuggestedDestinationId);
        // One of the destinations the dialog offers, by its id.
        Assert.Contains(body.Clients.SelectMany(client => client.Destinations), destination => destination.Id == body.SuggestedDestinationId);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Fact]
    public async Task Options_prefers_the_sources_own_section_to_a_standard_category()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A cartoon series is filed under TV like any show; «Мультсеріали» says more.
        await _sections.Map(_tvShows, _toloka, "TV/HD", false, "5040");
        await _sections.Map(_cartoons, _toloka, "Мультсеріали", true, "100125");
        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_cartoons.Id, (await ReadAsync(response, cancellationToken)).SuggestedDestinationId);
    }

    [Fact]
    public async Task Options_suggests_the_standard_categorys_destination_when_the_sources_own_section_is_not_mapped()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _sections.Map(_tvShows, _toloka, "TV/HD", false, "5040");
        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_tvShows.Id, (await ReadAsync(response, cancellationToken)).SuggestedDestinationId);
    }

    [Fact]
    public async Task Options_with_two_sections_of_the_release_mapped_to_one_destination_suggests_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _sections.Map(_cartoons, _toloka, "Мультсеріали", true, "100125");
        await _sections.Map(_cartoons, _toloka, "Мультсеріали в HD", true, "100174");
        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: "5040,100125,100174");

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_cartoons.Id, (await ReadAsync(response, cancellationToken)).SuggestedDestinationId);
    }

    [Theory]
    [InlineData("the source's own sections")]
    [InlineData("the standard categories")]
    public async Task Options_with_two_destinations_at_the_rank_that_decides_suggests_none(string rank)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string categoryIds;
        if (rank == "the source's own sections")
        {
            await _sections.Map(_cartoons, _toloka, "Мультсеріали", true, "100125");
            await _sections.Map(_tvShows, _toloka, "Серіали в HD", true, "100173");
            // Mapped at the rank below, which never breaks the tie.
            await _sections.Map(_movies, _toloka, "TV/HD", false, "5040");
            categoryIds = "5040,100125,100173";
        }
        else
        {
            await _sections.Map(_cartoons, _toloka, "TV", false, "5000");
            await _sections.Map(_tvShows, _toloka, "TV/HD", false, "5040");
            categoryIds = "5000,5040,100125";
        }

        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: categoryIds);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        // The dialog leaves the pick to the person rather than guess — not even the default.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Null(body.SuggestedDestinationId);
        Assert.Equal(3, Assert.Single(body.Clients).Destinations.Count);
    }

    [Theory]
    [InlineData("no categories")]
    [InlineData("categories nobody mapped")]
    [InlineData("its categories mapped of another source")]
    public async Task Options_with_no_section_of_the_release_mapped_suggests_the_default(string release)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var other = await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        await _sections.Map(_cartoons, other, "Мультсеріали", true, "100125");
        await _sections.Map(_tvShows, other, "TV/HD", false, "5040");
        await _sections.Map(_tvShows, _toloka, "Телесеріали", true, "100124");
        var found = release switch
        {
            // A release stored before MAM-32 kept no categories.
            "no categories" => await _releases.CreateFound(_member.User, _toloka),
            "categories nobody mapped" => await _releases.CreateFound(_member.User, _toloka, categoryIds: "2040,100096"),
            // One source's ids say nothing about another's.
            "its categories mapped of another source" => await _releases.CreateFound(_member.User, _toloka, categoryIds: CartoonSeriesCategories),
            _ => throw new ArgumentOutOfRangeException(nameof(release), release, null),
        };

        //Act
        var response = await GetOptionsAsync(found.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_movies.Id, (await ReadAsync(response, cancellationToken)).SuggestedDestinationId);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Fact]
    public async Task Options_with_no_section_mapped_and_no_default_suggests_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.WithDbAsync(db => db.DestinationMappings.ExecuteUpdateAsync(setters => setters.SetProperty(destination => destination.IsDefault, false), cancellationToken));
        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadAsync(response, cancellationToken)).SuggestedDestinationId);
    }

    [Theory]
    [InlineData("a switched-off client")]
    [InlineData("a client of a type this version can't send to")]
    public async Task Options_with_the_section_mapped_to_a_destination_it_does_not_offer_suggests_the_default(string client)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seedbox = client == "a switched-off client"
            ? await CreateClientAsync("Seedbox", SeedboxAddress, isEnabled: false)
            : await _integrations.Create("retired-client", "Old client", kind: IntegrationKind.DownloadClient);
        var elsewhere = await _destinations.Create(seedbox, "Cartoons", "Cartoons");
        await _sections.Map(elsewhere, _toloka, "Мультсеріали", true, "100125");
        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        // The dialog couldn't pick it: as good as unmapped.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(_movies.Id, body.SuggestedDestinationId);
        Assert.DoesNotContain(body.Clients.SelectMany(option => option.Destinations), destination => destination.Id == elsewhere.Id);
    }

    [Fact]
    public async Task Options_of_a_release_no_client_can_take_suggests_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _sections.Map(_cartoons, _toloka, "Мультсеріали", true, "100125");
        // qBittorrent can't take an NZB, so the dialog offers nothing: neither the mapped destination nor the
        // default can be picked.
        var release = await _releases.CreateFound(_member.User, _toloka, payloadKind: ReleasePayloadKind.Nzb, categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Empty(body.Clients);
        Assert.Null(body.SuggestedDestinationId);
    }

    [Fact]
    public async Task Options_suggests_the_destination_of_a_section_on_another_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seedbox = await CreateClientAsync("Seedbox", SeedboxAddress);
        var seedboxCartoons = await _destinations.Create(seedbox, "Cartoons", "Cartoons");
        await _sections.Map(seedboxCartoons, _toloka, "Мультсеріали", true, "100125");
        var release = await _releases.CreateFound(_member.User, _toloka, categoryIds: CartoonSeriesCategories);

        //Act
        var response = await GetOptionsAsync(release.Id);

        //Assert
        // The destination names its client: the dialog picks the Seedbox and its Cartoons.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(seedboxCartoons.Id, body.SuggestedDestinationId);
        Assert.Equal(seedbox.Id, Assert.Single(body.Clients, option => option.Destinations.Any(destination => destination.Id == seedboxCartoons.Id)).Id);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, string baseUrl, bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            name,
            baseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: isEnabled,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    private Task<HttpResponseMessage> GetOptionsAsync(Guid releaseId) =>
        _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={releaseId}", _member.AccessToken);

    private static async Task<DownloadOptionsResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>A read of the database alone: no job, audit row or section written, and no feed or client
    /// asked.</summary>
    private async Task AssertNothingWrittenOrAskedAsync(CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.TorznabHttp.Requests);
    }
}
