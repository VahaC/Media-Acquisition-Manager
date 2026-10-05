using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-18 — the Media folders row of the Library page (GET /api/library/sources, its `scan`): the scan under
// way (the running one before the queued one), the latest finished one and whether one waits, for every
// signed-in user, who asked for one, its first problem and why one failed the owner's only. And where the
// app's look at the names the scans stored stands, counted when asked by the libraries' rule, over the names
// no library reports now: a sync that starts reporting a scanned path, or a fixed path mapping, takes the
// name off the count at once, without a rescan, while its row waits for the next scan to forget it.
public sealed class LibrarySourcesScanHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/sources";
    private const string ScansUrl = "/api/library/scans";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";
    private const string InterstellarPath = "/media/downloads/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv";
    private const string BirthdayPath = "/media/home/Birthday 2019.mkv";
    private const string LockedPath = "/media/downloads/Locked (2001)/Locked (2001).mkv";
    private const string GonePath = "/media/downloads/Gone (2001)/Gone (2001).mkv";
    private const string DunePath = "/media/movies/Dune (2021)/Dune (2021) 2160p.mkv";
    private const string LockedError = "The app may not read “/media/downloads/Locked (2001)/Locked (2001).mkv”: permission denied.";
    private const string GoneError = "Nothing is at “/media/downloads/Gone (2001)/Gone (2001).mkv”.";
    private const string RestartedError = "The app restarted before this scan finished, so nothing it found was stored. Scan the folders again.";
    private const string LinkLeadsOutProblem = "“/media/links/backup” leads out of the media folders, so the app doesn't follow it.";

    /// <summary>The Media folders row before any scan stored a name.</summary>
    private static readonly LibrarySourceInspectionDto NoFiles = new(0, 0, 0, null);

    /// <summary>MAM-19 — no file the app knows the room of.</summary>
    private static readonly LibrarySpaceDto NoSpace = new(0, 0, 0, 0);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IFolderScanRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public LibrarySourcesScanHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _runs = new FolderScanRunSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sources_before_any_scan_gives_an_empty_media_folders_row_even_without_a_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The row the page shows beside the libraries — there with no library at all, as the app's own source.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var list = (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Empty(list.Items);
        Assert.Equal(new FolderScanOverviewDto(null, null, false, NoFiles, NoSpace), list.Scan);
        // What the page reads, by these names.
        var scan = JsonNode.Parse(body)!["scan"]!.AsObject();
        Assert.Equal(["activeRun", "lastFinishedRun", "hasQueuedRun", "files", "space"], scan.Select(property => property.Key));
        Assert.Equal(
            ["inspectedCount", "problemCount", "notInspectedCount", "problem"],
            scan["files"]!.AsObject().Select(property => property.Key));
        Assert.Equal(
            ["sizeBytes", "fileCount", "nameCount", "unmeasuredCount"],
            scan["space"]!.AsObject().Select(property => property.Key));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FolderScanRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sources_gives_the_scan_the_owner_asked_for_as_the_active_run_while_it_waits()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        var queuedAt = _factory.Time.GetUtcNow().UtcDateTime;
        var start = await _factory.PostHttpRequest(ScansUrl, _owner.AccessToken);
        var started = (await start.Content.ReadFromJsonAsync<FolderScanRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var queued = new FolderScanRunDto(started.Id, FolderScanStatus.Queued, "owner", queuedAt, null, null, 0, null, 0, null, null);
        Assert.Equal(new FolderScanOverviewDto(queued, null, true, NoFiles, NoSpace), (await ReadListAsync(owners, cancellationToken)).Scan);
        // Who asked is the owner's sign-in name, so the owner's only.
        Assert.Equal(
            new FolderScanOverviewDto(queued with { RequestedByUserName = null }, null, true, NoFiles, NoSpace),
            (await ReadListAsync(members, cancellationToken)).Scan);
        await _factory.WithDbAsync(async db =>
            Assert.Equal((started.Id, FolderScanStatus.Queued), Assert.Single((await db.FolderScanRuns.AsNoTracking().ToListAsync(cancellationToken)).Select(run => (run.Id, run.Status)))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sources_gives_the_running_scan_as_the_active_run_before_the_queued_one(bool queuedFirst)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Either one created and inserted first, so the answer is the status's, not the clock's or the insert
        // order's.
        var (queuedAt, runningAt) = queuedFirst ? (now.AddMinutes(-2), now.AddMinutes(-1)) : (now.AddMinutes(-1), now.AddMinutes(-2));
        FolderScanRunEntity running;
        if (queuedFirst)
        {
            await _runs.Create(FolderScanStatus.Queued, _owner.User, queuedAt);
            running = await _runs.Create(FolderScanStatus.Running, _owner.User, runningAt, filesFound: 350);
        }
        else
        {
            running = await _runs.Create(FolderScanStatus.Running, _owner.User, runningAt, filesFound: 350);
            await _runs.Create(FolderScanStatus.Queued, _owner.User, queuedAt);
        }

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // What is happening now, with how many files it found so far; the queued one only means "and again
        // after this", which the page says.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new FolderScanOverviewDto(
                new FolderScanRunDto(running.Id, FolderScanStatus.Running, "owner", runningAt, runningAt, null, 350, null, 0, null, null),
                null,
                true,
                NoFiles, NoSpace),
            (await ReadListAsync(response, cancellationToken)).Scan);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [FolderScanStatus.Queued, FolderScanStatus.Running],
                (await db.FolderScanRuns.Select(run => run.Status).ToListAsync(cancellationToken)).Order()));
    }

    [Theory]
    [InlineData(FolderScanStatus.Succeeded)]
    [InlineData(FolderScanStatus.Failed)]
    public async Task Sources_gives_the_latest_succeeded_or_failed_scan_as_the_last_finished_run(FolderScanStatus latestStatus)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var (filesUnreported, latestError) = latestStatus == FolderScanStatus.Failed ? ((int?)null, RestartedError) : (12, (string?)null);
        // Seeded out of order and at explicit times, so "latest" is the clock's answer, not the insert order's
        // or the ids'; and a newer scan still under way is not a finished one.
        await _runs.Create(FolderScanStatus.Failed, _owner.User, now.AddMinutes(-2), filesFound: 40, error: RestartedError);
        var latest = await _runs.Create(latestStatus, _owner.User, now.AddMinutes(-1), filesFound: 1394, filesUnreported: filesUnreported, error: latestError);
        await _runs.Create(FolderScanStatus.Succeeded, _owner.User, now.AddMinutes(-3), filesFound: 1390, filesUnreported: 10);
        var running = await _runs.Create(FolderScanStatus.Running, _owner.User, now, filesFound: 50);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var latestAt = now.AddMinutes(-1);
        Assert.Equal(
            new FolderScanOverviewDto(
                new FolderScanRunDto(running.Id, FolderScanStatus.Running, "owner", now, now, null, 50, null, 0, null, null),
                new FolderScanRunDto(latest.Id, latestStatus, "owner", latestAt, latestAt, latestAt, 1394, filesUnreported, 0, null, latestError),
                false,
                NoFiles, NoSpace),
            (await ReadListAsync(response, cancellationToken)).Scan);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(4, await db.FolderScanRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sources_gives_who_asked_for_a_scan_its_first_problem_and_why_one_failed_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _runs.Create(FolderScanStatus.Succeeded, _owner.User, now.AddMinutes(-5), filesFound: 1394, filesUnreported: 12, problemCount: 3, problem: LinkLeadsOutProblem);
        await _runs.Create(FolderScanStatus.Failed, _owner.User, now.AddMinutes(-1), filesFound: 37, error: RestartedError);
        await _runs.Create(FolderScanStatus.Queued, _owner.User, now);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);
        var ownersRow = (await ReadListAsync(owners, cancellationToken)).Scan;
        var membersRow = (await ReadListAsync(members, cancellationToken)).Scan;

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        Assert.Equal(
            ("owner", "owner", RestartedError),
            (ownersRow.ActiveRun!.RequestedByUserName, ownersRow.LastFinishedRun!.RequestedByUserName, ownersRow.LastFinishedRun.Error));
        // Everyone else learns that one failed and that one waits, never why or who asked: the error is the
        // owner's diagnostics, and only the owner asks for one.
        Assert.DoesNotContain(RestartedError, await members.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.Equal(
            ownersRow with
            {
                ActiveRun = ownersRow.ActiveRun with { RequestedByUserName = null },
                LastFinishedRun = ownersRow.LastFinishedRun with { RequestedByUserName = null, Error = null },
            },
            membersRow);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                LinkLeadsOutProblem,
                await db.FolderScanRuns.Where(run => run.Status == FolderScanStatus.Succeeded).Select(run => run.Problem).SingleAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sources_gives_a_scans_first_problem_to_the_owner_only_and_its_count_to_everyone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1);
        var finished = await _runs.Create(FolderScanStatus.Succeeded, _owner.User, at, filesFound: 1394, filesUnreported: 12, problemCount: 3, problem: LinkLeadsOutProblem);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The sentence quotes a place on the owner's disks; how many there were is everyone's.
        var run = new FolderScanRunDto(finished.Id, FolderScanStatus.Succeeded, "owner", at, at, at, 1394, 12, 3, LinkLeadsOutProblem, null);
        Assert.Equal(new FolderScanOverviewDto(null, run, false, NoFiles, NoSpace), (await ReadListAsync(owners, cancellationToken)).Scan);
        Assert.Equal(
            new FolderScanOverviewDto(null, run with { RequestedByUserName = null, Problem = null }, false, NoFiles, NoSpace),
            (await ReadListAsync(members, cancellationToken)).Scan);
        Assert.DoesNotContain("/media", await members.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sources_counts_the_scans_names_by_their_last_look_and_gives_the_first_problem_in_path_order_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        // Read, locked, and away when the inspection looks; one more found by a second scan, not looked at yet.
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102), TestMedia.Taken());
        _factory.Files.AddProblem(LockedPath, FileInventoryStatus.Unreadable, LockedError);
        _factory.Files.AddFile(GonePath, TestMedia.File(inode: 110));
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        _factory.Files.Remove(GonePath);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        // Back by the second scan, which keeps its row and what the inspection found there.
        _factory.Files.AddFile(GonePath, TestMedia.File(inode: 110));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        var second = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 4, 4), (second.Status, second.FilesFound, second.FilesUnreported));
        // In path order "Gone" comes before "Locked": the first problem named is the gone one's.
        Assert.Equal(new LibrarySourceInspectionDto(1, 2, 1, GoneError), (await ReadListAsync(owners, cancellationToken)).Scan.Files);
        // The sentence quotes a path inside the app: the owner's, like each file's; the counts are everyone's.
        Assert.Equal(new LibrarySourceInspectionDto(1, 2, 1, null), (await ReadListAsync(members, cancellationToken)).Scan.Files);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [
                    (GonePath, (FileInspectionStatus?)FileInspectionStatus.NotFound),
                    (InterstellarPath, FileInspectionStatus.Inspected),
                    (LockedPath, FileInspectionStatus.Unreadable),
                    (BirthdayPath, null),
                ],
                (await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken))
                    .OrderBy(file => file.ServicePath, StringComparer.Ordinal)
                    .Select(file => (file.ServicePath, file.InspectionStatus))));
    }

    [Fact]
    public async Task Sources_takes_a_scanned_name_off_the_count_once_a_sync_reports_its_path_without_a_rescan()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        jellyfin.Arm(_factory.JellyfinHttp);
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(DunePath, TestMedia.File(inode: 111));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        var before = (await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken)).Scan.Files;
        // Jellyfin picks the film up, at the path the scan found it.
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(new string('d', 32), "Дюна", "Dune", 2021, DunePath, "tt1160419", "438631"));
        var sync = await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 5), (sync.Status, sync.FilesCount));
        Assert.Equal(new LibrarySourceInspectionDto(0, 0, 2, null), before);
        // Counted when asked, not stored with the scan: the library's name answers for the file at once.
        Assert.Equal(new LibrarySourceInspectionDto(0, 0, 1, null), (await ReadListAsync(response, cancellationToken)).Scan.Files);
        // The scan's row waits for the next scan to forget it.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                2,
                await db.PhysicalFiles.CountAsync(file => file.SourceKind == ProvenanceSourceKind.FileInspection && file.MediaVersionId == null, cancellationToken)));
    }

    [Fact]
    public async Task Sources_takes_a_scanned_name_off_the_count_once_the_owner_fixes_a_path_mapping()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        jellyfin.Movies.Clear();
        // The library sees the disk as /data: with no mapping the app can't tell its file is the one at /media.
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(new string('d', 32), "Дюна", "Dune", 2021, "/data/movies/Dune (2021)/Dune (2021) 2160p.mkv", "tt1160419", "438631"));
        jellyfin.Arm(_factory.JellyfinHttp);
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(DunePath, TestMedia.File(inode: 111));
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        var before = (await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken)).Scan.Files;
        // The owner maps the library's /data to the app's /media; no sync, no scan.
        var update = await _factory.PutHttpRequest(
            $"/api/settings/integrations/{livingRoom.Id}",
            _owner.AccessToken,
            new UpdateIntegrationRequest("Living room", livingRoom.BaseUrl, PathMappings: [new PathMappingRequest("/data", "/media")]));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal(new LibrarySourceInspectionDto(0, 0, 1, null), before);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        // Through the mappings as they are now (ADR 0003): the file is the library's, so no longer counted here.
        Assert.Equal(NoFiles, (await ReadListAsync(response, cancellationToken)).Scan.Files);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(DunePath, (await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.MediaVersionId == null, cancellationToken)).ServicePath);
            Assert.Equal(1, await db.PathMappings.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Sources_counts_a_scanned_name_beside_the_libraries_and_never_in_their_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
        var livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102), TestMedia.Taken());
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        // The inspection reads the scanned name and looks for the library's four, which aren't on this disk.
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Each source counts its own names: the library its four, the Media folders row the one it found.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        var library = Assert.Single(list.Items);
        Assert.Equal((0, 4, 0, 0), (library.Inspection.InspectedCount, library.Inspection.ProblemCount, library.Inspection.NotInspectedCount, library.FilesWithoutAppPathCount));
        Assert.Equal(new LibrarySourceInspectionDto(1, 0, 0, null), list.Scan.Files);
    }

    private static async Task<LibrarySourceListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
