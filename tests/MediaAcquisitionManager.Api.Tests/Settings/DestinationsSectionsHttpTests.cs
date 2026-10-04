using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-32 — the sections a destination's dialog maps (ADR 0020): every release source's categories, asked
// live from its capabilities, the ids of one name one section — Toloka's real caps list «Мультсеріали» three
// times, as "|-Мультсеріали" — each with the destination it is mapped to. A source that can't be asked
// answers its own sentence beside the others, with the sections mapped of it, which a save would otherwise
// drop unseen. Who may call it is DestinationsAccessHttpTests.
public sealed class DestinationsSectionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations/sections";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationSectionSeeder _sections = null!;
    private AuthorizedUser _owner = null!;
    private FakeTorznabServer _tolokaFeed = null!;
    private FakeTorznabServer _otherFeed = null!;
    private DestinationMappingEntity _cartoons = null!;
    private DestinationMappingEntity _tvShows = null!;

    public DestinationsSectionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _sections = new DestinationSectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _tolokaFeed = new FakeTorznabServer(TolokaAddress, ApiKey);
        _otherFeed = new FakeTorznabServer(OtherAddress, ApiKey);
        FakeTorznabServer.Arm(_factory.TorznabHttp, _tolokaFeed, _otherFeed);
        var destinations = new DestinationMappingSeeder(_factory.Scope);
        var client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            FakeQBittorrentServer.DefaultBaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _cartoons = await destinations.Create(client, "Cartoons", "Cartoons", isDefault: true);
        _tvShows = await destinations.Create(client, "TV Shows", "TV Shows");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sections_lists_the_sources_categories_in_its_order_the_ids_of_one_name_as_one_section()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        // What the real caps list, by id, once each (100137 is listed twice).
        var listedIds = XDocument.Parse(TorznabSamples.Caps()).Descendants()
            .Where(element => element.Name.LocalName is "category" or "subcat")
            .Select(element => (string)element.Attribute("id")!)
            .Distinct()
            .ToList();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(toloka.Id, source.Id);
        Assert.Equal("Toloka", source.Name);
        Assert.Null(source.Error);
        // The standard categories first, as the source lists them, a subcategory under its own name.
        Assert.Equal(
            [
                new DestinationSectionDto("Movies", ["2000"], false, null, null),
                new DestinationSectionDto("Movies/Other", ["2020"], false, null, null),
                new DestinationSectionDto("Movies/HD", ["2040"], false, null, null),
            ],
            source.Sections.Take(3),
            SectionComparer.Instance);
        // The gateway's "|-" mark gone, and the ids a tracker files under one name one section, in the
        // source's order.
        Assert.Equal(["100125", "100044", "100195"], SectionNamed(source, "Мультсеріали").CategoryIds);
        Assert.True(SectionNamed(source, "Мультсеріали").IsSourceSpecific);
        Assert.Equal(["100174"], SectionNamed(source, "Мультсеріали в HD").CategoryIds);
        Assert.Equal(["100137"], SectionNamed(source, "Мультфільми та мультсеріали в DVD").CategoryIds);
        Assert.Equal(["100120"], SectionNamed(source, "DVD українською").CategoryIds);
        Assert.False(SectionNamed(source, "TV/HD").IsSourceSpecific);
        Assert.DoesNotContain(source.Sections, section => section.Name.StartsWith('|'));
        // Every id once, every name once — nothing is mapped, so nothing splits a section.
        Assert.Equal(listedIds.Order(StringComparer.Ordinal), source.Sections.SelectMany(section => section.CategoryIds).Order(StringComparer.Ordinal));
        Assert.Equal(source.Sections.Count, source.Sections.Select(section => section.Name).Distinct().Count());
        Assert.All(source.Sections, section => Assert.Null(section.DestinationId));
        // Asked once, for its capabilities, and nothing written.
        Assert.Equal(["/1/api?t=caps"], FeedRequests());
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Sections_names_the_destination_each_section_is_mapped_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        await _sections.Map(_cartoons, toloka, "Мультсеріали", true, "100125", "100044", "100195");
        await _sections.Map(_tvShows, toloka, "TV/HD", false, "5040");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(
            new DestinationSectionDto("Мультсеріали", ["100125", "100044", "100195"], true, _cartoons.Id, "Cartoons"),
            SectionNamed(source, "Мультсеріали"),
            SectionComparer.Instance);
        Assert.Equal(
            new DestinationSectionDto("TV/HD", ["5040"], false, _tvShows.Id, "TV Shows"),
            SectionNamed(source, "TV/HD"),
            SectionComparer.Instance);
        Assert.Equal(2, source.Sections.Count(section => section.DestinationId is not null));
    }

    [Fact]
    public async Task Sections_lists_the_ids_of_one_name_mapped_apart_as_a_section_each_where_its_first_id_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        // A forum mapped while the tracker had one, and its other two since: mapped to another destination,
        // and to none.
        await _sections.Map(_cartoons, toloka, "Мультсеріали", true, "100125");
        await _sections.Map(_tvShows, toloka, "Мультсеріали", true, "100195");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // What is stored, exactly: one section would have to claim a destination for an id that has none, or
        // one of two destinations for all three.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        var parts = source.Sections.Where(section => section.Name == "Мультсеріали").ToList();
        Assert.Equal(
            [
                new DestinationSectionDto("Мультсеріали", ["100125"], true, _cartoons.Id, "Cartoons"),
                new DestinationSectionDto("Мультсеріали", ["100044"], true, null, null),
                new DestinationSectionDto("Мультсеріали", ["100195"], true, _tvShows.Id, "TV Shows"),
            ],
            parts,
            SectionComparer.Instance);
        // Side by side, where «Мультсеріали» is in the source's list.
        var first = source.Sections.ToList().IndexOf(parts[0]);
        Assert.Equal(parts, source.Sections.Skip(first).Take(3));
    }

    [Fact]
    public async Task Sections_lists_a_mapped_category_the_source_no_longer_lists_after_its_own_by_the_stored_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        await _sections.Map(_cartoons, toloka, "Старі мультсеріали", true, "100998", "100999");
        await _sections.Map(_tvShows, toloka, "Архів", true, "100997");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Kept in sight after the tracker drops it: a save of the destination sends it back rather than
        // losing it, and the owner can take it off.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(
            [
                new DestinationSectionDto("Архів", ["100997"], true, _tvShows.Id, "TV Shows"),
                new DestinationSectionDto("Старі мультсеріали", ["100998", "100999"], true, _cartoons.Id, "Cartoons"),
            ],
            source.Sections.TakeLast(2),
            SectionComparer.Instance);
    }

    [Fact]
    public async Task Sections_of_a_category_the_source_renamed_lists_it_by_its_new_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        await _sections.Map(_cartoons, toloka, "Мультсеріали (старе)", true, "100125", "100044", "100195");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(_cartoons.Id, SectionNamed(source, "Мультсеріали").DestinationId);
        Assert.DoesNotContain(source.Sections, section => section.Name == "Мультсеріали (старе)");
    }

    [Fact]
    public async Task Sections_lists_every_release_source_by_name_and_no_other_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Made out of name order, so the answer's order is the names'.
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        var other = await CreateSourceAsync("other", OtherAddress);
        await _integrations.Create("jellyfin", "Living room", apiKey: "0123456789abcdef0123456789abcdef");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        Assert.Equal([other.Id, toloka.Id], sources.Select(source => source.Id));
        Assert.All(sources, source => Assert.NotEmpty(source.Sections));
        Assert.Equal(["/1/api?t=caps", "/2/api?t=caps"], FeedRequests().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sections_without_a_release_source_is_200_with_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Sections_answers_the_json_shape_the_dialog_reads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        await _sections.Map(_cartoons, toloka, "Мультсеріали", true, "100125", "100044", "100195");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(["sources"], PropertyNames(document.RootElement));
        var source = Assert.Single(document.RootElement.GetProperty("sources").EnumerateArray().ToList());
        Assert.Equal(["id", "name", "sections", "error"], PropertyNames(source));
        var section = source.GetProperty("sections").EnumerateArray().Single(candidate => candidate.GetProperty("name").GetString() == "Мультсеріали");
        Assert.Equal(["name", "categoryIds", "isSourceSpecific", "destinationId", "destinationName"], PropertyNames(section));
        Assert.Equal(_cartoons.Id, section.GetProperty("destinationId").GetGuid());
    }

    [Fact]
    public async Task Sections_of_a_source_that_can_not_be_reached_answers_its_sentence_and_its_mapped_sections_beside_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await CreateSourceAsync("Toloka", TolokaAddress);
        var other = await CreateSourceAsync("Other", OtherAddress);
        await _sections.Map(_cartoons, other, "Мультсеріали", true, "100044", "100125");
        await _sections.Map(_tvShows, other, "TV", false, "5000");
        _otherFeed.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // A live call: 200 whatever the source said, and the one that failed doesn't cost the other its
        // sections.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        var failed = Assert.Single(sources, source => source.Id == other.Id);
        Assert.Equal(
            $"Couldn't reach Torznab at {OtherAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.",
            failed.Error);
        // What is mapped of it, by the stored names, so a save of either destination keeps it.
        Assert.Equal(
            [
                new DestinationSectionDto("TV", ["5000"], false, _tvShows.Id, "TV Shows"),
                new DestinationSectionDto("Мультсеріали", ["100044", "100125"], true, _cartoons.Id, "Cartoons"),
            ],
            failed.Sections,
            SectionComparer.Instance);
        var answered = Assert.Single(sources, source => source.Id == toloka.Id);
        Assert.Null(answered.Error);
        Assert.Contains(answered.Sections, section => section.Name == "Мультсеріали" && section.DestinationId is null);
    }

    [Fact]
    public async Task Sections_masks_the_api_key_in_a_sources_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateSourceAsync("Toloka", TolokaAddress);
        // A feed's own error that repeats the key it was sent: the only way the key could reach the
        // sentence. (Prowlarr's real refusal, an empty 401, carries nothing to mask.)
        _tolokaFeed.Intercept = _ => VendorResponses.Text(
            $"""<?xml version="1.0" encoding="UTF-8"?><error code="100" description="Invalid API key {ApiKey}" />""",
            HttpStatusCode.OK,
            "application/xml");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var source = Assert.Single(JsonSerializer.Deserialize<DestinationSectionListResponse>(text, MediaAcquisitionManagerApiFactory.JsonOptions)!.Sources);
        Assert.Contains("Invalid API key", source.Error, StringComparison.Ordinal);
        Assert.Empty(source.Sections);
        Assert.DoesNotContain(ApiKey, text, StringComparison.Ordinal);
        Assert.All(_factory.Logs.Entries, entry => Assert.DoesNotContain(ApiKey, entry.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("switched off")]
    [InlineData("a type this version can't search through")]
    [InlineData("a key it can't read")]
    public async Task Sections_of_a_source_that_is_not_asked_says_why_beside_its_mapped_sections(string state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = state switch
        {
            "switched off" => await CreateSourceAsync("Toloka", TolokaAddress, isEnabled: false),
            "a type this version can't search through" => await _integrations.Create("gone-type", "Toloka", kind: IntegrationKind.ReleaseSearch),
            "a key it can't read" => await _integrations.CreateWithUnreadableApiKey("torznab", "Toloka"),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
        var expectedError = state switch
        {
            "switched off" => "“Toloka” is switched off. Switch it on under Integrations to read its sections.",
            "a type this version can't search through" => "This version of the app can't search releases through “gone-type”.",
            _ => "The stored API key of “Toloka” can't be read with this installation's encryption key. Enter the API key again and save.",
        };
        await _sections.Map(_cartoons, source, "Мультсеріали", true, "100125");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(source.Id, listed.Id);
        Assert.Equal(expectedError, listed.Error);
        Assert.Equal(
            [new DestinationSectionDto("Мультсеріали", ["100125"], true, _cartoons.Id, "Cartoons")],
            listed.Sections,
            SectionComparer.Instance);
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Sections_of_a_source_whose_answer_the_app_can_not_read_says_so_and_logs_the_type_but_not_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateSourceAsync("Toloka", TolokaAddress);
        await CreateSourceAsync("Other", OtherAddress);
        _tolokaFeed.Intercept = _ => throw new InvalidOperationException($"Unexpected answer quoting {ApiKey}");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // A bug meeting an answer nobody foresaw costs that source its sections, never the page.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        Assert.Equal("The app couldn't read what “Toloka” answered.", Assert.Single(sources, source => source.Name == "Toloka").Error);
        Assert.NotEmpty(Assert.Single(sources, source => source.Name == "Other").Sections);
        var logged = Assert.Single(_factory.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(typeof(InvalidOperationException).FullName!, logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, logged.Message, StringComparison.Ordinal);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<IntegrationConnectionEntity> CreateSourceAsync(string name, string address, bool isEnabled = true) =>
        _integrations.Create("torznab", name, address, apiKey: ApiKey, isEnabled: isEnabled);

    private static async Task<DestinationSectionListResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DestinationSectionListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static DestinationSectionDto SectionNamed(DestinationSectionSourceDto source, string name) =>
        Assert.Single(source.Sections, section => section.Name == name);

    // Each feed request as its path and its t= alone: the key is a credential, not part of what is asked.
    private List<string> FeedRequests() =>
        [.. _factory.TorznabHttp.Requests.Select(request =>
            $"{request.Uri.AbsolutePath}?{request.Uri.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("t=", StringComparison.Ordinal))}")];

    private Task AssertNothingWrittenAsync(CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        Assert.Equal(0, await db.DestinationSections.CountAsync(cancellationToken));
        Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
    });

    private static List<string> PropertyNames(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    /// <summary>Compares sections by value, their id lists by their items in order.</summary>
    private sealed class SectionComparer : IEqualityComparer<DestinationSectionDto>
    {
        public static SectionComparer Instance { get; } = new();

        public bool Equals(DestinationSectionDto? x, DestinationSectionDto? y) =>
            x is not null && y is not null
            && x.Name == y.Name
            && x.CategoryIds.SequenceEqual(y.CategoryIds)
            && x.IsSourceSpecific == y.IsSourceSpecific
            && x.DestinationId == y.DestinationId
            && x.DestinationName == y.DestinationName;

        public int GetHashCode(DestinationSectionDto obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
