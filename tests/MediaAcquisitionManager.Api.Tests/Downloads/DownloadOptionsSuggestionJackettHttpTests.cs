using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30 — the destination the Download dialog picks first (ADR 0020) for a release Jackett listed (Vasyl's decision 5):
// Jackett lists Toloka's own sections on the ids Prowlarr does, so a cartoon series a search found through Jackett is
// stored with «Мультсеріали» among its categories, and the destination that section is mapped to is offered first —
// over the standard TV/HD's — as through Prowlarr.
public sealed class DownloadOptionsSuggestionJackettHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string SearchUrl = "/api/searches";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IDestinationSectionSeeder _sections = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _jackett = null!;
    private DestinationMappingEntity _movies = null!;
    private DestinationMappingEntity _cartoons = null!;
    private DestinationMappingEntity _tvShows = null!;

    public DownloadOptionsSuggestionJackettHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var destinations = new DestinationMappingSeeder(_factory.Scope);
        _sections = new DestinationSectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Jackett's real answer to a search for the cartoon series by its Ukrainian title.
        var jackett = FakeTorznabServer.Jackett(JackettAddress, ApiKey);
        jackett.SearchXml = TorznabSamples.Feed();
        jackett.TvSearchXml = TorznabSamples.JackettSearch(TorznabSamples.JackettTvCartoonUkrainianTitleSearchFile, JackettAddress, ApiKey);
        jackett.Arm(_factory.TorznabHttp);
        new FakeQBittorrentServer().Arm(_factory.QBittorrentHttp);
        _jackett = await integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        var client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            FakeQBittorrentServer.DefaultBaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await destinations.Create(client, "Movies", "Movies", isDefault: true);
        _cartoons = await destinations.Create(client, "Cartoons", "Cartoons");
        _tvShows = await destinations.Create(client, "TV Shows", "TV Shows");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    // "(S1-4) (2006-2016) WEB-DL 1080p": TV/HD (5040) and «Мультсеріали в HD» (100174).
    [InlineData("Mickey Mouse Clubhouse (S1-4) (2006-2016) WEB-DL 1080p/DVDRip-AVC Ukr/Eng | sub Ukr (S1-4)")]
    // "(S1) (2006-2007) Satrip": TV (5000) and «Мультсеріали» (100044).
    [InlineData("Mickey Mouse Clubhouse (S1) (2006-2007) Satrip (S1)")]
    public async Task Options_suggests_the_destination_the_section_jackett_listed_the_release_in_is_mapped_to(string title)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _sections.Map(_tvShows, _jackett, "TV/HD", false, "5040");
        await _sections.Map(_tvShows, _jackett, "TV", false, "5000");
        await _sections.Map(_cartoons, _jackett, "Мультсеріали", true, "100125", "100044", "100195");
        await _sections.Map(_cartoons, _jackett, "Мультсеріали в HD", true, "100174");
        var release = await FindAsync(title, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release}", _member.AccessToken);

        //Assert
        // The source's own section wins over the standard category the release is also listed in.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(_cartoons.Id, body.SuggestedDestinationId);
        Assert.Contains(body.Clients.SelectMany(client => client.Destinations), destination => destination.Id == _cartoons.Id);
    }

    [Fact]
    public async Task Options_with_no_section_of_jacketts_mapped_suggests_the_default_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAsync("Mickey Mouse Clubhouse (S1) (2006-2007) Satrip (S1)", cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(_movies.Id, body.SuggestedDestinationId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The member's search for the cartoon series by its Ukrainian title through Jackett, and the id of the release it
    /// stored under <paramref name="title"/>, with the categories Jackett listed it in.
    /// </summary>
    private async Task<Guid> FindAsync(string title, CancellationToken cancellationToken)
    {
        var created = await _factory.PostHttpRequest(SearchUrl, _member.AccessToken, new SearchRequest("Клуб Міккі Мауса"));
        if (created.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync(cancellationToken)}");
        var search = (await created.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var listed = await _factory.GetHttpRequest($"{SearchUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);
        var releases = (await listed.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
        var id = releases.First(release => release.Title == title).Id;
        await _factory.WithDbAsync(async db =>
        {
            // Stored as Jackett listed it: a standard TV category and Toloka's own section.
            var stored = await db.Releases.AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
            if (stored.CategoryIds is not ("5040,100174" or "5000,100044"))
                throw new InvalidOperationException($"The release was stored in “{stored.CategoryIds}”.");
        });
        return id;
    }
}
