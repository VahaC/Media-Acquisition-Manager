using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-24, ADR 0033 — the owner's replacement gates on the Media folders page (GET/PUT /api/settings/allowed-roots): the
// master switch, off by default, and a tick per library folder, none by default, read and saved with the allowed roots
// by the page's one Save, in one transaction, with one audit row per part that changed and none for a save that changes
// nothing. The folders come from a real sync of VendorSamples/Jellyfin/virtual-folders.json, with Jellyfin's /media and
// /config mapped to folders of the platform running the tests — Windows on a developer's box, Linux in CI — so the roots
// the page saves pass the platform's rules: /media/movies, /media/tv-shows, /media/cartoons and /media/documentary lie
// inside the media root, /config/data/collections outside it. A tick must be a library folder the app lists or a tick
// already stored; missing fields switch off and untick (fail closed).
public sealed class ReplacementSettingsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/allowed-roots";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    // Spelled out rather than read from the product code: these are what the settings page shows.
    private const string UnknownFolderError = "“{0}” isn't a library folder the app knows, so it can't be ticked for replacement.";
    private const string TooManyError = "The app can have at most 50 library folders ticked for replacement.";
    private const string BlankError = "Each library folder ticked for replacement needs its path.";
    private const string TooLongError = "A library folder's path can be at most 1024 characters long.";
    private const string ControlCharacterError = "A library folder's path can't contain line breaks or other control characters.";
    private const string RelativeError = "“media/movies” is not a full path, so it can't be ticked for replacement.";
    private const string DotSegmentError = "“/media/../etc” contains . or .. as a folder name, so it can't be ticked for replacement.";
    private const string LinuxTickedTwiceError = "“/media/movies” is ticked twice. Keep one.";
    private const string WindowsTickedTwiceError = "“D:\\media\\movies” is ticked twice. Keep one.";

    // The libraries of the sample, by name.
    private const string Movies = "Фільми";
    private const string Cartoons = "Мульфільми";
    private const string Documentaries = "Документалки";
    private const string Collections = "Колекції";
    private const string TvShows = "Серіали";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IReplacementSettingsSeeder _replacement = null!;
    private IIntegrationConnectionSeeder _integrations = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public ReplacementSettingsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>One case per rule of ReplacementFolderRules, with the sentence the owner gets on the platform running the
    /// tests.</summary>
    public static TheoryData<string, string> InvalidTicks => new()
    {
        { "51 folders", TooManyError },
        { "a blank path", BlankError },
        { "a null path", BlankError },
        { "a path of 1025 characters", TooLongError },
        { "a line break", ControlCharacterError },
        { "a relative path", RelativeError },
        { "a dot-dot folder", DotSegmentError },
        { "one folder twice", OnThisPlatform(LinuxTickedTwiceError, WindowsTickedTwiceError) },
    };

    /// <summary>Each kind of path the page never lists, so it can't be ticked.</summary>
    public static TheoryData<string> UnknownFolders => new()
    {
        "a folder no library lists",
        "a folder inside a library folder",
        "a library folder no mapping brings into the app",
        "a library folder in the other platform's style",
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        _replacement = new ReplacementSettingsSeeder(_factory.Scope);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Get_on_a_fresh_installation_is_off_with_no_library_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // ADR 0033's default: off, nothing ticked, and no library folder until a library is synced. Empty lists and a
        // zero on the wire, never null or a missing property, so the page needs no special case.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"paths":[],"replacementEnabled":false,"libraryFolders":[],"unmappedLibraryFolderCount":0}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ReplacementSettings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementFolders.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Get_lists_the_sample_library_folders_inside_the_media_root_as_available_and_the_other_outside()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();

        //Act
        var body = await GetAsync(cancellationToken);

        //Assert
        // By app path; none ticked by default; the collections library's folder lies outside the media root.
        Assert.Equal([MediaRoot], body.Paths);
        Assert.Equal((false, 0), (body.ReplacementEnabled, body.UnmappedLibraryFolderCount));
        Assert.Equal(
            [
                (Config("data", "collections"), Collections, false, ReplacementFolderStatus.OutsideMediaFolders),
                (Media("cartoons"), Cartoons, false, ReplacementFolderStatus.Available),
                (Media("documentary"), Documentaries, false, ReplacementFolderStatus.Available),
                (Media("movies"), Movies, false, ReplacementFolderStatus.Available),
                (Media("tv-shows"), TvShows, false, ReplacementFolderStatus.Available),
            ],
            Folders(body));
    }

    [Fact]
    public async Task Get_lists_a_folder_two_libraries_share_once_with_both_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var cartoons = _jellyfin.VirtualFolders.OfType<JsonObject>().Single(folder => (string?)folder["Name"] == Cartoons);
        ((JsonArray)cartoons["Locations"]!).Add(JellyfinSamples.MoviesRoot);
        await SyncSampleLibrariesAsync();

        //Act
        var body = await GetAsync(cancellationToken);

        //Assert
        // One checkbox for the folder, as the tick belongs to the folder; the libraries by name, as the reader orders them.
        var movies = Assert.Single(body.LibraryFolders, folder => folder.AppPath == Media("movies"));
        Assert.Equal([Cartoons, Movies], movies.LibraryNames);
        Assert.Equal(5, body.LibraryFolders.Count);
    }

    [Fact]
    public async Task Get_counts_the_library_folders_no_mapping_brings_into_the_app()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        // Only /media is mapped: the collections library's /config/data/collections has no path in the app.
        await SyncSampleLibrariesAsync([new PathMappingPair("/media", MediaRoot)]);

        //Act
        var body = await GetAsync(cancellationToken);

        //Assert
        // Not listed, as there is nothing in the app to tick, but counted, so the page can say why it is missing.
        Assert.Equal(1, body.UnmappedLibraryFolderCount);
        Assert.Equal(
            [Media("cartoons"), Media("documentary"), Media("movies"), Media("tv-shows")],
            body.LibraryFolders.Select(folder => folder.AppPath));
    }

    [Fact]
    public async Task Update_switching_on_and_ticking_folders_is_204_and_writes_the_rows_and_one_audit_row_per_change()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var seeded = await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Media("tv-shows"), Media("movies")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await GetAsync(cancellationToken);
        Assert.True(body.ReplacementEnabled);
        Assert.Equal([Media("movies"), Media("tv-shows")], body.LibraryFolders.Where(folder => folder.Ticked).Select(folder => folder.AppPath));
        await _factory.WithDbAsync(async db =>
        {
            var settings = await db.ReplacementSettings.SingleAsync(cancellationToken);
            Assert.Equal((new Guid("c0000000-0000-0000-0000-000000000002"), true), (settings.Id, settings.IsEnabled));
            Assert.Equal(
                [(Media("tv-shows"), 0), (Media("movies"), 1)],
                (await db.ReplacementFolders.OrderBy(folder => folder.Position).ToListAsync(cancellationToken)).Select(folder => (folder.Path, folder.Position)));
            // The roots were the stored ones: not rewritten, and no row claims they changed.
            Assert.Equal(seeded[0].Id, (await db.AllowedRoots.SingleAsync(cancellationToken)).Id);
            var audit = await db.AuditEvents.OrderBy(row => row.Action).ToListAsync(cancellationToken);
            Assert.Equal([AuditAction.ReplacementFoldersChanged, AuditAction.ReplacementSwitchedOn], audit.Select(row => row.Action));
            Assert.All(audit, row =>
            {
                Assert.Equal((_owner.User.Id, "owner"), (row.ActorUserId, row.ActorUserName));
                // No target: the switch and the ticks belong to the installation, not to a row the audit could name.
                Assert.Null(row.TargetKind);
                Assert.Null(row.TargetId);
                Assert.Null(row.TargetName);
                Assert.Equal(now, row.CreatedUtc);
            });
        });
    }

    [Fact]
    public async Task Update_adding_a_root_switching_on_and_ticking_a_folder_under_it_saves_all_three_with_three_audit_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSampleLibrariesAsync();

        //Act
        // The folder is outside every root when the page is read: the tick is still a library folder the app lists, so
        // one Save takes the root and the tick together.
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Media("movies")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await GetAsync(cancellationToken);
        Assert.Equal([MediaRoot], body.Paths);
        Assert.True(body.ReplacementEnabled);
        Assert.Contains((Media("movies"), Movies, true, ReplacementFolderStatus.Available), Folders(body));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [AuditAction.AllowedRootsChanged, AuditAction.ReplacementFoldersChanged, AuditAction.ReplacementSwitchedOn],
                await db.AuditEvents.OrderBy(row => row.Action).Select(row => row.Action).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_switching_off_writes_one_switched_off_row_and_keeps_the_ticks()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        var ticks = await _replacement.SwitchOn(Media("movies"));

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], false, [Media("movies")]));

        //Assert
        // The switch row stays, off; the ticks wait, unchanged, for the switch to come back on.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False((await db.ReplacementSettings.SingleAsync(cancellationToken)).IsEnabled);
            Assert.Equal(ticks[0].Id, (await db.ReplacementFolders.SingleAsync(cancellationToken)).Id);
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal((AuditAction.ReplacementSwitchedOff, "owner"), (audit.Action, audit.ActorUserName));
        });
    }

    [Fact]
    public async Task Update_that_changes_nothing_is_204_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var roots = await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        var ticks = await _replacement.SwitchOn(Media("movies"), Media("tv-shows"));

        //Act
        // The ticks in the other order and the root with a trailing separator: no change once canonical, and the
        // order of the ticks means nothing.
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot + Separator], true, [Media("tv-shows"), Media("movies")]));

        //Assert
        // MAM-4's rule over all three parts: the rows stay the same rows, and no audit row claims a change.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(roots.Select(root => root.Id), await db.AllowedRoots.Select(root => root.Id).ToListAsync(cancellationToken));
            Assert.Equal(
                ticks.Select(tick => (tick.Id, tick.Position)),
                (await db.ReplacementFolders.OrderBy(tick => tick.Position).ToListAsync(cancellationToken)).Select(tick => (tick.Id, tick.Position)));
            Assert.True((await db.ReplacementSettings.SingleAsync(cancellationToken)).IsEnabled);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_off_with_nothing_ticked_on_a_fresh_installation_writes_no_switch_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], false, []));

        //Assert
        // No row is already off: saving off writes nothing, and the next GET still reads off.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False((await GetAsync(cancellationToken)).ReplacementEnabled);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ReplacementSettings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("no fields")]
    [InlineData("null fields")]
    public async Task Update_without_the_replacement_fields_switches_off_and_unticks_every_folder(string fields)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        await _replacement.SwitchOn(Media("movies"));
        object request = fields switch
        {
            "no fields" => new { paths = new[] { MediaRoot } },
            "null fields" => new { paths = new[] { MediaRoot }, replacementEnabled = (bool?)null, replacementFolders = (string?[]?)null },
            _ => throw new ArgumentOutOfRangeException(nameof(fields), fields, null),
        };

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // Fail closed, as a missing list of roots allows nothing: a page or a script that doesn't know the gates can only
        // close them, never leave them open behind its back.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False((await db.ReplacementSettings.SingleAsync(cancellationToken)).IsEnabled);
            Assert.Equal(0, await db.ReplacementFolders.CountAsync(cancellationToken));
            Assert.Equal(
                [AuditAction.ReplacementFoldersChanged, AuditAction.ReplacementSwitchedOff],
                await db.AuditEvents.OrderBy(row => row.Action).Select(row => row.Action).ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Get_lists_a_stored_tick_that_is_no_longer_a_library_folder_ticked_so_it_can_be_unticked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        // A folder the library dropped since the owner ticked it.
        await _replacement.Tick(Media("old-films"));

        //Act
        var body = await GetAsync(cancellationToken);

        //Assert
        // Among the folders by its path, with no library name, and no effect.
        Assert.Equal(
            [Config("data", "collections"), Media("cartoons"), Media("documentary"), Media("movies"), Media("old-films"), Media("tv-shows")],
            body.LibraryFolders.Select(folder => folder.AppPath));
        Assert.Contains((Media("old-films"), "", true, ReplacementFolderStatus.NoLongerALibraryFolder), Folders(body));
    }

    [Fact]
    public async Task Get_lists_a_stored_tick_that_is_no_full_path_as_it_is_so_it_can_be_unticked_and_ticks_no_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        // A row edited by hand: no rule would have let the page save it.
        await _replacement.SwitchOn("media/movies");

        //Act
        var body = await GetAsync(cancellationToken);

        //Assert
        // Listed as it is, with no library name, so the owner can untick it — and matching no folder, Фільми included.
        Assert.Contains(("media/movies", "", true, ReplacementFolderStatus.NoLongerALibraryFolder), Folders(body));
        Assert.Contains((Media("movies"), Movies, false, ReplacementFolderStatus.Available), Folders(body));
        Assert.DoesNotContain(Folders(body), folder => folder.Ticked && folder.Status != ReplacementFolderStatus.NoLongerALibraryFolder);
    }

    [Fact]
    public async Task Update_keeping_a_stale_tick_is_204_and_keeps_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        await _replacement.SwitchOn(Media("old-films"));

        //Act
        // The page sends back every tick it shows, the stale one too, and adds one.
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Media("old-films"), Media("movies")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(
                [Media("old-films"), Media("movies")],
                await db.ReplacementFolders.OrderBy(folder => folder.Position).Select(folder => folder.Path).ToListAsync(cancellationToken));
            Assert.Equal(AuditAction.ReplacementFoldersChanged, (await db.AuditEvents.SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task Update_unticking_a_stale_tick_removes_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        await _replacement.SwitchOn(Media("old-films"), Media("movies"));

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Media("movies")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await GetAsync(cancellationToken);
        Assert.DoesNotContain(body.LibraryFolders, folder => folder.AppPath == Media("old-films"));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal([Media("movies")], await db.ReplacementFolders.Select(folder => folder.Path).ToListAsync(cancellationToken));
            Assert.Equal(AuditAction.ReplacementFoldersChanged, (await db.AuditEvents.SingleAsync(cancellationToken)).Action);
        });
    }

    [Fact]
    public async Task Update_ticking_a_library_folder_outside_the_media_folders_is_204_and_lists_it_ticked_outside()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Config("data", "collections")]));

        //Assert
        // ADR 0033: a tick outside the allowed roots is kept and simply has no effect until its folder is inside one.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(
            (Config("data", "collections"), Collections, true, ReplacementFolderStatus.OutsideMediaFolders),
            Folders(await GetAsync(cancellationToken)));
    }

    [Fact]
    public async Task Update_removing_the_root_of_a_ticked_folder_keeps_it_listed_once_ticked_outside_the_media_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        await _replacement.SwitchOn(Media("movies"));

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([ConfigRoot], true, [Media("movies")]));

        //Assert
        // Still a library folder the app knows, so listed once with its real status — never a second time as a stale
        // tick — and ticked, so the owner can untick it.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await GetAsync(cancellationToken);
        var movies = Assert.Single(body.LibraryFolders, folder => folder.AppPath == Media("movies"));
        Assert.Equal((true, ReplacementFolderStatus.OutsideMediaFolders), (movies.Ticked, movies.Status));
        Assert.DoesNotContain(body.LibraryFolders, folder => folder.Status == ReplacementFolderStatus.NoLongerALibraryFolder);
    }

    [Theory]
    [MemberData(nameof(UnknownFolders))]
    public async Task Update_ticking_a_path_the_page_never_lists_is_400_and_changes_nothing(string unknown)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(MediaRoot);
        // Only /media is mapped: the collections library's folder has no path in the app.
        await SyncSampleLibrariesAsync([new PathMappingPair("/media", MediaRoot)]);
        var path = unknown switch
        {
            "a folder no library lists" => Media("not-a-library"),
            "a folder inside a library folder" => Media("movies", "Alien (1979)"),
            "a library folder no mapping brings into the app" => Config("data", "collections"),
            "a library folder in the other platform's style" => OnThisPlatform(@"D:\media\movies", "/media/movies"),
            _ => throw new ArgumentOutOfRangeException(nameof(unknown), unknown, null),
        };

        //Act
        // With a new root, the switch on and a real library folder beside it: the refusal must take all of it back.
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([ConfigRoot], true, [Media("movies"), path]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(string.Format(UnknownFolderError, path), await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(seeded[0].Id, cancellationToken);
    }

    [Theory]
    [MemberData(nameof(InvalidTicks))]
    public async Task Update_with_invalid_ticks_is_400_with_the_rule_and_changes_nothing(string invalid, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        IReadOnlyList<string?> ticks = invalid switch
        {
            "51 folders" => [.. Enumerable.Range(1, 51).Select(number => Media($"folder{number}"))],
            "a blank path" => [Media("movies"), "   "],
            "a null path" => [Media("movies"), null],
            "a path of 1025 characters" => ["/" + new string('a', 1024)],
            "a line break" => ["/media/mo\nvies"],
            "a relative path" => ["media/movies"],
            "a dot-dot folder" => ["/media/../etc"],
            "one folder twice" => OnThisPlatform<IReadOnlyList<string?>>(["/media/movies", "/media/movies/"], [@"D:\media\movies", @"d:\MEDIA\movies\"]),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([ConfigRoot], true, ticks));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(seeded[0].Id, cancellationToken);
    }

    [Fact]
    public async Task Update_as_a_signed_in_non_owner_is_403_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _member.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Media("movies")]));

        //Assert
        // ADR 0033: the gates are the owner's alone, as every other Media folders write is.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingSavedAsync(seeded[0].Id, cancellationToken);
    }

    [Fact]
    public async Task Update_racing_another_first_switch_on_writes_one_switch_row_and_both_are_204()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();
        var request = new UpdateAllowedRootsRequest([MediaRoot], true, [Media("movies")]);
        HttpResponseMessage? racing = null;
        // Both read "off" before either writes; the other one commits just before this one takes the write lock.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true, async _ => racing = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, request));

        //Act
        var response = await _factory.PutHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // The second reads the state again under the lock and finds its change made: no second insert of the switch
        // row (a primary-key clash, a 500), and no second audit row claiming a change.
        Assert.Equal(HttpStatusCode.NoContent, racing?.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.True((await db.ReplacementSettings.SingleAsync(cancellationToken)).IsEnabled);
            Assert.Equal([Media("movies")], await db.ReplacementFolders.Select(folder => folder.Path).ToListAsync(cancellationToken));
            Assert.Equal(
                [AuditAction.ReplacementFoldersChanged, AuditAction.ReplacementSwitchedOn],
                await db.AuditEvents.OrderBy(row => row.Action).Select(row => row.Action).ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_logs_the_switch_and_how_many_folders_are_ticked_and_never_the_paths()
    {
        //Arrange
        await _roots.Allow(MediaRoot);
        await SyncSampleLibrariesAsync();

        //Act
        var response = await _factory.PutHttpRequest(
            BaseUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaRoot], true, [Media("movies"), Media("tv-shows")]));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entries = _factory.Logs.Entries.Where(logged => logged.Category == typeof(AllowedRootsService).FullName).ToList();
        Assert.All(entries, entry => Assert.Equal(LogLevel.Information, entry.Level));
        // The roots didn't change, so their line isn't written.
        Assert.Equal(
            ["owner switched replacement on.", "owner changed the library folders ticked for replacement; the number ticked is now 2."],
            entries.Select(entry => entry.Message));
        // The paths say how the host's disks are laid out, which no log that ships off the box should.
        Assert.DoesNotContain(entries, entry => entry.Message.Contains("movies", StringComparison.Ordinal));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The platform the app under test runs on, read independently of AllowedRootRules.PlatformStyle.
    private static T OnThisPlatform<T>(T linux, T windows) => OperatingSystem.IsWindows() ? windows : linux;

    // Where Jellyfin's /media and /config are inside the app on the platform running the tests: roots the page can save.
    private static string MediaRoot => OnThisPlatform("/media", @"D:\media");

    private static string ConfigRoot => OnThisPlatform("/config", @"D:\config");

    private static string Separator => OnThisPlatform("/", @"\");

    // A folder under the media root or the config root, in canonical form.
    private static string Media(params string[] names) => string.Join(Separator, names.Prepend(MediaRoot));

    private static string Config(params string[] names) => string.Join(Separator, names.Prepend(ConfigRoot));

    // The sample's libraries stored by a real sync, Jellyfin's /media and /config mapped to the platform's roots.
    private async Task SyncSampleLibrariesAsync(IReadOnlyList<PathMappingPair>? pathMappings = null)
    {
        var jellyfin = await _integrations.Create(
            "jellyfin",
            "Living room",
            apiKey: JellyfinApiKey,
            pathMappings: pathMappings ?? [new PathMappingPair("/media", MediaRoot), new PathMappingPair("/config", ConfigRoot)]);
        _jellyfin.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);
    }

    // The page as the owner reads it.
    private async Task<AllowedRootsResponse> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AllowedRootsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    // Each listed folder with its library names joined, so lists compare by value.
    private static List<(string AppPath, string LibraryNames, bool Ticked, ReplacementFolderStatus Status)> Folders(AllowedRootsResponse body) =>
        [.. body.LibraryFolders.Select(folder => (folder.AppPath, string.Join(", ", folder.LibraryNames), folder.Ticked, folder.Status))];

    /// <summary>That the save left the seeded root as the only root, wrote no switch row and no tick, and recorded
    /// nothing.</summary>
    private async Task AssertNothingSavedAsync(Guid seededRootId, CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            var root = await db.AllowedRoots.SingleAsync(cancellationToken);
            Assert.Equal((seededRootId, MediaRoot), (root.Id, root.Path));
            Assert.Equal(0, await db.ReplacementSettings.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementFolders.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
