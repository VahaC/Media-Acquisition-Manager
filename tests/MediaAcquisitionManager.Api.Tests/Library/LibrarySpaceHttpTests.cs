using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-19 — how much room the files really take on disk, on the Library page (GET /api/library/sources; Vasyl's
// decision 2): in each library's row, in the Media folders row and in total (the inspection's), each file
// counted once by its device and inode however many names lead to it (ADR 0018) — two hardlinks in one
// library, a library's name and the scan's, two libraries naming one file — and two files of one size twice.
// Only what the app's own look read counts (ADR 0006): a name not looked at yet, whose last look found nothing
// or no identity, or that lies outside the media folders, is counted as not measured and adds no bytes, and
// the size a library reported never counts. Counted from the names when asked, and the same for every
// signed-in user. Identities come from real inspections over HTTP, through the fake file inventory. The
// hashing step's own counts are FileInspectionHashHttpTests'.
public sealed class LibrarySpaceHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/sources";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    private const string CopyPath = "/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string LinkPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string GonePath = "/media/movies/Gone (2001)/Gone (2001).mkv";
    private const string UnknownPath = "/media/movies/Unknown (2001)/Unknown (2001).mkv";
    private const string LaterPath = "/media/movies/Later (2001)/Later (2001).mkv";
    private const string OutsidePath = "/srv/downloads/Taken (2008).mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const string BirthdayPath = "/media/home/Birthday 2019.mkv";
    private const long TakenInode = 1_048_577;
    private const long CopyInode = 1_048_579;
    private const long TakenSizeBytes = 30_870_249_472;

    /// <summary>What a library said of each of its files' size: never the app's measure of the room.</summary>
    private const long ReportedSizeBytes = 99_000_000_000;

    /// <summary>No name, or none the app looked at.</summary>
    private static readonly LibrarySpaceDto NoSpace = new(0, 0, 0, 0);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibrarySpaceHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // The library the planted names come from; with no path mapping, the app looks for each where the
        // library says it is.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sources_count_two_hardlink_names_of_one_file_in_a_library_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(_livingRoom, LinkPath);
        await PlantFileAsync(_livingRoom, TakenPath);
        // One inode with two names, the way a finished download is linked into the library.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // The ticket's "done when": two hardlink names count their space once — one file, two names.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        var once = new LibrarySpaceDto(TakenSizeBytes, 1, 2, 0);
        Assert.Equal(once, Assert.Single(list.Items).Space);
        Assert.Equal(once, list.Inspection.Space);
        Assert.Equal(NoSpace, list.Scan.Space);
        // Not sensitive: every signed-in user reads the same room.
        Assert.Equal(SpacesOf(list), SpacesOf(await ReadListAsync(members, cancellationToken)));
        await _factory.WithDbAsync(async db =>
            Assert.All(
                await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken),
                file => Assert.Equal(
                    ((long?)TestMedia.DeviceId, (long?)TakenInode, (long?)TakenSizeBytes, (int?)2),
                    (file.DeviceId, file.Inode, file.ReadSizeBytes, file.LinkCount))));
    }

    [Fact]
    public async Task Sources_count_a_file_a_library_and_the_scan_both_name_in_each_row_and_once_in_total()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(_livingRoom, TakenPath);
        // The download's own name, linked into the library: no library reports it, so only the scan stores it.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        // Each row counts the file its names lead to; the total counts it once, by its identity.
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 1, 0), Assert.Single(list.Items).Space);
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 1, 0), list.Scan.Space);
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 2, 0), list.Inspection.Space);
        Assert.Equal(SpacesOf(list), SpacesOf(await ReadListAsync(members, cancellationToken)));
    }

    [Fact]
    public async Task Sources_count_a_file_two_libraries_name_in_each_row_and_once_in_total()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Two Jellyfins over one disk: each reports the film at the same path.
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        await PlantFileAsync(_livingRoom, TakenPath);
        await PlantFileAsync(bedroom, TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        Assert.Equal(
            [("Bedroom", new LibrarySpaceDto(TakenSizeBytes, 1, 1, 0)), ("Living room", new LibrarySpaceDto(TakenSizeBytes, 1, 1, 0))],
            list.Items.Select(item => (item.Name, item.Space)));
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 2, 0), list.Inspection.Space);
        Assert.Equal(SpacesOf(list), SpacesOf(await ReadListAsync(members, cancellationToken)));
    }

    [Fact]
    public async Task Sources_count_two_files_of_one_size_twice()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(_livingRoom, CopyPath);
        await PlantFileAsync(_livingRoom, TakenPath);
        // A copy, not a link: another inode, so its own room on the disk.
        _factory.Files.AddFile(CopyPath, TestMedia.File(CopyInode, TakenSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        var twice = new LibrarySpaceDto(2 * TakenSizeBytes, 2, 2, 0);
        Assert.Equal(twice, Assert.Single(list.Items).Space);
        Assert.Equal(twice, list.Inspection.Space);
    }

    [Fact]
    public async Task Sources_count_names_without_a_measured_file_apart_and_never_the_size_a_library_reported()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Every name with the size the library reported, which says nothing about whether two names share bytes.
        await PlantFileAsync(_livingRoom, TakenPath, ReportedSizeBytes);
        await PlantFileAsync(_livingRoom, GonePath, ReportedSizeBytes);
        await PlantFileAsync(_livingRoom, UnknownPath, ReportedSizeBytes);
        await PlantFileAsync(_livingRoom, OutsidePath, ReportedSizeBytes);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        // Nothing at the gone one; a file without an identity at the unknown one; a real file outside the
        // media folders, which only the roots keep the app away from.
        _factory.Files.AddFile(UnknownPath, new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, null, null, 1), TestMedia.Taken());
        _factory.Files.AddFile(OutsidePath, TestMedia.File(CopyInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Synced after the inspection: a name the app hasn't looked at yet.
        await PlantFileAsync(_livingRoom, LaterPath, ReportedSizeBytes);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // ADR 0006 — only what the app read itself: the one file it measured, and four names it knows no file
        // behind, counted apart and never guessed into the room.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        var measured = new LibrarySpaceDto(TakenSizeBytes, 1, 1, 4);
        Assert.Equal(measured, Assert.Single(list.Items).Space);
        Assert.Equal(measured, list.Inspection.Space);
        Assert.Equal(SpacesOf(list), SpacesOf(await ReadListAsync(members, cancellationToken)));
        await _factory.WithDbAsync(async db =>
        {
            var files = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.ServicePath, cancellationToken);
            Assert.Equal(
                ((long?)ReportedSizeBytes, (long?)TakenSizeBytes),
                (files[TakenPath].SizeBytes, files[TakenPath].ReadSizeBytes));
            Assert.Equal(
                new FileInspectionStatus?[] { FileInspectionStatus.NotFound, FileInspectionStatus.IdentityUnknown, FileInspectionStatus.OutsideAllowedRoots, null },
                new[] { GonePath, UnknownPath, OutsidePath, LaterPath }.Select(path => files[path].InspectionStatus));
            // The look read the size beside no identity: without one, it can't tell which file it belongs to.
            Assert.Equal((long?)TakenSizeBytes, files[UnknownPath].ReadSizeBytes);
        });
    }

    [Fact]
    public async Task Sources_before_any_inspection_count_every_name_as_not_measured()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Three names a sync stored, with the sizes the library reported, and one a scan stored.
        await PlantFileAsync(_livingRoom, CopyPath, ReportedSizeBytes);
        await PlantFileAsync(_livingRoom, TakenPath, ReportedSizeBytes);
        await PlantFileAsync(_livingRoom, ContinentalPath, ReportedSizeBytes);
        await _physicalFiles.CreateUnreported(BirthdayPath);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // Nothing measured yet: no bytes, every name waiting for a look.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        Assert.Equal(new LibrarySpaceDto(0, 0, 0, 3), Assert.Single(list.Items).Space);
        Assert.Equal(new LibrarySpaceDto(0, 0, 0, 1), list.Scan.Space);
        Assert.Equal(new LibrarySpaceDto(0, 0, 0, 4), list.Inspection.Space);
        Assert.Equal(SpacesOf(list), SpacesOf(await ReadListAsync(members, cancellationToken)));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Sources_name_the_room_and_the_hashing_fields_as_the_page_reads_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(_livingRoom, TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // What the page reads, by these names, in each of the three places.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
        string[] space = ["sizeBytes", "fileCount", "nameCount", "unmeasuredCount"];
        var source = body["items"]![0]!.AsObject();
        Assert.Equal(
            ["integrationId", "name", "adapterDisplayName", "isEnabled", "canReadLibrary", "latestRun", "lastSucceededRun", "filesWithoutAppPathCount", "pathProblem", "pathProblemCode", "inspection", "space"],
            source.Select(property => property.Key));
        Assert.Equal(space, source["space"]!.AsObject().Select(property => property.Key));
        Assert.Equal(space, body["scan"]!["space"]!.AsObject().Select(property => property.Key));
        var inspection = body["inspection"]!.AsObject();
        Assert.Equal(["hasAllowedRoots", "activeRun", "lastFinishedRun", "hasQueuedRun", "space"], inspection.Select(property => property.Key));
        Assert.Equal(space, inspection["space"]!.AsObject().Select(property => property.Key));
        Assert.Equal(
            ["candidates", "checked", "read", "problemCount", "problem"],
            inspection["lastFinishedRun"]!["hashing"]!.AsObject().Select(property => property.Key));
        Assert.Equal(TakenSizeBytes, source["space"]!["sizeBytes"]!.GetValue<long>());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A file name library reported, with the size it said when given, in a work and version of its own — what a
    // sync stores.
    private async Task<PhysicalFileEntity> PlantFileAsync(IntegrationConnectionEntity library, string servicePath, long? reportedSizeBytes = null)
    {
        var work = await _works.Create();
        var version = await _versions.Create(
            work.Id, sourceItemId: Guid.NewGuid().ToString("N"), sourceConnectionId: library.Id, sourceName: library.Name);
        return await _physicalFiles.Create(
            version.Id, servicePath, sizeBytes: reportedSizeBytes, sourceConnectionId: library.Id, sourceName: library.Name);
    }

    // Every room the page shows: each library's, in the page's order, the Media folders row's and the total.
    private static List<LibrarySpaceDto> SpacesOf(LibrarySourceListResponse list) =>
        [.. list.Items.Select(item => item.Space), list.Scan.Space, list.Inspection.Space];

    private static async Task<LibrarySourceListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
