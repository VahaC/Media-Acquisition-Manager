using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
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

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-20 — the first class of specification §3.3 on GET /api/library/duplicates (ADR 0028): the names of one file,
// grouped by the device and inode the app's own look read (ADR 0006), two names or more. The file's room is counted
// once (ADR 0018), and removing one of its names frees nothing until the last goes, so the estimate is none, and
// certain. One path is one name however many libraries report it, a name the folder scan found joins the library's,
// and a scan row of a path a library reports now is left out (ADR 0021). A name the app has no identity for is no
// one's hardlink. Identities come from real inspections and scans over HTTP, through the fake file inventory. Access,
// validation, order and wire names are LibraryDuplicatesHttpTests'; byte-identical copies
// LibraryDuplicatesByteIdenticalHttpTests'.
public sealed class LibraryDuplicatesSameFileHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    // In path order: the download's own name first, the library's after.
    private const string LinkPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string LaterPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.part2.mkv";

    /// <summary>Where the bedroom's Jellyfin sees <see cref="TakenPath"/>: mapped to it, and after the media folders in
    /// path order.</summary>
    private const string BedroomTakenPath = "/srv/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";

    private const long TakenInode = 1_048_577;
    private const long TakenSizeBytes = 30_870_249_472;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryDuplicatesSameFileHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    public async Task List_groups_two_hardlink_names_of_one_file_and_counts_its_space_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Two items of one library over one file: the download, linked into the movies folder.
        var link = await PlantFileAsync(_livingRoom, LinkPath);
        var taken = await PlantFileAsync(_livingRoom, TakenPath);
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var rowsBefore = await RowCountsAsync(cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}?class=SameFile", _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (owners.StatusCode, members.StatusCode));
        var list = await ReadListAsync(owners, cancellationToken);
        Assert.Equal(1, list.TotalCount);
        var group = Assert.Single(list.Items);
        Assert.Matches("^[0-9a-f]{32}$", group.Key);
        // One file, two names: the bytes say so, so nothing is left to review.
        Assert.Equal((DuplicateClass.SameFile, false), (group.Class, group.NeedsReview));
        Assert.Empty(group.Distinctions);
        Assert.Equal(new DuplicateIdentityDto(TestMedia.DeviceId, TakenInode, 2, 2), group.Evidence.Identity);
        Assert.Equal(((string?)null, (bool?)null), (group.Evidence.Sha256, group.Evidence.DurationsAgree));
        Assert.Empty(group.Evidence.Identifiers);
        Assert.Empty(group.Evidence.Titles);
        // ADR 0018 — the ticket's "done when": the file's room once, with both its names.
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 2, 0), group.Space);
        Assert.Equal(new DuplicateRecoverableSpaceDto(0, null), group.Recoverable);
        // Each name as the Library shows it, in path order, with what the app's look read and the work holding it.
        Assert.Collection(
            group.Files,
            file => AssertName(file, link, LinkPath),
            file => AssertName(file, taken, TakenPath));
        // Not sensitive: every signed-in user reads the same group.
        var membersGroup = Assert.Single((await ReadListAsync(members, cancellationToken)).Items);
        Assert.Equal((group.Key, group.Class, group.Space), (membersGroup.Key, membersGroup.Class, membersGroup.Space));
        // Worked out on the read, stored nowhere.
        Assert.Equal(rowsBefore, await RowCountsAsync(cancellationToken));
    }

    [Fact]
    public async Task List_says_removing_a_hardlink_name_frees_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(_livingRoom, LinkPath);
        await PlantFileAsync(_livingRoom, TakenPath);
        // Three names, one outside the folders the app reads, on a file system that can share blocks: none of that
        // changes what removing a name of one file frees.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 3);
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Files.SetFileSystem(TestMedia.DeviceId, "btrfs");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.SameFile, group.Class);
        // The bytes stay while any name leads to them: none recovered, and that is certain — no gap to explain.
        Assert.Equal(new DuplicateRecoverableSpaceDto(0, null), group.Recoverable);
        Assert.Equal(new DuplicateIdentityDto(TestMedia.DeviceId, TakenInode, 3, 2), group.Evidence.Identity);
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 2, 0), group.Space);
        // No byte-identical group, so no file system was asked about.
        Assert.Empty(_factory.Files.DescribedDevices);
    }

    [Fact]
    public async Task List_makes_no_group_of_one_path_two_libraries_report()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Two Jellyfins over one disk, each reporting the film at the same path under the same IMDb id: one name of
        // one file, so neither a hardlink nor another copy.
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
        await PlantFileAsync(_livingRoom, TakenPath, "tt0936501");
        await PlantFileAsync(bedroom, TakenPath, "tt0936501");
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        // Both rows carry the identity the app read: one path, not an unknown one, is why there is no group.
        await _factory.WithDbAsync(async db =>
            Assert.All(
                await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken),
                file => Assert.Equal(((long?)TestMedia.DeviceId, (long?)TakenInode), (file.DeviceId, file.Inode))));
    }

    [Fact]
    public async Task List_groups_a_scanned_hardlink_name_with_the_librarys_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(_livingRoom, TakenPath);
        // The download's own name, linked into the library: no library reports it, so only the scan stores it.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var link = await RowAtAsync(LinkPath, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.SameFile, group.Class);
        Assert.Equal(new DuplicateIdentityDto(TestMedia.DeviceId, TakenInode, 2, 2), group.Evidence.Identity);
        Assert.Equal(new LibrarySpaceDto(TakenSizeBytes, 1, 2, 0), group.Space);
        // The scan's name holds no work and sits in no library; the library's holds its work.
        Assert.Collection(
            group.Files,
            file =>
            {
                Assert.Equal((link.Id, LinkPath, LinkPath, (string?)null), (file.File.Id, file.File.ServicePath, file.File.AppPath, file.File.LocationName));
                Assert.Equal(((long?)TestMedia.DeviceId, (long?)TakenInode, (int?)2), (file.DeviceId, file.Inode, file.LinkCount));
                Assert.Empty(file.Works);
            },
            file => AssertName(file, taken, TakenPath));
    }

    [Fact]
    public async Task List_leaves_out_a_scanned_name_a_library_reports_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Scanned before any library reported the film: both names of the file are the scan's.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(LinkPath, facts, TestMedia.Taken());
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var link = await RowAtAsync(LinkPath, cancellationToken);
        var stale = await RowAtAsync(TakenPath, cancellationToken);
        // Then the bedroom's Jellyfin reports the film, at a path of its own that maps to the scanned one — after it
        // in path order, so the stale scan row would be the one shown if it were read.
        var bedroom = await _integrations.Create(
            "jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey, pathMappings: [new PathMappingPair("/srv", Root)]);
        var reported = await PlantFileAsync(bedroom, BedroomTakenPath);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 2), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        // ADR 0021 — the scan row stays until the next scan, but the library's name stands for the path now: two
        // names of the file, never three, and the stale row is not one of them.
        Assert.Equal(new DuplicateIdentityDto(TestMedia.DeviceId, TakenInode, 2, 2), group.Evidence.Identity);
        Assert.Equal([link.Id, reported.Id], group.Files.Select(file => file.File.Id));
        Assert.DoesNotContain(stale.Id, group.Files.Select(file => file.File.Id));
        var library = group.Files[1];
        Assert.Equal((BedroomTakenPath, (string?)TakenPath, PathMapStatus.Mapped), (library.File.ServicePath, library.File.AppPath, library.File.PathStatus));
        // Held through the bedroom's own version: the work the library reports at the path now.
        Assert.Equal((reported.MediaVersionId!.Value, (string?)"Bedroom"), (Assert.Single(library.Works).VersionId, library.Works[0].SourceName));
        // A read: the stale row is the next scan's to remove, not this list's.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(3, await db.PhysicalFiles.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task List_makes_no_group_of_names_whose_identity_is_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(_livingRoom, LinkPath);
        await PlantFileAsync(_livingRoom, TakenPath);
        // Found and of one size, each said to have two names, but without a device and inode: nothing tells whether
        // they are one file.
        var withoutIdentity = new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, null, null, 2);
        _factory.Files.AddFile(LinkPath, withoutIdentity, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, withoutIdentity, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // And one name the app hasn't looked at yet.
        await PlantFileAsync(_livingRoom, LaterPath);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // ADR 0006 — an unknown identity is never guessed into a shared one.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
        Assert.Empty(_factory.Files.DescribedDevices);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                new (string, FileInspectionStatus?, long?)[]
                {
                    (LinkPath, FileInspectionStatus.IdentityUnknown, null),
                    (TakenPath, FileInspectionStatus.IdentityUnknown, null),
                    (LaterPath, null, null),
                },
                (await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken))
                    .OrderBy(file => file.ServicePath, StringComparer.Ordinal)
                    .Select(file => (file.ServicePath, file.InspectionStatus, file.DeviceId))));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A file name a library reported, in a work and version of its own — what a sync stores — with the IMDb id the
    // item carries when given, and no title, so no possible match by title comes into it.
    private async Task<PhysicalFileEntity> PlantFileAsync(IntegrationConnectionEntity library, string servicePath, string? imdbId = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        return await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
    }

    // A library's name of the Taken file as the list shows it: the row, its path as the app finds it, what the look
    // read, and the one work holding it.
    private static void AssertName(LibraryDuplicateFileDto file, PhysicalFileEntity row, string appPath)
    {
        Assert.Equal((row.Id, row.ServicePath, (string?)appPath, PathMapStatus.Identity), (file.File.Id, file.File.ServicePath, file.File.AppPath, file.File.PathStatus));
        Assert.Equal(
            ((long?)TakenSizeBytes, (long?)TestMedia.DeviceId, (long?)TakenInode, (string?)null),
            (file.ReadSizeBytes, file.DeviceId, file.Inode, file.Sha256));
        Assert.Empty(file.Editions);
        Assert.Equal(row.MediaVersionId, Assert.Single(file.Works).VersionId);
    }

    // The scan's row of a path, as it was stored.
    private async Task<PhysicalFileEntity> RowAtAsync(string servicePath, CancellationToken cancellationToken)
    {
        PhysicalFileEntity? row = null;
        await _factory.WithDbAsync(async db =>
            row = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == servicePath, cancellationToken));
        return row!;
    }

    // What the list could write if it wrote anything: names, works, hashes and the audit log.
    private async Task<(int Names, int Works, int Hashes, int AuditEvents)> RowCountsAsync(CancellationToken cancellationToken)
    {
        (int, int, int, int) counts = default;
        await _factory.WithDbAsync(async db =>
            counts = (
                await db.PhysicalFiles.CountAsync(cancellationToken),
                await db.MediaWorks.CountAsync(cancellationToken),
                await db.FileHashes.CountAsync(cancellationToken),
                await db.AuditEvents.CountAsync(cancellationToken)));
        return counts;
    }

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
