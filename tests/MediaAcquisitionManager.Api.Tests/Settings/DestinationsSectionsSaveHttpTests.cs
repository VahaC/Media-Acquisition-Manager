using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-32 — a destination's save maps release sources' sections to it (ADR 0020): in the same save as the
// rest, with the one audit row the save writes. An update replaces the destination's sections, a section
// mapped to another destination moves here, and one left out of the request keeps them; every section is
// checked by the rules, its source must be a release source the app has, and the source is never asked.
public sealed class DestinationsSectionsSaveHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    // Spelled out rather than read from the product code: these are what the destination dialog shows.
    private const string SourceGoneError = "A release source you picked sections of no longer exists. Reload the page and pick them again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IDestinationSectionSeeder _sections = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _client = null!;
    private IntegrationConnectionEntity _toloka = null!;

    public DestinationsSectionsSaveHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> SectionsThatBreakARule => new()
    {
        { "no section", "Pick the release source of each section." },
        { "no source", "Pick the release source of each section." },
        { "no name", "A section needs its name." },
        { "a blank name", "A section needs its name." },
        { "a name with a line break", "A section's name can't contain line breaks or other control characters." },
        { "no category ids", "A section needs at least one category of its release source." },
        { "an empty category list", "A section needs at least one category of its release source." },
        { "a missing category id", "“” is not a category id of a release source." },
        { "a blank category id", "“ ” is not a category id of a release source." },
        { "a category id with a comma", "“5040,100125” is not a category id of a release source." },
        { "a category id with a space", "“100 125” is not a category id of a release source." },
        // 33 characters, one past the column, echoed cut to 32.
        { "a category id too long", "“12345678901234567890123456789012…” is not a category id of a release source." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _sections = new DestinationSectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        // Armed, so a source the save asked would be recorded.
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_with_sections_maps_them_to_it_in_the_same_save_with_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var request = new CreateDestinationRequest(
            "Cartoons",
            _client.Id,
            "Cartoons",
            Sections:
            [
                new DestinationSectionRequest(_toloka.Id, ["100125", "100044", "100195"], "Мультсеріали", IsSourceSpecific: true),
                new DestinationSectionRequest(_toloka.Id, ["5040"], "TV/HD"),
            ]);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [
                (created!.Id, "100044", "Мультсеріали", true),
                (created.Id, "100125", "Мультсеріали", true),
                (created.Id, "100195", "Мультсеріали", true),
                (created.Id, "5040", "TV/HD", false),
            ],
            await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.All(await db.DestinationSections.ToListAsync(cancellationToken), row =>
            {
                Assert.Equal(_toloka.Id, row.ReleaseSourceConnectionId);
                Assert.Equal(now, row.CreatedUtc);
            });
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationCreated, audit.Action);
            Assert.Equal(created.Id, audit.TargetId);
        });
        // Taken as sent: the source isn't asked whether it has them.
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Create_with_a_section_mapped_to_another_destination_moves_its_row_here()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var tvShows = await _destinations.Create(_client, "TV Shows", "TV Shows", isDefault: true);
        var moved = (await _sections.Map(tvShows, _toloka, "Мультсеріали", true, "100125", "100044"))[0];

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new CreateDestinationRequest("Cartoons", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        // One section goes to one destination: 100125 is the new one's now, the same row, and the other id
        // stays where it was.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [(tvShows.Id, "100044", "Мультсеріали", true), (created!.Id, "100125", "Мультсеріали", true)],
            await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DestinationSections.SingleAsync(section => section.CategoryId == "100125", cancellationToken);
            Assert.Equal(moved.Id, row.Id);
            Assert.Equal(AuditAction.DestinationCreated, (await db.AuditEvents.SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task Update_replaces_the_destinations_sections_with_the_ones_it_sends_keeping_the_rows_it_keeps()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        var kept = await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125", "100044");
        await _sections.Map(cartoons, _toloka, "Мультфільми", true, "100019");
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest(
                "Cartoons",
                _client.Id,
                "Cartoons",
                Sections: [new DestinationSectionRequest(_toloka.Id, ["100125", "100044", "100195"], "Мультсеріали", true)]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            [
                (cartoons.Id, "100044", "Мультсеріали", true),
                (cartoons.Id, "100125", "Мультсеріали", true),
                (cartoons.Id, "100195", "Мультсеріали", true),
            ],
            await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // The ones it had are the same rows; the new one is new.
            var rows = await db.DestinationSections.ToDictionaryAsync(row => row.CategoryId, cancellationToken);
            Assert.Equal(kept[0].Id, rows["100125"].Id);
            Assert.Equal(kept[1].Id, rows["100044"].Id);
            Assert.Equal(kept[0].CreatedUtc, rows["100125"].CreatedUtc);
            Assert.Equal(now, rows["100195"].CreatedUtc);
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationUpdated, audit.Action);
            Assert.Equal(cartoons.Id, audit.TargetId);
        });
        // The category is the same, so the client wasn't asked; nor was the source.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Update_with_a_section_of_another_destination_moves_it_and_leaves_that_ones_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        var tvShows = await _destinations.Create(_client, "TV Shows", "TV Shows");
        await _sections.Map(tvShows, _toloka, "Телесеріали", true, "100124", "100032");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Cartoons", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100124"], "Телесеріали", true)]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            [(tvShows.Id, "100032", "Телесеріали", true), (cartoons.Id, "100124", "Телесеріали", true)],
            await StoredSectionsAsync(cancellationToken));
        // The one save's one row, for the destination the owner saved.
        await _factory.WithDbAsync(async db => Assert.Equal(cartoons.Id, (await db.AuditEvents.SingleAsync(cancellationToken)).TargetId));
    }

    [Fact]
    public async Task Update_without_sections_keeps_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125");

        //Act
        // A client that doesn't know the field can't take the sections away.
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("Мультики", _client.Id, "Cartoons"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(cartoons.Id, "100125", "Мультсеріали", true)], await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal("Мультики", (await db.DestinationMappings.SingleAsync(cancellationToken)).Name));
    }

    [Fact]
    public async Task Update_with_no_sections_takes_every_one_of_its_own_off_and_no_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        var tvShows = await _destinations.Create(_client, "TV Shows", "TV Shows");
        await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125", "100044");
        await _sections.Map(tvShows, _toloka, "Телесеріали", true, "100124");

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("Cartoons", _client.Id, "Cartoons", Sections: []));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(tvShows.Id, "100124", "Телесеріали", true)], await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(AuditAction.DestinationUpdated, (await db.AuditEvents.SingleAsync(cancellationToken)).Action));
    }

    [Fact]
    public async Task Update_with_the_sections_it_has_changes_nothing_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125", "100044");
        await _sections.Map(cartoons, _toloka, "TV/HD", false, "5040");
        var before = await StoredSectionsAsync(cancellationToken);

        //Act
        // In another order, a name with spaces around it the save trims away.
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest(
                "Cartoons",
                _client.Id,
                "Cartoons",
                Sections:
                [
                    new DestinationSectionRequest(_toloka.Id, ["5040"], " TV/HD "),
                    new DestinationSectionRequest(_toloka.Id, ["100044", "100125"], "Мультсеріали", true),
                ]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before, await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_with_a_section_the_source_renamed_stores_the_new_name_on_the_same_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        var rows = await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Cartoons", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали українською", true)]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(cartoons.Id, "100125", "Мультсеріали українською", true)], await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(rows[0].Id, (await db.DestinationSections.SingleAsync(cancellationToken)).Id);
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("switched off")]
    public async Task Update_of_the_sections_alone_saves_without_asking_the_client_or_the_source(string state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await _integrations.Create(
            "qbittorrent",
            "Seedbox",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: state != "switched off");
        _qbittorrent.Unreachable = state == "unreachable";
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons", isDefault: true);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Cartoons", client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        // Sections don't depend on what the client has (decision 3's rule for a new name).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(cartoons.Id, "100125", "Мультсеріали", true)], await StoredSectionsAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Create_with_a_section_of_a_switched_off_source_maps_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var resting = await _integrations.Create("torznab", "Resting", "http://prowlarr.test:9696/2/api", apiKey: ApiKey, isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new CreateDestinationRequest("Cartoons", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(resting.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        // The owner may map a source's sections before switching it on.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(resting.Id, (await db.DestinationSections.SingleAsync(cancellationToken)).ReleaseSourceConnectionId));
    }

    [Fact]
    public async Task Create_with_a_category_picked_twice_maps_it_once_by_the_first_section()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new CreateDestinationRequest(
                "Cartoons",
                _client.Id,
                "Cartoons",
                Sections:
                [
                    new DestinationSectionRequest(_toloka.Id, ["100125", "100125"], "Мультсеріали", true),
                    new DestinationSectionRequest(_toloka.Id, ["100125", "100174"], "Мультсеріали в HD", true),
                ]));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [(created!.Id, "100125", "Мультсеріали", true), (created.Id, "100174", "Мультсеріали в HD", true)],
            await StoredSectionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_with_a_section_name_longer_than_its_column_stores_it_cut()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = new string('я', 250);

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new CreateDestinationRequest("Cartoons", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], name, true)]));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(new string('я', DestinationSectionEntity.NameMaximumLength), (await db.DestinationSections.SingleAsync(cancellationToken)).Name));
    }

    [Theory]
    [MemberData(nameof(SectionsThatBreakARule))]
    public async Task Create_with_a_section_that_breaks_a_rule_is_400_with_the_rule_and_asks_and_writes_nothing(string section, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var body = new { name = "Cartoons", downloadClientId = _client.Id, clientDestinationKey = "Cartoons", sections = new[] { SectionBody(section) } };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
        // The rules come before the client is asked.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(SectionsThatBreakARule))]
    public async Task Update_with_a_section_that_breaks_a_rule_is_400_with_the_rule_and_changes_nothing(string section, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125");
        var body = new { name = "Мультики", downloadClientId = _client.Id, clientDestinationKey = "Cartoons", sections = new[] { SectionBody(section) } };

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([(cartoons.Id, "100125", "Мультсеріали", true)], await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("Cartoons", (await db.DestinationMappings.SingleAsync(cancellationToken)).Name);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("", "Cartoons", "A destination needs a name.")]
    // MAM-31 — the key is read once the client is found, still before the sections.
    [InlineData("Cartoons", "", "Pick the category of “qBittorrent” this destination saves to.")]
    public async Task Create_checks_the_destinations_own_fields_before_its_sections(string name, string key, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var body = new { name, downloadClientId = _client.Id, clientDestinationKey = key, sections = new[] { SectionBody("no name") } };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("no integration")]
    [InlineData("a download client")]
    [InlineData("a media library")]
    public async Task Create_with_a_section_of_what_is_not_a_release_source_is_404_with_the_sentence_and_asks_and_writes_nothing(string source)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sourceId = source switch
        {
            "no integration" => Guid.NewGuid(),
            "a download client" => _client.Id,
            "a media library" => (await _integrations.Create("jellyfin", "Living room", apiKey: "0123456789abcdef0123456789abcdef")).Id,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new CreateDestinationRequest(
                "Cartoons",
                _client.Id,
                "Cartoons",
                Sections: [new DestinationSectionRequest(_toloka.Id, ["100044"], "Мультсеріали", true), new DestinationSectionRequest(sourceId, ["100125"], "Мультсеріали", true)]));

        //Assert
        // A source id in the body, not the addressed row: the dialog gets the sentence. Checked before the
        // client is asked about its category.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SourceGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Update_with_a_section_of_what_is_not_a_release_source_is_404_with_the_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Мультики", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(Guid.NewGuid(), ["100125"], "Мультсеріали", true)]));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SourceGoneError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([(cartoons.Id, "100125", "Мультсеріали", true)], await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("Cartoons", (await db.DestinationMappings.SingleAsync(cancellationToken)).Name);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_with_sections_the_client_refuses_writes_no_section()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new CreateDestinationRequest("Cartoons 4K", _client.Id, "Cartoons 4K", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“qBittorrent” has no category “Cartoons 4K”. Create it in the client first, or pick one it has.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Update_moving_a_section_another_request_maps_just_before_the_save_still_takes_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        var tvShows = await _destinations.Create(_client, "TV Shows", "TV Shows");
        // Deterministic race: this save passed every check, and another request maps the section it picks to
        // TV Shows just before its transaction begins. The sections are read under the transaction's lock, so
        // that row is found and moved, not inserted a second time.
        IReadOnlyList<DestinationSectionEntity>? competing = null;
        _factory.TransactionHook.BeforeNextTransaction(
            IsEditingADestination,
            async _ => competing = await InAnotherRequestAsync(services => new DestinationSectionSeeder(services).Map(tvShows, _toloka, "Мультсеріали", true, "100125")));

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Мультики", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        // The last save wins, whole, and the unique index never sees two rows of one section: the other
        // request's row is the one moved here.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(competing);
        Assert.Equal([(cartoons.Id, "100125", "Мультсеріали", true)], await StoredSectionsAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(competing[0].Id, (await db.DestinationSections.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Update_with_a_section_whose_source_is_removed_before_the_save_is_404_with_the_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        // The source was there when checked, and another request removes it just before the save's
        // transaction begins: the section's key breaks, not a 500.
        _factory.TransactionHook.BeforeNextTransaction(IsEditingADestination, token => RemoveIntegrationAsync(_toloka.Id, token));

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Мультики", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SourceGoneError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("Cartoons", (await db.DestinationMappings.SingleAsync(cancellationToken)).Name);
            Assert.Equal(0, await db.DestinationSections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_with_sections_of_a_destination_removed_before_the_save_is_a_bare_404_and_writes_no_section()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        _factory.TransactionHook.BeforeNextTransaction(
            IsEditingADestination,
            token => InAnotherRequestAsync(services => services.GetRequiredService<ApplicationDbContext>().DestinationMappings
                .Where(destination => destination.Id == cartoons.Id)
                .ExecuteDeleteAsync(token)));

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}",
            _owner.AccessToken,
            new UpdateDestinationRequest("Мультики", _client.Id, "Cartoons", Sections: [new DestinationSectionRequest(_toloka.Id, ["100125"], "Мультсеріали", true)]));

        //Assert
        // Gone either way, whichever of its writes the database refused first.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Delete_of_a_destination_removes_its_sections_and_no_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons", isDefault: true);
        var tvShows = await _destinations.Create(_client, "TV Shows", "TV Shows");
        await _sections.Map(cartoons, _toloka, "Мультсеріали", true, "100125");
        await _sections.Map(tvShows, _toloka, "Телесеріали", true, "100124");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{cartoons.Id}", _owner.AccessToken);

        //Assert
        // Its releases go to the default again, or wherever the owner maps the section next.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(tvShows.Id, "100124", "Телесеріали", true)], await StoredSectionsAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The body of one section that breaks the rule <paramref name="section"/> names.</summary>
    private object? SectionBody(string section) => section switch
    {
        "no section" => null,
        "no source" => new { categoryIds = new[] { "100125" }, name = "Мультсеріали" },
        "no name" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { "100125" } },
        "a blank name" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { "100125" }, name = "  " },
        "a name with a line break" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { "100125" }, name = "Мульт\nсеріали" },
        "no category ids" => new { releaseSourceId = _toloka.Id, name = "Мультсеріали" },
        "an empty category list" => new { releaseSourceId = _toloka.Id, categoryIds = Array.Empty<string>(), name = "Мультсеріали" },
        "a missing category id" => new { releaseSourceId = _toloka.Id, categoryIds = new string?[] { "100044", null }, name = "Мультсеріали" },
        "a blank category id" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { " " }, name = "Мультсеріали" },
        "a category id with a comma" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { "5040,100125" }, name = "Мультсеріали" },
        "a category id with a space" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { "100 125" }, name = "Мультсеріали" },
        "a category id too long" => new { releaseSourceId = _toloka.Id, categoryIds = new[] { "123456789012345678901234567890123" }, name = "Мультсеріали" },
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    // A save of a destination, or the transaction around it: the destination changed or added in its context.
    private static bool IsEditingADestination(DbContext context) =>
        context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State is EntityState.Modified or EntityState.Added);

    // What another request does: through a scope and a context of its own.
    private async Task<T> InAnotherRequestAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> RemoveIntegrationAsync(Guid integrationId, CancellationToken cancellationToken) =>
        InAnotherRequestAsync(services => services.GetRequiredService<ApplicationDbContext>().IntegrationConnections
            .Where(integration => integration.Id == integrationId)
            .ExecuteDeleteAsync(cancellationToken));

    /// <summary>Every stored section as (destination, category, name, own section), by category.</summary>
    private async Task<List<(Guid DestinationId, string CategoryId, string Name, bool IsSourceSpecific)>> StoredSectionsAsync(CancellationToken cancellationToken)
    {
        List<(Guid, string, string, bool)> stored = [];
        await _factory.WithDbAsync(async db =>
            stored = [.. (await db.DestinationSections.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(row => row.CategoryId, StringComparer.Ordinal)
                .Select(row => (row.DestinationMappingId, row.CategoryId, row.Name, row.IsSourceSpecific))]);
        return stored;
    }

    private Task AssertNothingWrittenAsync(CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        Assert.Equal(0, await db.DestinationSections.CountAsync(cancellationToken));
        Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
    });
}
