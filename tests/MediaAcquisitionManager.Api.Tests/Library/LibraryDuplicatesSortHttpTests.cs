using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-45 — the order of the Duplicates list (GET /api/library/duplicates?sort=; Vasyl's decision e, 2026-10-16): Default,
// today's class-then-title order (LibraryDuplicatesHttpTests), also when blank; Recoverable, the largest recoverable room
// first by the estimate a group carries, then one file's names, which free nothing, and last every group with no
// estimate — byte-identical copies the estimate can't be sure of, copies of one work and possible matches —, each tie in
// the default order, so pages never repeat or drop a group. Read by name in any letter case, checked after the paging,
// the class and the decision; any other value is a 400 with its sentence. Estimates come from real inspections over
// HTTP, through the fake file inventory.
public sealed class LibraryDuplicatesSortHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    private const string AlienImdbId = "tt0078748";

    /// <summary>A second disk (8:17), on a file system that can share blocks, beside <see cref="TestMedia.DeviceId"/>'s
    /// ext4.</summary>
    private const long BackupDeviceId = 2_065;

    private const long SmallSizeBytes = 1_000_000_003;
    private const long LargeSizeBytes = 3_000_000_007;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private IntegrationConnectionEntity _bedroom = null!;

    public LibraryDuplicatesSortHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> UnknownSorts => new()
    {
        { "Largest", NotASort("Largest") },
        // By name only: the number of a sort is not its name.
        { "2", NotASort("2") },
        { "Default,Recoverable", NotASort("Default,Recoverable") },
        // A pasted page of text comes back cut, never whole.
        { new string('x', 500), NotASort(new string('x', 32) + "…") },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Two Jellyfins over one disk, with no path mapping: the app looks for each file where its library says it is.
        _livingRoom = await integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("")]
    [InlineData("sort=Default")]
    [InlineData("sort=%20default%20")]
    [InlineData("sort=")]
    public async Task List_without_a_sort_or_sorted_default_lists_by_class_then_title(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryKindOfGroupAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                (DuplicateClass.SameFile, "Heat", (long?)0),
                (DuplicateClass.ByteIdentical, "Brazil", (long?)SmallSizeBytes),
                (DuplicateClass.ByteIdentical, "Memento", (long?)null),
                (DuplicateClass.ByteIdentical, "Zodiac", (long?)LargeSizeBytes),
                (DuplicateClass.SameWork, "Alien", (long?)null),
                (DuplicateClass.Possible, "Arrival", (long?)null),
            ],
            (await ReadListAsync(response, cancellationToken)).Items.Select(PlaceOf));
    }

    [Fact]
    public async Task List_sorted_recoverable_puts_the_most_room_first_and_the_groups_without_an_estimate_last()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryKindOfGroupAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort=Recoverable", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // Heat's file is the largest of all, but its two names free nothing: 0 is an estimate, and the smallest. Memento's
        // copy on btrfs has none, so it waits with the classes that never have one, in the default order.
        Assert.Equal(
            [
                (DuplicateClass.ByteIdentical, "Zodiac", (long?)LargeSizeBytes),
                (DuplicateClass.ByteIdentical, "Brazil", (long?)SmallSizeBytes),
                (DuplicateClass.SameFile, "Heat", (long?)0),
                (DuplicateClass.ByteIdentical, "Memento", (long?)null),
                (DuplicateClass.SameWork, "Alien", (long?)null),
                (DuplicateClass.Possible, "Arrival", (long?)null),
            ],
            list.Items.Select(PlaceOf));
        Assert.Equal(new DuplicateRecoverableSpaceDto(null, RecoverableSpaceGap.SharingFileSystem), list.Items[3].Recoverable);
        Assert.Equal((6, 1, 25), (list.TotalCount, list.Page, list.PageSize));
    }

    [Fact]
    public async Task List_sorted_recoverable_breaks_ties_in_the_default_order_and_pages_every_group_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Planted out of title order: two byte-identical copies of one size, two files of two names each, and two
        // possible matches.
        await PlantByteIdenticalCopiesAsync("Brazil", 1985, 1_048_603, 1_048_605, SmallSizeBytes);
        await PlantByteIdenticalCopiesAsync("Arrival", 2016, 1_048_607, 1_048_609, SmallSizeBytes);
        await PlantHardlinksAsync("Zodiac", 2007, 1_048_611, LargeSizeBytes);
        await PlantHardlinksAsync("Memento", 2000, 1_048_613, SmallSizeBytes);
        await PlantPossibleMatchAsync("Heat", 1995);
        await PlantPossibleMatchAsync("Gattaca", 1997);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var first = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?sort=Recoverable&page=1&pageSize=4", _member.AccessToken), cancellationToken);
        var second = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?sort=Recoverable&page=2&pageSize=4", _member.AccessToken), cancellationToken);
        var again = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?sort=Recoverable&page=1&pageSize=4", _member.AccessToken), cancellationToken);

        //Assert
        Assert.Equal((6, 4, 6, 2), (first.TotalCount, first.Items.Count, second.TotalCount, second.Items.Count));
        Assert.Equal(
            [
                (DuplicateClass.ByteIdentical, "Arrival", (long?)SmallSizeBytes),
                (DuplicateClass.ByteIdentical, "Brazil", (long?)SmallSizeBytes),
                (DuplicateClass.SameFile, "Memento", (long?)0),
                (DuplicateClass.SameFile, "Zodiac", (long?)0),
                (DuplicateClass.Possible, "Gattaca", (long?)null),
                (DuplicateClass.Possible, "Heat", (long?)null),
            ],
            first.Items.Concat(second.Items).Select(PlaceOf));
        Assert.Equal(6, first.Items.Concat(second.Items).Select(group => group.Key).Distinct(StringComparer.Ordinal).Count());
        // A total order: the same page on every read.
        Assert.Equal(first.Items.Select(group => group.Key), again.Items.Select(group => group.Key));
    }

    [Fact]
    public async Task List_sorted_recoverable_orders_within_the_class_asked_for()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryKindOfGroupAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?class=ByteIdentical&sort=Recoverable", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(["Zodiac", "Brazil", "Memento"], list.Items.Select(TitleOf));
        Assert.Equal(3, list.TotalCount);
    }

    [Theory]
    [InlineData("recoverable")]
    [InlineData("RECOVERABLE")]
    [InlineData("%20Recoverable%20")]
    public async Task List_takes_a_sort_in_any_letter_case(string sort)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantEveryKindOfGroupAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort={sort}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Zodiac", "Brazil", "Heat", "Memento", "Alien", "Arrival"], (await ReadListAsync(response, cancellationToken)).Items.Select(TitleOf));
    }

    [Theory]
    [MemberData(nameof(UnknownSorts))]
    public async Task List_with_an_unknown_sort_is_400_naming_the_sorts(string sort, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?sort={Uri.EscapeDataString(sort)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("page=0&sort=Largest", "Page must be 1 or more.")]
    [InlineData("pageSize=101&sort=Largest", "Page size must be between 1 and 100.")]
    [InlineData("class=Hardlink&sort=Largest", "“Hardlink” is not a duplicate class. Use SameFile, ByteIdentical, SameWork or Possible.")]
    [InlineData("decision=Maybe&sort=Largest", "“Maybe” is not a decision. Use Confirmed or Dismissed.")]
    public async Task List_checks_the_sort_after_the_paging_the_class_and_the_decision(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string NotASort(string echo) => $"“{echo}” is not a sort. Use Default or Recoverable.";

    private static string MoviePath(string title, int year) => $"/media/movies/{title} ({year})/{title} ({year}).mkv";

    // One group of each class, and byte-identical copies whose room is known, large or small, or not known — then an
    // inspection, which reads the files' identities and hashes.
    private async Task PlantEveryKindOfGroupAsync(CancellationToken cancellationToken)
    {
        await _roots.Allow(Root);
        await PlantPossibleMatchAsync("Arrival", 2016);
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        await PlantByteIdenticalCopiesAsync("Brazil", 1985, 1_048_603, 1_048_605, SmallSizeBytes);
        await PlantByteIdenticalCopiesAsync("Zodiac", 2007, 1_048_607, 1_048_609, LargeSizeBytes);
        // Its backup on a disk whose file system can share blocks: a reflinked copy there may take no room of its own.
        await PlantByteIdenticalCopiesAsync("Memento", 2000, 1_048_611, 1_048_613, 2_000_000_011, BackupDeviceId);
        _factory.Files.SetFileSystem(BackupDeviceId, "btrfs");
        // The largest file of all, with two names.
        await PlantHardlinksAsync("Heat", 1995, 1_048_615, 5_000_000_015);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
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

    // A SameFile group: one id-less movie of the living room whose version names one file twice — its own name and
    // the download's — for the next inspection to read.
    private async Task PlantHardlinksAsync(string title, int year, long inode, long sizeBytes)
    {
        var versionId = await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        var linkPath = $"/media/downloads/{title} ({year}).mkv";
        await _physicalFiles.Create(versionId, linkPath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        var facts = TestMedia.File(inode, sizeBytes, linkCount: 2);
        _factory.Files.AddFile(MoviePath(title, year), facts, TestMedia.Taken());
        _factory.Files.AddFile(linkPath, facts, TestMedia.Taken());
    }

    // A ByteIdentical group: one id-less movie of the living room whose version names two files, copied rather than
    // linked, with every byte the same — the copy on copyDeviceId when given — for the next inspection to read and hash.
    // Each copy takes sizeBytes, so removing one frees that much where the estimate is sure.
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

    // A group's class, the title it is ordered by, and the room its estimate says removing the extra copies frees.
    private static (DuplicateClass, string?, long?) PlaceOf(LibraryDuplicateGroupDto group) =>
        (group.Class, TitleOf(group), group.Recoverable?.SizeBytes);

    // The title a group is ordered by, as its first file's first work shows it: every planted group's works share one.
    private static string? TitleOf(LibraryDuplicateGroupDto group) => group.Files[0].Works[0].Title;

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
