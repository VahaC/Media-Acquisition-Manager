using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — list_duplicates: the Duplicates page's list (GET /api/library/duplicates) as a tool, for any token of the
// owner or a member, the owner-only sentences shaped for the caller by the service, the same JSON a script reads from
// the HTTP API. Its filters by name — the class, the owner's decision (dismissed groups only on asking) —, Vasyl's
// "most recoverable space first" sort (decision e), and paging of 10 groups unless asked otherwise; a value the list
// doesn't take is the service's sentence, marked isError. A read: it writes nothing. The sort's own rules are
// LibraryDuplicatesSortHttpTests'; the gate for every tool McpToolAccessHttpTests'.
public sealed class McpDuplicateToolHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string Tool = "list_duplicates";
    private const string DuplicatesUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    /// <summary>A third, whose path mapping covers none of its files.</summary>
    private const string AtticBaseUrl = "http://127.0.0.3:9";

    private const string AlienImdbId = "tt0078748";
    private const string GattacaImdbId = "tt0119177";
    private const string HeatImdbId = "tt0113277";

    /// <summary>Where the attic's Jellyfin sees its copy of Alien: under no prefix its mapping knows.</summary>
    private const string AtticAlienPath = "/srv/films/Alien (1979).mkv";

    // Spelled out rather than read from the product code: these are what the user is told.
    private const string AtticAlienPathProblem = "No path mapping of this integration covers “/srv/films/Alien (1979).mkv”.";

    /// <summary>A second disk, on a file system that can share blocks.</summary>
    private const long BackupDeviceId = 2_065;

    private const long SmallSizeBytes = 1_000_000_003;
    private const long LargeSizeBytes = 3_000_000_007;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private IDuplicateMatchDecisionDataFactory _decisions = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private IntegrationConnectionEntity _bedroom = null!;

    public McpDuplicateToolHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        _decisions = new DuplicateMatchDecisionDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Two Jellyfins over one disk, with no path mapping: the app looks for each file where its library says it is.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListDuplicates_gives_the_duplicates_pages_json_with_the_owners_path_problems_for_the_owner_only(bool asTheOwner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = asTheOwner ? _owner : _member;
        // A copy of Alien in a library whose mapping covers none of its files: the reason quotes the owner's paths.
        var attic = await _integrations.Create(
            "jellyfin", "Attic", AtticBaseUrl, apiKey: ApiKey, pathMappings: [new PathMappingPair("/data", Root)]);
        await PlantMovieAsync(_livingRoom, "Alien", 1979, MoviePath("Alien", 1979), AlienImdbId);
        await PlantMovieAsync(attic, "Alien", 1979, AtticAlienPath, AlienImdbId);
        await PlantPossibleMatchAsync("Arrival", 2016);
        // The tool's page size is 10, the page's 25: the same page is asked of both.
        var page = await GetAsync($"{DuplicatesUrl}?pageSize=10", user, cancellationToken);
        var token = await _tokens.Create(user.User, PersonalAccessTokenAccess.ReadOnly);
        var auditsBefore = await CountAuditsAsync(cancellationToken);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal("2 duplicate groups in all; page 1 lists 2.", result.ReadText());
        // Field for field what the HTTP API gives the same user, enums by name.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(page), JsonNode.Parse(result.StructuredContent!.Value.GetRawText())));
        var list = result.ReadStructured<LibraryDuplicateListResponse>();
        Assert.Equal((2, 1, 10), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal([DuplicateClass.SameWork, DuplicateClass.Possible], list.Items.Select(group => group.Class));
        var atticFile = list.Items.SelectMany(group => group.Files).Single(file => file.File.ServicePath == AtticAlienPath);
        // Everyone sees that the attic's file has no app path, and why only the owner.
        Assert.Equal(
            (PathMapStatus.NotCovered, (string?)null, asTheOwner ? AtticAlienPathProblem : null),
            (atticFile.File.PathStatus, atticFile.File.AppPath, atticFile.File.PathProblem));
        // A read: nothing decided, nothing audited.
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DuplicateMatchDecisions.CountAsync(cancellationToken));
            Assert.Equal(auditsBefore, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task ListDuplicates_sorted_recoverable_lists_the_most_room_first_and_the_groups_without_an_estimate_last()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        await PlantByteIdenticalCopiesAsync("Brazil", 1985, 1_048_603, 1_048_605, SmallSizeBytes);
        await PlantByteIdenticalCopiesAsync("Zodiac", 2007, 1_048_607, 1_048_609, LargeSizeBytes);
        // Its backup on btrfs, where a reflinked copy may take no room of its own: no estimate.
        await PlantByteIdenticalCopiesAsync("Memento", 2000, 1_048_611, 1_048_613, LargeSizeBytes, BackupDeviceId);
        _factory.Files.SetFileSystem(BackupDeviceId, "btrfs");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var page = await GetAsync($"{DuplicatesUrl}?sort=Recoverable&pageSize=10", _member, cancellationToken);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(Tool, new Dictionary<string, object?> { ["sort"] = "Recoverable" }, cancellationToken: cancellationToken);

        //Assert
        var list = result.ReadStructured<LibraryDuplicateListResponse>();
        Assert.Equal(
            [
                (DuplicateClass.ByteIdentical, "Zodiac", (long?)LargeSizeBytes),
                (DuplicateClass.ByteIdentical, "Brazil", (long?)SmallSizeBytes),
                (DuplicateClass.ByteIdentical, "Memento", (long?)null),
                (DuplicateClass.SameWork, "Alien", (long?)null),
            ],
            list.Items.Select(group => (group.Class, TitleOf(group), group.Recoverable?.SizeBytes)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(page), JsonNode.Parse(result.StructuredContent!.Value.GetRawText())));
    }

    [Fact]
    public async Task ListDuplicates_filters_by_class_and_by_the_owners_decision_and_pages()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Heat", 1995, HeatImdbId);
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        await PlantCopiesAsync("Gattaca", 1997, GattacaImdbId);
        await PlantPossibleMatchAsync("Brazil", 1985);
        await PlantPossibleMatchAsync("Arrival", 2016);
        var possible = JsonNode.Parse(await GetAsync($"{DuplicatesUrl}?class=Possible", _owner, cancellationToken))!["items"]!.AsArray();
        // The owner dismissed Arrival, the first possible match by title.
        var dismissedKey = possible[0]!["key"]!.GetValue<string>();
        await _decisions.Create(dismissedKey, DuplicateMatchDecision.Dismissed, _owner.User.Id);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var secondPage = await client.CallToolAsync(
            Tool, new Dictionary<string, object?> { ["duplicateClass"] = "SameWork", ["page"] = 2, ["pageSize"] = 2 }, cancellationToken: cancellationToken);
        var all = await client.CallToolAsync(Tool, cancellationToken: cancellationToken);
        var dismissed = await client.CallToolAsync(
            Tool, new Dictionary<string, object?> { ["decision"] = "Dismissed" }, cancellationToken: cancellationToken);

        //Assert
        var second = secondPage.ReadStructured<LibraryDuplicateListResponse>();
        Assert.Equal((3, 2, 2), (second.TotalCount, second.Page, second.PageSize));
        Assert.Equal(["Heat"], second.Items.Select(TitleOf));
        Assert.Equal("3 duplicate groups in all; page 2 lists 1.", secondPage.ReadText());
        // A dismissed group leaves the list, and is listed on asking for it.
        Assert.Equal(["Alien", "Gattaca", "Heat", "Brazil"], all.ReadStructured<LibraryDuplicateListResponse>().Items.Select(TitleOf));
        var dismissedGroup = Assert.Single(dismissed.ReadStructured<LibraryDuplicateListResponse>().Items);
        Assert.Equal((dismissedKey, DuplicateMatchDecision.Dismissed, (string?)null), (dismissedGroup.Key, dismissedGroup.Decision!.Decision, dismissedGroup.Decision.DecidedByUserName));
        Assert.Equal("1 duplicate group in all; page 1 lists 1.", dismissed.ReadText());
    }

    [Theory]
    [InlineData(0, 10, "Page must be 1 or more.")]
    [InlineData(1, 0, "Page size must be between 1 and 100.")]
    [InlineData(1, 101, "Page size must be between 1 and 100.")]
    public async Task ListDuplicates_with_a_page_the_list_doesnt_take_is_an_error_with_the_lists_sentence(int page, int pageSize, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            Tool, new Dictionary<string, object?> { ["page"] = page, ["pageSize"] = pageSize }, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(error, result.ReadText());
        // No data to check against the output schema.
        Assert.Null(result.StructuredContent);
        Assert.Single(result.Content.OfType<TextContentBlock>());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string MoviePath(string title, int year) => $"/media/movies/{title} ({year})/{title} ({year}).mkv";

    /// <summary>The body the web app reads, through the HTTP API with <paramref name="user"/>'s session.</summary>
    private async Task<string> GetAsync(string url, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<int> CountAuditsAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        await _factory.WithDbAsync(async db => count = await db.AuditEvents.CountAsync(cancellationToken));
        return count;
    }

    // A movie a library reported in an item of its own — its title and year, the id given, one version and one name —
    // what a sync stores. Returns the version.
    private async Task<Guid> PlantMovieAsync(IntegrationConnectionEntity library, string title, int year, string servicePath, string? imdbId = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
        return version.Id;
    }

    // A SameWork group: the movie in each library under one IMDb id, at two paths the app hasn't looked at.
    private async Task PlantCopiesAsync(string title, int year, string imdbId)
    {
        await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year), imdbId);
        await PlantMovieAsync(_bedroom, title, year, $"/media/films/{title} ({year}).mkv", imdbId);
    }

    // A Possible group: the movie in each library with one title and year and no id at all.
    private async Task PlantPossibleMatchAsync(string title, int year)
    {
        await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        await PlantMovieAsync(_bedroom, title, year, $"/media/films/{title} ({year}).mkv");
    }

    // A ByteIdentical group: one id-less movie of the living room whose version names two files, copied rather than
    // linked, with every byte the same — the copy on copyDeviceId when given — for the next inspection to read and hash.
    private async Task PlantByteIdenticalCopiesAsync(string title, int year, long inode, long copyInode, long sizeBytes, long? copyDeviceId = null)
    {
        var versionId = await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        var copyPath = $"/media/backup/{title} ({year}).mkv";
        await _physicalFiles.Create(versionId, copyPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        var facts = TestMedia.File(inode, sizeBytes);
        var copyFacts = TestMedia.File(copyInode, sizeBytes, TestMedia.ModifiedUtc.AddDays(3), copyDeviceId);
        _factory.Files.AddFile(MoviePath(title, year), facts, TestMedia.Taken());
        _factory.Files.AddFile(copyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, FakeFileInventory.DefaultSha256(facts)));
    }

    // The title a group is ordered by, as its first file's first work shows it: every planted group's works share one.
    private static string? TitleOf(LibraryDuplicateGroupDto group) => group.Files[0].Works[0].Title;
}
