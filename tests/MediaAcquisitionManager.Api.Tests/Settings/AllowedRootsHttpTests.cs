using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-9 — the allowed roots behind the Media folders settings page (GET/PUT /api/settings/allowed-roots):
// the installation owner's alone, the read included, as the list says how the host's folders are laid
// out. A save replaces the whole list with the paths in canonical form, in the order typed, and writes
// one audit row with no target, unless it changes nothing; a list that breaks a rule is refused with the
// rule's sentence and changes nothing. Saving starts no inspection. The rules check paths of the platform the app runs on
// (Windows on a developer's box, Linux in CI and in the container), so every path a test saves is picked
// for the platform; a list stored without the page (a seeder) may hold any path.
public sealed class AllowedRootsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/allowed-roots";
    private static readonly string SignInRequiredBody = ErrorBody.Json("Sign in to continue.", "account.sign-in-required");
    private static readonly string OwnerOnlyBody = ErrorBody.Json("Only the installation owner can do this.", "account.owner-only");

    // Spelled out rather than read from AllowedRootRules: they are what the settings page shows.
    private const string TooManyError = "The app can have at most 20 media folders.";
    private const string BlankError = "Each media folder needs its path.";
    private const string TooLongError = "A media folder's path can be at most 1024 characters long.";
    private const string ControlCharacterError = "A media folder's path can't contain line breaks or other control characters.";
    private const string LinuxRelativeError = "“media” is not a full path. Start it with /.";
    private const string WindowsRelativeError = "“media” is not a full path. Start it with a drive letter such as D:\\.";
    private const string LinuxDotSegmentError = "“/media/../etc” contains . or .. as a folder name. Write the folder's full path without them.";
    private const string WindowsDotSegmentError = "“D:\\Media\\..\\etc” contains . or .. as a folder name. Write the folder's full path without them.";
    private const string WindowsPathOnLinuxError =
        "“D:\\Media” is a Windows path, but this app runs on Linux. Give the folder's path inside the app, such as /media.";
    private const string LinuxPathOnWindowsError =
        "“/media” is a Linux path, but this app runs on Windows. Give the folder's path inside the app, such as D:\\Media.";
    private const string LinuxWholeFileSystemError = "“/” is the whole file system. Name the folder the media is in, such as /media.";
    private const string WindowsWholeFileSystemError = "“D:\\” is the whole file system. Name the folder the media is in, such as D:\\Media.";
    private const string LinuxListedTwiceError = "“/media” is listed twice. Keep one.";
    private const string WindowsListedTwiceError = "“D:\\Media” is listed twice. Keep one.";
    private const string LinuxInsideAnotherError = "“/media/movies” is inside “/media”, which already allows it. Keep only “/media”.";
    private const string WindowsInsideAnotherError =
        "“D:\\Media\\Movies” is inside “D:\\media”, which already allows it. Keep only “D:\\media”.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public AllowedRootsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>One case per rule of AllowedRootRules, with the sentence the owner gets on the platform
    /// running the tests.</summary>
    public static TheoryData<string, string> InvalidLists => new()
    {
        { "21 folders", TooManyError },
        { "a blank path", BlankError },
        { "a null path", BlankError },
        { "a path of 1025 characters", TooLongError },
        { "a line break", ControlCharacterError },
        { "a relative path", OnThisPlatform(LinuxRelativeError, WindowsRelativeError) },
        { "a dot-dot folder", OnThisPlatform(LinuxDotSegmentError, WindowsDotSegmentError) },
        { "a path of the other style", OnThisPlatform(WindowsPathOnLinuxError, LinuxPathOnWindowsError) },
        { "the whole file system", OnThisPlatform(LinuxWholeFileSystemError, WindowsWholeFileSystemError) },
        { "one folder twice", OnThisPlatform(LinuxListedTwiceError, WindowsListedTwiceError) },
        { "a folder inside another", OnThisPlatform(LinuxInsideAnotherError, WindowsInsideAnotherError) },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Get_as_the_owner_before_any_folder_is_allowed_is_200_with_no_paths()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AllowedRootsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        // The default: a fresh installation reads nothing until the owner names a folder. An empty list on
        // the wire, never null or a missing property, so the page needs no special case. MAM-24 — beside it the
        // replacement gates, closed (ReplacementSettingsHttpTests pins them). MAM-26 — and no quarantine.
        Assert.Empty(body!.Paths);
        Assert.Equal(
            """{"paths":[],"replacementEnabled":false,"libraryFolders":[],"unmappedLibraryFolderCount":0,"quarantines":[]}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.AllowedRoots.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Get_as_the_owner_lists_the_paths_in_the_order_entered()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Written in neither the entered order nor alphabetical order, so only the position orders them.
        var rows = new AllowedRootDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>());
        await rows.Create("/srv/tv", position: 2);
        await rows.Create("/media", position: 0);
        await rows.Create("/data", position: 1);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AllowedRootsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["/media", "/data", "/srv/tv"], body!.Paths);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(3, await db.AllowedRoots.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Get_as_a_signed_in_non_owner_is_403_with_the_owner_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow("/media", "/data");

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        // The whole body, so no path rides along beside the sentence.
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(2, await db.AllowedRoots.CountAsync(cancellationToken)));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Endpoint_without_a_token_is_401_with_the_sign_in_sentence_and_changes_nothing(string method)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow("/media");

        //Act
        var response = method == "GET"
            ? await _factory.GetHttpRequest(BaseUrl)
            : await _factory.PutHttpRequest(BaseUrl, body: new UpdateAllowedRootsRequest([ValidPath("tv")]));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal([(seeded[0].Id, "/media", 0)], await StoredRootsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_as_the_owner_is_204_and_stores_the_canonical_paths_in_the_order_given()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Spaces around, repeated and trailing separators, and not in alphabetical order.
        IReadOnlyList<string?> typed = OnThisPlatform<IReadOnlyList<string?>>(
            [" /srv//tv/ ", "/media", "//data/"],
            [@" e:/TV\ ", @"D:\Media", @"\\nas\share\\data\"]);
        IReadOnlyList<string> canonical = OnThisPlatform<IReadOnlyList<string>>(
            ["/srv/tv", "/media", "/data"],
            [@"E:\TV", @"D:\Media", @"\\nas\share\data"]);

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest(typed));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(canonical, await GetPathsAsync(cancellationToken));
        Assert.Equal(
            [(canonical[0], 0), (canonical[1], 1), (canonical[2], 2)],
            (await StoredRootsAsync(cancellationToken)).Select(row => (row.Path, row.Position)));
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.AllowedRootsChanged, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            // No target: the roots belong to the installation, not to a row the audit could name.
            Assert.Null(audit.TargetKind);
            Assert.Null(audit.TargetId);
            Assert.Null(audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Update_replaces_the_earlier_list_completely()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(ValidPath("media"), ValidPath("old"));

        //Act
        // One folder of the old list again: its old row must be gone before the new one arrives, or the
        // unique index on the path refuses the save.
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([ValidPath("new"), ValidPath("media") + "/"]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([ValidPath("new"), ValidPath("media")], await GetPathsAsync(cancellationToken));
        var stored = await StoredRootsAsync(cancellationToken);
        Assert.Equal([(ValidPath("new"), 0), (ValidPath("media"), 1)], stored.Select(row => (row.Path, row.Position)));
        // Replaced rather than updated in place: none of the old rows is left.
        Assert.DoesNotContain(stored, row => seeded.Any(old => old.Id == row.Id));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(AuditAction.AllowedRootsChanged, (await db.AuditEvents.SingleAsync(cancellationToken)).Action));
    }

    [Fact]
    public async Task Update_with_the_folders_already_allowed_is_204_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(ValidPath("media"), ValidPath("data"));

        //Act
        // The same folders in the same order, one typed with a trailing separator: no change once canonical.
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([ValidPath("media") + "/", ValidPath("data")]));

        //Assert
        // MAM-4's rule: a save that changes nothing records nothing. The rows stay the same rows, and no audit
        // row claims the roots changed.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = await StoredRootsAsync(cancellationToken);
        Assert.Equal(seeded.Select(row => row.Id), stored.Select(row => row.Id));
        await _factory.WithDbAsync(async db => Assert.Empty(await db.AuditEvents.ToListAsync(cancellationToken)));
    }

    [Theory]
    [InlineData("an empty list")]
    [InlineData("a null list")]
    [InlineData("no list")]
    public async Task Update_with_no_paths_is_204_and_allows_nothing(string body)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(ValidPath("media"), ValidPath("data"));
        object request = body switch
        {
            "an empty list" => new UpdateAllowedRootsRequest([]),
            "a null list" => new { paths = (string?[]?)null },
            "no list" => new { },
            _ => throw new ArgumentOutOfRangeException(nameof(body), body, null),
        };

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // Allowing nothing is the default state, so saving it is never refused.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await GetPathsAsync(cancellationToken));
        Assert.Empty(await StoredRootsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.AllowedRootsChanged, audit.Action);
            Assert.Equal((_owner.User.Id, "owner"), (audit.ActorUserId, audit.ActorUserName));
        });
    }

    [Theory]
    [MemberData(nameof(InvalidLists))]
    public async Task Update_with_an_invalid_list_is_400_with_the_rule_and_changes_nothing(string invalid, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(ValidPath("srv"));
        IReadOnlyList<string?> paths = invalid switch
        {
            "21 folders" => [.. Enumerable.Range(1, 21).Select(number => ValidPath($"media{number}"))],
            "a blank path" => [ValidPath("media"), "   "],
            "a null path" => [ValidPath("media"), null],
            "a path of 1025 characters" => [ValidPath(new string('a', 1025 - ValidPath("").Length))],
            "a line break" => [ValidPath("me\ndia")],
            "a relative path" => ["media"],
            "a dot-dot folder" => [OnThisPlatform("/media/../etc", @"D:\Media\..\etc")],
            "a path of the other style" => [OnThisPlatform(@"D:\Media", "/media")],
            "the whole file system" => [OnThisPlatform("/", @"D:\")],
            "one folder twice" => OnThisPlatform<IReadOnlyList<string?>>(["/media", "/media/"], [@"D:\Media", @"d:\media\"]),
            "a folder inside another" => OnThisPlatform<IReadOnlyList<string?>>(["/media/movies", "/media"], [@"D:\Media\Movies", @"d:\media"]),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest(paths));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([(seeded[0].Id, ValidPath("srv"), 0)], await StoredRootsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(ValidPath("media"));

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _member.AccessToken, new UpdateAllowedRootsRequest([ValidPath("data")]));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal([(seeded[0].Id, ValidPath("media"), 0)], await StoredRootsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_as_a_signed_in_non_owner_with_an_invalid_list_is_403_not_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The owner check runs before the body is bound or the rules read it, so a non-owner never learns
        // from a 400 what a valid list would look like.
        var response = await _factory.PutHttpRequest(BaseUrl, _member.AccessToken, new UpdateAllowedRootsRequest(["media", null]));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AllowedRoots.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_as_a_signed_in_non_owner_logs_one_warning_naming_the_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _member.AccessToken, new UpdateAllowedRootsRequest([ValidPath("media")]));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // A Warning ships off the box: the page offers the save to the owner only, so this is someone
        // sending it by hand.
        var entry = Assert.Single(_factory.Logs.Entries, logged => logged.Category == typeof(InstallationOwnerFilter).FullName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(_member.User.Id.ToString(), entry.Message, StringComparison.Ordinal);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AllowedRoots.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_queues_no_file_inspection()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([ValidPath("media")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // The owner starts one from the Library page; a save that read the disks on its own would surprise
        // whoever is still typing the next folder.
        Assert.False(_factory.Resolve<FileInspectionQueue>().Reader.TryPeek(out _));
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.AllowedRoots.CountAsync(cancellationToken));
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_logs_how_many_folders_the_owner_saved_and_never_the_paths()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A folder name nothing else in the host would ever log.
        var path = ValidPath("private-share-7f3a");

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([path, ValidPath("data")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entry = Assert.Single(_factory.Logs.Entries, logged => logged.Category == typeof(AllowedRootsService).FullName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("owner changed the allowed roots; the number allowed is now 2.", entry.Message);
        // The paths say how the host's disks are laid out, which no log that ships off the box should.
        Assert.DoesNotContain(_factory.Logs.Entries, logged => logged.Message.Contains("private-share-7f3a", StringComparison.Ordinal));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(2, await db.AllowedRoots.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The platform the app under test runs on, read independently of AllowedRootRules.PlatformStyle so a
    // wrong platform there fails these tests too.
    private static T OnThisPlatform<T>(T linux, T windows) => OperatingSystem.IsWindows() ? windows : linux;

    // A folder the app may be given on the platform running the tests: /<name>, or D:\<name> on Windows.
    private static string ValidPath(string name) => OnThisPlatform("/" + name, @"D:\" + name);

    // The list as the settings page reads it back.
    private async Task<IReadOnlyList<string>> GetPathsAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AllowedRootsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        return body!.Paths;
    }

    // The stored rows, in order, with their positions.
    private async Task<List<(Guid Id, string Path, int Position)>> StoredRootsAsync(CancellationToken cancellationToken)
    {
        List<AllowedRootEntity> rows = [];
        await _factory.WithDbAsync(async db =>
            rows = await db.AllowedRoots.OrderBy(root => root.Position).ToListAsync(cancellationToken));
        return [.. rows.Select(root => (root.Id, root.Path, root.Position))];
    }
}
