using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the media libraries the Library page syncs from (GET /api/library/sources): every media
// library integration and nothing else, whether this build can read it, its latest and last successful
// sync, and how many of its files the current path mappings leave without a path in the app. For every
// signed-in user, except a run's error, which can name an address, the path problem, which quotes the
// owner's mappings, and who started a run, the owner's sign-in name: the owner's only (LibraryMapper,
// LibrarySyncService).
// MAM-9 — and where the app's own look at the files stands: per library, how many files it read, how
// many it couldn't (a file without an app path included) and how many it hasn't looked at, counted from
// the files when asked, with the first problem's sentence in path order for the owner only; for the whole
// list, whether any media folder is allowed, the inspection under way (the running one before the queued
// one) and the latest finished one, who asked for a run and why one failed again the owner's only.
public sealed class LibrarySourcesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/sources";
    private const string InspectionsUrl = "/api/library/inspections";
    private const string AllowedRootsUrl = "/api/settings/allowed-roots";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    /// <summary>MAM-9 — a source whose files the app hasn't looked at, or that has none.</summary>
    private static readonly LibrarySourceInspectionDto NoInspection = new(0, 0, 0, null, null);

    /// <summary>MAM-19 — a run that never reached its hashing step: queued, failed before it, or planted.</summary>
    private static readonly FileHashingDto NotHashed = new(null, 0, 0, 0, null);

    /// <summary>MAM-19 — no file the app knows the room of.</summary>
    private static readonly LibrarySpaceDto NoSpace = new(0, 0, 0, 0);

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string ContinentalLockedError = "The app may not read “" + ContinentalPath + "”: permission denied.";

    /// <summary>What the fake file inventory says about a path nothing is at.</summary>
    private const string AliensTheatricalGoneError = "Nothing is at “" + AliensTheatricalPath + "”.";

    private const string AliensTheatricalNotCoveredError = "No path mapping of this integration covers “" + AliensTheatricalPath + "”.";

    /// <summary>MAM-47 — the key of <see cref="AliensTheatricalNotCoveredError"/>'s sentence, the API's code for it.</summary>
    private const string NotCoveredCode = "integrations.path-map.not-covered";

    private const string RestartedError = "The app restarted before this inspection finished. What it had read is kept.";

    /// <summary>MAM-47 — <see cref="RestartedError"/> as the reconciler stores it: key and values (ADR 0042).</summary>
    private static readonly Message Restarted = Message.Of("files.inspection.restarted");

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public LibrarySourcesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A folder the allowed-roots API accepts on the platform the app runs on: its rules refuse
    /// a path of the other style (AllowedRootRules.PlatformStyle).</summary>
    private static string MediaFolder => OperatingSystem.IsWindows() ? @"D:\Media" : "/media";

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sources_without_a_token_is_401()
    {
        //Arrange
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sources_as_a_signed_in_non_owner_is_200_with_every_media_library_by_name_and_nothing_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await _integrations.Create("jellyfin", "living room", apiKey: ApiKey);
        var cellar = await _integrations.Create("jellyfin", "Cellar", apiKey: ApiKey, isEnabled: false);
        var attic = await _integrations.Create(FakeIntegrationAdapter.TypeId, "Attic");
        var basement = await _integrations.Create("retired-service", "Basement", kind: IntegrationKind.MediaLibrary);
        await _integrations.Create("qbittorrent", "Downloads", userName: "admin", password: "correct-horse-battery");
        await _integrations.Create("torznab", "Toloka", apiKey: ApiKey);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // By name, letter case ignored. Whether a type can read a library is the registry's answer: the
        // fake is a media library type without the library contract, and the retired type isn't
        // registered at all. A switched-off one is listed, keeping its snapshot, but can't be synced.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                new LibrarySourceDto(attic.Id, "Attic", FakeIntegrationAdapter.DisplayName, true, false, null, null, 0, null, null, NoInspection, NoSpace),
                new LibrarySourceDto(basement.Id, "Basement", null, true, false, null, null, 0, null, null, NoInspection, NoSpace),
                new LibrarySourceDto(cellar.Id, "Cellar", "Jellyfin", false, true, null, null, 0, null, null, NoInspection, NoSpace),
                new LibrarySourceDto(livingRoom.Id, "living room", "Jellyfin", true, true, null, null, 0, null, null, NoInspection, NoSpace),
            ],
            (await ReadListAsync(response, cancellationToken)).Items);
    }

    [Fact]
    public async Task Sources_gives_each_library_its_latest_run_and_its_last_successful_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var synced = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var neverSynced = await _integrations.Create("jellyfin", "Bedroom", apiKey: ApiKey);
        var runs = new LibrarySyncRunSeeder(_factory.Scope);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Seeded out of order and at explicit times, so "latest" is the clock's answer, not the insert
        // order's or the ids'.
        var lastSucceeded = await runs.Create(synced, LibrarySyncStatus.Succeeded, _owner.User, now.AddMinutes(-2));
        var latest = await runs.Create(
            synced,
            LibrarySyncStatus.Failed,
            _owner.User,
            now.AddMinutes(-1),
            Message.Of("http.status.server-error", ("service", "Jellyfin"), ("status", "500 Internal Server Error")));
        await runs.Create(synced, LibrarySyncStatus.Succeeded, _owner.User, now.AddMinutes(-3));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var items = (await ReadListAsync(response, cancellationToken)).Items;
        var source = items.Single(item => item.IntegrationId == synced.Id);
        Assert.Equal(
            new LibrarySyncRunDto(
                latest.Id, synced.Id, "Living room", LibrarySyncStatus.Failed, "owner", now.AddMinutes(-1), now.AddMinutes(-1), now.AddMinutes(-1),
                0, null, null, null, null, null, "Jellyfin answered 500 Internal Server Error. Its own log says why."),
            source.LatestRun);
        Assert.Equal((lastSucceeded.Id, LibrarySyncStatus.Succeeded), (source.LastSucceededRun!.Id, source.LastSucceededRun.Status));
        var never = items.Single(item => item.IntegrationId == neverSynced.Id);
        Assert.Equal((null, null), (never.LatestRun, never.LastSucceededRun));
    }

    [Fact]
    public async Task Sources_gives_the_error_of_a_failed_sync_and_who_started_each_run_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var succeeded = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        _factory.JellyfinHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError);
        var failed = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The owner reads why; the page still shows the library as the earlier sync stored it.
        var ownersSource = Assert.Single((await ReadListAsync(owners, cancellationToken)).Items);
        Assert.Equal(
            (failed.Id, "Couldn't reach Jellyfin at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that Jellyfin is running."),
            (ownersSource.LatestRun!.Id, ownersSource.LatestRun.Error));
        Assert.Equal(succeeded, ownersSource.LastSucceededRun);
        Assert.Equal(("owner", "owner"), (ownersSource.LatestRun.RequestedByUserName, ownersSource.LastSucceededRun!.RequestedByUserName));
        // Everyone else learns that it failed, never the address, and not who started either run: only
        // the owner starts one, so the name is the owner's sign-in name.
        var membersBody = await members.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("127.0.0.1", membersBody, StringComparison.Ordinal);
        var membersSource = Assert.Single((await ReadListAsync(members, cancellationToken)).Items);
        Assert.Equal((null, null), (membersSource.LatestRun!.RequestedByUserName, membersSource.LastSucceededRun!.RequestedByUserName));
        Assert.Equal(
            ownersSource with
            {
                LatestRun = ownersSource.LatestRun with { RequestedByUserName = null, Error = null },
                LastSucceededRun = ownersSource.LastSucceededRun with { RequestedByUserName = null },
            },
            membersSource);
    }

    [Fact]
    public async Task Sources_counts_the_files_the_current_path_mappings_give_no_path_and_says_why_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping for the shows only: none of the four movie files is covered.
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The first problem in path order, so the warning reads the same on every poll.
        var ownersSource = Assert.Single((await ReadListAsync(owners, cancellationToken)).Items);
        // MAM-47 — with the sentence's key beside it, the same in every language.
        Assert.Equal(
            (4, $"No path mapping of this integration covers “{AliensTheatricalPath}”.", NotCoveredCode),
            (ownersSource.FilesWithoutAppPathCount, ownersSource.PathProblem, ownersSource.PathProblemCode));
        // The reason quotes a path and the mappings, which are the owner's; the count is everyone's.
        var membersSource = Assert.Single((await ReadListAsync(members, cancellationToken)).Items);
        Assert.Equal((4, null, null), (membersSource.FilesWithoutAppPathCount, membersSource.PathProblem, membersSource.PathProblemCode));
    }

    [Fact]
    public async Task Sources_counts_files_without_an_app_path_with_the_mappings_as_they_are_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        // The owner fixes the mappings; no new sync.
        await _factory.PutHttpRequest(
            $"/api/settings/integrations/{jellyfin.Id}",
            _owner.AccessToken,
            new UpdateIntegrationRequest(
                "Living room",
                jellyfin.BaseUrl,
                PathMappings: [new PathMappingRequest("/media/tv-shows", "/data/tv"), new PathMappingRequest("/media/movies", "/data/movies")]));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Counted when asked, not stored with the run: the warning agrees with the works list at once.
        var source = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((0, null), (source.FilesWithoutAppPathCount, source.PathProblem));
    }

    [Fact]
    public async Task Sources_counts_each_librarys_read_problem_and_unread_files_and_gives_the_first_problem_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        var bedroomServer = new FakeJellyfinServer(BedroomBaseUrl) { ApiKey = ApiKey };
        // Two other movies, with paths of their own.
        bedroomServer.Movies.Clear();
        bedroomServer.Movies.AddRange(JellyfinSamples.Library(2));
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, new FakeJellyfinServer { ApiKey = ApiKey }, bedroomServer);
        var snapshots = new LibrarySnapshotSeeder(_factory.Scope);
        await snapshots.Sync(livingRoom, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        // In path order the two "Чужі" come first and the Continental last, the other way round from the
        // order the library listed them: a locked Continental, an unreadable District 13, nothing at the
        // theatrical "Чужі" and the special edition read.
        _factory.Files.AddProblem(ContinentalPath, FileInventoryStatus.Unreadable, ContinentalLockedError);
        _factory.Files.AddFile(District13Path, TestMedia.File(inode: 1_002), media: null);
        _factory.Files.AddFile(AliensSpecialEditionPath, TestMedia.File(inode: 1_004), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Synced after the inspection: files the app hasn't looked at yet.
        var bedroomSync = await snapshots.Sync(bedroom, _owner.User);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (bedroomSync.Status, bedroomSync.FilesCount));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        // Every reason the streams weren't read is a problem; the first in path order is the one named.
        Assert.Equal(
            [("Bedroom", new LibrarySourceInspectionDto(0, 0, 2, null, null)), ("Living room", new LibrarySourceInspectionDto(1, 3, 0, AliensTheatricalGoneError, Message.LegacyKey))],
            (await ReadListAsync(owners, cancellationToken)).Items.Select(item => (item.Name, item.Inspection)));
        // The sentence quotes a path inside the app: the owner's, like each file's; the counts are everyone's.
        Assert.Equal(
            [("Bedroom", new LibrarySourceInspectionDto(0, 0, 2, null, null)), ("Living room", new LibrarySourceInspectionDto(1, 3, 0, null, null))],
            (await ReadListAsync(members, cancellationToken)).Items.Select(item => (item.Name, item.Inspection)));
        await _factory.WithDbAsync(async db =>
        {
            var files = await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    (AliensTheatricalPath, FileInspectionStatus.NotFound),
                    (AliensSpecialEditionPath, FileInspectionStatus.Inspected),
                    (District13Path, FileInspectionStatus.MediaUnreadable),
                    (ContinentalPath, FileInspectionStatus.Unreadable),
                ],
                files
                    .Where(file => file.SourceConnectionId == livingRoom.Id)
                    .OrderBy(file => file.ServicePath, StringComparer.Ordinal)
                    .Select(file => (file.ServicePath, file.InspectionStatus!.Value)));
            Assert.Equal([null, null], files.Where(file => file.SourceConnectionId == bedroom.Id).Select(file => file.InspectionStatus));
        });
    }

    [Fact]
    public async Task Sources_counts_a_file_without_an_app_path_as_a_problem_with_the_mappers_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping for the shows only: none of the four movie files has a path in the app.
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        // A file at the service's path, inside the allowed root: still not the app's to guess at.
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(new LibrarySourceInspectionDto(0, 4, 0, AliensTheatricalNotCoveredError, NotCoveredCode), source.Inspection);
        // MAM-47 — the path warning's key is the first problem's: the page tells it is the same kind of reason by the
        // codes (LibrarySyncPanel) and doesn't say it twice, in any language.
        Assert.Equal((4, NotCoveredCode), (source.FilesWithoutAppPathCount, source.PathProblemCode));
        Assert.Empty(_factory.Files.DescribedPaths);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [FileInspectionStatus.NoAppPath, FileInspectionStatus.NoAppPath, FileInspectionStatus.NoAppPath, FileInspectionStatus.NoAppPath],
                await db.PhysicalFiles.Select(file => file.InspectionStatus!.Value).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sources_counts_a_file_whose_mappings_were_fixed_since_the_run_as_not_inspected_yet()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // The owner removes the mapping that left the movies without a path: they map as they are now.
        await _factory.WithDbAsync(db => db.PathMappings.Where(mapping => mapping.IntegrationConnectionId == jellyfin.Id).ExecuteDeleteAsync(cancellationToken));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // MAM-9 — the stored "no app path" of the last run is stale once the mappings give one: the four
        // files wait for the next run instead of staying problems with an old sentence, the way the path
        // warning, worked out now, drops at once (ADR 0003).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((0, new LibrarySourceInspectionDto(0, 0, 4, null, null)), (source.FilesWithoutAppPathCount, source.Inspection));
        await _factory.WithDbAsync(async db =>
            Assert.All(
                await db.PhysicalFiles.Select(file => file.InspectionStatus).ToListAsync(cancellationToken),
                status => Assert.Equal(FileInspectionStatus.NoAppPath, status)));
    }

    [Fact]
    public async Task Sources_gives_a_library_without_files_no_inspection_counts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 1_001), TestMedia.Continental());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // An inspection reads every library's files; one that has none has nothing to count.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [(bedroom.Id, NoInspection), (livingRoom.Id, new LibrarySourceInspectionDto(1, 3, 0, AliensTheatricalGoneError, Message.LegacyKey))],
            (await ReadListAsync(response, cancellationToken)).Items.Select(item => (item.IntegrationId, item.Inspection)));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.PhysicalFiles.CountAsync(file => file.SourceConnectionId == bedroom.Id, cancellationToken)));
    }

    [Fact]
    public async Task Sources_says_no_media_folder_is_allowed_until_the_owner_saves_one_and_saving_queues_no_inspection()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var before = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken);
        var saved = await _factory.PutHttpRequest(AllowedRootsUrl, _owner.AccessToken, new UpdateAllowedRootsRequest([MediaFolder]));

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // None by default: the app reads nothing, and the page says so — to everyone, with the way to
        // the settings for the owner.
        Assert.Equal(new FileInspectionOverviewDto(false, null, null, false, NoSpace), before.Inspection);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        // Saving starts nothing: the owner inspects when ready.
        Assert.Equal(new FileInspectionOverviewDto(true, null, null, false, NoSpace), (await ReadListAsync(owners, cancellationToken)).Inspection);
        Assert.Equal(new FileInspectionOverviewDto(true, null, null, false, NoSpace), (await ReadListAsync(members, cancellationToken)).Inspection);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.AllowedRoots.CountAsync(cancellationToken));
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Sources_gives_the_inspection_the_owner_asked_for_as_the_active_run_while_it_waits()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var queuedAt = _factory.Time.GetUtcNow().UtcDateTime;
        var start = await _factory.PostHttpRequest(InspectionsUrl, _owner.AccessToken);
        var started = await start.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var queued = new FileInspectionRunDto(
            started!.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Queued, "owner", queuedAt, null, null, null, 0, 0, NotHashed, null);
        Assert.Equal(new FileInspectionOverviewDto(true, queued, null, true, NoSpace), (await ReadListAsync(owners, cancellationToken)).Inspection);
        // Who asked is the owner's sign-in name, so the owner's only.
        Assert.Equal(
            new FileInspectionOverviewDto(true, queued with { RequestedByUserName = null }, null, true, NoSpace),
            (await ReadListAsync(members, cancellationToken)).Inspection);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (started.Id, FileInspectionRunStatus.Queued, _owner.User.Id, "owner"),
                (run.Id, run.Status, run.RequestedByUserId!.Value, run.RequestedByUserName));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sources_gives_the_running_inspection_as_the_active_run_before_the_queued_one(bool queuedFirst)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var runs = new FileInspectionRunSeeder(_factory.Scope);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Either one created and inserted first, so the answer is the status's, not the clock's or the
        // insert order's.
        var (queuedAt, runningAt) = queuedFirst ? (now.AddMinutes(-2), now.AddMinutes(-1)) : (now.AddMinutes(-1), now.AddMinutes(-2));
        FileInspectionRunEntity running;
        if (queuedFirst)
        {
            await runs.Create(FileInspectionRunStatus.Queued, FileInspectionTrigger.LibrarySynced, createdUtc: queuedAt);
            running = await runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.Requested, _owner.User, runningAt, filesTotal: 4, filesChecked: 1);
        }
        else
        {
            running = await runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.Requested, _owner.User, runningAt, filesTotal: 4, filesChecked: 1);
            await runs.Create(FileInspectionRunStatus.Queued, FileInspectionTrigger.LibrarySynced, createdUtc: queuedAt);
        }

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // What is happening now; the queued one only means "and again after this", which the page says.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new FileInspectionOverviewDto(
                true,
                new FileInspectionRunDto(
                    running.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Running, "owner", runningAt, runningAt, null, 4, 1, 0, NotHashed, null),
                null,
                true, NoSpace),
            (await ReadListAsync(response, cancellationToken)).Inspection);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [FileInspectionRunStatus.Queued, FileInspectionRunStatus.Running],
                (await db.FileInspectionRuns.Select(run => run.Status).ToListAsync(cancellationToken)).Order()));
    }

    [Theory]
    [InlineData(FileInspectionRunStatus.Succeeded)]
    [InlineData(FileInspectionRunStatus.Failed)]
    public async Task Sources_gives_the_latest_succeeded_or_failed_inspection_as_the_last_finished_run(FileInspectionRunStatus latestStatus)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var runs = new FileInspectionRunSeeder(_factory.Scope);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var latestError = latestStatus == FileInspectionRunStatus.Failed ? RestartedError : null;
        var latestReason = latestError is null ? null : Restarted;
        // Seeded out of order and at explicit times, so "latest" is the clock's answer, not the insert
        // order's or the ids'; and a newer run still under way is not a finished one.
        await runs.Create(FileInspectionRunStatus.Failed, FileInspectionTrigger.AppStarted, createdUtc: now.AddMinutes(-2), error: Restarted, filesTotal: 4, filesChecked: 2);
        var latest = await runs.Create(latestStatus, FileInspectionTrigger.LibrarySynced, createdUtc: now.AddMinutes(-1), error: latestReason, filesTotal: 4, filesChecked: 4, filesProbed: 2);
        await runs.Create(FileInspectionRunStatus.Succeeded, FileInspectionTrigger.Requested, _owner.User, now.AddMinutes(-3), filesTotal: 4, filesChecked: 4, filesProbed: 4);
        var running = await runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.AppStarted, createdUtc: now, filesTotal: 4, filesChecked: 1);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var latestAt = now.AddMinutes(-1);
        Assert.Equal(
            new FileInspectionOverviewDto(
                true,
                new FileInspectionRunDto(running.Id, FileInspectionTrigger.AppStarted, FileInspectionRunStatus.Running, null, now, now, null, 4, 1, 0, NotHashed, null),
                new FileInspectionRunDto(latest.Id, FileInspectionTrigger.LibrarySynced, latestStatus, null, latestAt, latestAt, latestAt, 4, 4, 2, NotHashed, latestError),
                false, NoSpace),
            (await ReadListAsync(response, cancellationToken)).Inspection);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(4, await db.FileInspectionRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sources_gives_who_asked_for_an_inspection_and_why_one_failed_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var runs = new FileInspectionRunSeeder(_factory.Scope);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await runs.Create(FileInspectionRunStatus.Failed, FileInspectionTrigger.Requested, _owner.User, now.AddMinutes(-5), Restarted, filesTotal: 4, filesChecked: 2);
        await runs.Create(FileInspectionRunStatus.Running, FileInspectionTrigger.Requested, _owner.User, now.AddMinutes(-1), filesTotal: 4);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var ownersOverview = (await ReadListAsync(owners, cancellationToken)).Inspection;
        Assert.Equal(
            ("owner", "owner", RestartedError),
            (ownersOverview.ActiveRun!.RequestedByUserName, ownersOverview.LastFinishedRun!.RequestedByUserName, ownersOverview.LastFinishedRun.Error));
        // Everyone else learns that one failed and that one runs, never why or who asked: the error is
        // the owner's diagnostics, and only the owner asks for one.
        Assert.DoesNotContain(RestartedError, await members.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.Equal(
            ownersOverview with
            {
                ActiveRun = ownersOverview.ActiveRun with { RequestedByUserName = null },
                LastFinishedRun = ownersOverview.LastFinishedRun with { RequestedByUserName = null, Error = null },
            },
            (await ReadListAsync(members, cancellationToken)).Inspection);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                RestartedError,
                TestTexts.En(await db.FileInspectionRuns.Where(run => run.Status == FileInspectionRunStatus.Failed).Select(run => run.Error).SingleAsync(cancellationToken))));
    }

    private static async Task<LibrarySourceListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
