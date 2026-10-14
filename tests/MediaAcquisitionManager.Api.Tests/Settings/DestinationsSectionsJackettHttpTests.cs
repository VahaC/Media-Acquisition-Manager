using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-30 — the sections a destination's dialog maps (ADR 0020) when Jackett serves the feed (Vasyl's decision 5): its
// real caps carry Toloka's subsection mark "|-" on the same ids as Prowlarr's, so the sections read the same — the ids
// of one name one section, «Мультсеріали» once — and Jackett's 20 more, "Other" among them, are sections too. A key
// Jackett refuses is the source's own sentence, with where to copy the right one.
public sealed class DestinationsSectionsJackettHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations/sections";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationSectionSeeder _sections = null!;
    private AuthorizedUser _owner = null!;
    private DestinationMappingEntity _cartoons = null!;

    public DestinationsSectionsJackettHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _sections = new DestinationSectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        FakeTorznabServer.Jackett(JackettAddress, ApiKey).Arm(_factory.TorznabHttp);
        var client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            FakeQBittorrentServer.DefaultBaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _cartoons = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Cartoons", "Cartoons", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sections_lists_jacketts_categories_as_prowlarrs_the_ids_of_one_name_as_one_section()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        // What Jackett's real caps list, by id, once each (100137 is listed twice, as in Prowlarr's).
        var listedIds = XDocument.Parse(TorznabSamples.JackettCaps()).Descendants()
            .Where(element => element.Name.LocalName is "category" or "subcat")
            .Select(element => (string)element.Attribute("id")!)
            .Distinct()
            .ToList();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal((jackett.Id, "Toloka", (string?)null), (source.Id, source.Name, source.Error));
        Assert.Equal(["Movies", "Movies/Other", "Movies/HD"], source.Sections.Take(3).Select(section => section.Name));
        // The "|-" mark gone, the ids Toloka files under one name one section, in the source's order: as Prowlarr's.
        Assert.Equal(["100125", "100044", "100195"], SectionNamed(source, "Мультсеріали").CategoryIds);
        Assert.True(SectionNamed(source, "Мультсеріали").IsSourceSpecific);
        Assert.Equal(["100174"], SectionNamed(source, "Мультсеріали в HD").CategoryIds);
        Assert.DoesNotContain(source.Sections, section => section.Name.StartsWith('|'));
        // Jackett's own extra category, which no search asks, is a section all the same.
        Assert.Equal(["8000"], SectionNamed(source, "Other").CategoryIds);
        Assert.False(SectionNamed(source, "Other").IsSourceSpecific);
        Assert.Equal(listedIds.Order(StringComparer.Ordinal), source.Sections.SelectMany(section => section.CategoryIds).Order(StringComparer.Ordinal));
        Assert.Equal(source.Sections.Count, source.Sections.Select(section => section.Name).Distinct().Count());
        // Asked once, for its capabilities, at the address Jackett gives with /api.
        var request = Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Equal($"{JackettAddress}api?t=caps&apikey={ApiKey}", request.Uri.AbsoluteUri);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.DestinationSections.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sections_names_the_destination_a_section_of_jacketts_is_mapped_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        await _sections.Map(_cartoons, jackett, "Мультсеріали", true, "100125", "100044", "100195");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var section = SectionNamed(Assert.Single((await ReadAsync(response, cancellationToken)).Sources), "Мультсеріали");
        Assert.Equal((_cartoons.Id, "Cartoons"), (section.DestinationId, section.DestinationName));
        Assert.Equal(["100125", "100044", "100195"], section.CategoryIds);
    }

    [Fact]
    public async Task Sections_of_a_key_jackett_refuses_says_so_with_where_to_copy_the_right_one_and_keeps_the_mapped_sections()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: "a-key-jackett-refuses");
        await _sections.Map(_cartoons, jackett, "Мультсеріали", true, "100125", "100044", "100195");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Jackett's recorded answer to a wrong key on caps: HTTP 200 and error 100.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(
            "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.",
            source.Error);
        Assert.Equal((_cartoons.Id, "Мультсеріали"), (Assert.Single(source.Sections).DestinationId, source.Sections[0].Name));
        Assert.DoesNotContain("a-key-jackett-refuses", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static async Task<DestinationSectionListResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DestinationSectionListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static DestinationSectionDto SectionNamed(DestinationSectionSourceDto source, string name) =>
        Assert.Single(source.Sections, section => section.Name == name);
}
