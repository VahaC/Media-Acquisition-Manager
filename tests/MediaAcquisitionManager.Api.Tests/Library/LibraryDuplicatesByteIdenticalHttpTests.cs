using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-20 — byte-identical files on the duplicates list (GET /api/library/duplicates; spec §3.3 row 2, ADR 0022,
// ADR 0028): files of two identities or more whose bytes have one SHA-256, each with every name the app knows of
// it, the hash counted only under the identity, size and time of the app's last look at the name. Their room is
// counted once per file (ADR 0018), and what removing all but one copy would free is estimated only where it is
// sure (Vasyl, 2026-10-07): every copy's device on a file system whose files never share blocks — read from the
// mount table, asked only for the devices of byte-identical files — and no copy with a name the app never found;
// else no estimate, with why. A file a torrent seeds is classed and counted like any other, and no download
// client is asked (ADR 0018). Identities and hashes come from real inspections over HTTP, through the fake file
// inventory. The list's access, validation, paging and order are LibraryDuplicatesHttpTests'; the names of one
// file LibraryDuplicatesSameFileHttpTests'; how a hash is made FileInspectionHashHttpTests'.
public sealed class LibraryDuplicatesByteIdenticalHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    // In path order: the backup copy first, the download's name, the library's film.
    private const string CopyPath = "/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string LinkPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const string ContinentalLinkPath = "/media/tv/The Continental (2023)/The Continental S01E01.mkv";
    private const long TakenInode = 1_048_577;
    private const long CopyInode = 1_048_579;
    private const long ContinentalInode = 1_048_578;
    private const long TakenSizeBytes = 30_870_249_472;

    /// <summary>A second disk (8:17), where the backup copy lives, beside <see cref="TestMedia.DeviceId"/>.</summary>
    private const long BackupDeviceId = 2_065;

    /// <summary>A third disk (8:33), holding only a file with two names and no copy.</summary>
    private const long SeriesDeviceId = 2_081;

    /// <summary>The torrent's v1 info hash, as qBittorrent names it.</summary>
    private const string InfoHash = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    private const string CopyLockedError = "The app may not read “/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv”: permission denied.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private IFileHashDataFactory _hashes = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryDuplicatesByteIdenticalHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        _hashes = new FileHashDataFactory(db, _factory.Time);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        // The library the planted names come from; with no path mapping, the app looks for each where the
        // library says it is.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_groups_two_files_with_one_sha256_and_estimates_the_room_a_copy_takes_on_ext4()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var copy = await PlantFileAsync(CopyPath);
        var link = await PlantFileAsync(LinkPath);
        var taken = await PlantFileAsync(TakenPath);
        // The film with two names, the way a finished download is linked into the library, and a backup copied
        // from it: another inode and a later write, every byte the same.
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(LinkPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, sha256));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // The classes are independent: the film's two names are one file's, and its bytes are the backup's too.
        Assert.Equal([DuplicateClass.SameFile, DuplicateClass.ByteIdentical], list.Items.Select(group => group.Class));
        var group = Assert.Single(list.Items, item => item.Class == DuplicateClass.ByteIdentical);
        // The key MAM-21 addresses the group by: the hash's, so it stays while the same bytes are there.
        Assert.Equal(KeyOf($"ByteIdentical|{sha256}"), group.Key);
        Assert.Equal((false, 0), (group.NeedsReview, group.Distinctions.Count));
        Assert.Equal(((DuplicateIdentityDto?)null, (string?)sha256, 0, 0, (bool?)null), EvidenceOf(group));
        // A hardlinked copy brings every name it has, in path order, each with what the app read of it.
        Assert.Equal(
            [
                (copy.FileId, CopyPath, (long?)TakenSizeBytes, (long?)TestMedia.DeviceId, (long?)CopyInode, (int?)1, (string?)sha256),
                (link.FileId, LinkPath, (long?)TakenSizeBytes, (long?)TestMedia.DeviceId, (long?)TakenInode, (int?)2, (string?)sha256),
                (taken.FileId, TakenPath, (long?)TakenSizeBytes, (long?)TestMedia.DeviceId, (long?)TakenInode, (int?)2, (string?)sha256),
            ],
            group.Files.Select(file => (file.File.Id, file.File.ServicePath, file.ReadSizeBytes, file.DeviceId, file.Inode, file.LinkCount, file.Sha256)));
        // Each name with the work its library holds it in.
        Assert.Equal(
            [(copy.WorkId, (string?)"Living room"), (link.WorkId, (string?)"Living room"), (taken.WorkId, (string?)"Living room")],
            group.Files.Select(file => Assert.Single(file.Works)).Select(work => (work.Id, work.SourceName)));
        Assert.All(group.Files, file => Assert.Empty(file.Editions));
        // ADR 0018 — two files, three names: the room counted once per file, by the size the app read.
        Assert.Equal(new LibrarySpaceDto(2 * TakenSizeBytes, 2, 3, 0), group.Space);
        // On ext4 each copy takes its own room, and every name of each is known: removing all but one frees one
        // copy's size, and no more however many names the kept one has.
        Assert.Equal(new DuplicateRecoverableSpaceDto(TakenSizeBytes, null), group.Recoverable);
        Assert.Equal([TestMedia.DeviceId], _factory.Files.DescribedDevices);
        // Worked out from the two hashes the inspection kept, one per file; the list itself keeps nothing.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [(TakenInode, sha256), (CopyInode, sha256)],
                (await db.FileHashes.AsNoTracking().ToListAsync(cancellationToken))
                    .OrderBy(hash => hash.Inode)
                    .Select(hash => (hash.Inode, hash.Sha256))));
    }

    [Theory]
    [InlineData("btrfs")]
    [InlineData("zfs")]
    [InlineData("xfs")]
    public async Task List_gives_no_estimate_on_a_file_system_that_can_share_blocks(string fileSystem)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        // The film on ext4, its backup on a disk whose file system can share blocks: a reflinked copy there
        // may take no room of its own.
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3), BackupDeviceId);
        AddByteIdenticalCopies(copyFacts);
        _factory.Files.SetFileSystem(BackupDeviceId, fileSystem);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.ByteIdentical, group.Class);
        // Vasyl, 2026-10-07 — no estimate where it may be wrong, with why: one copy's file system is enough.
        Assert.Equal(new DuplicateRecoverableSpaceDto(null, RecoverableSpaceGap.SharingFileSystem), group.Recoverable);
        // The room the names lead to is still what the app read: two files.
        Assert.Equal(new LibrarySpaceDto(2 * TakenSizeBytes, 2, 2, 0), group.Space);
        Assert.Equal([TestMedia.DeviceId, BackupDeviceId], _factory.Files.DescribedDevices.Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nfs4")]
    [InlineData("fuse.mergerfs")]
    [InlineData("overlay")]
    public async Task List_gives_no_estimate_where_the_file_system_is_unknown(string? fileSystem)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        // The backup on a disk the mount table says nothing of, or on a share or layer whose server or lower
        // file system may share blocks without saying so.
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3), BackupDeviceId);
        AddByteIdenticalCopies(copyFacts);
        _factory.Files.SetFileSystem(BackupDeviceId, fileSystem);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.ByteIdentical, group.Class);
        // Unknown beats a guess (ADR 0028).
        Assert.Equal(new DuplicateRecoverableSpaceDto(null, RecoverableSpaceGap.UnknownFileSystem), group.Recoverable);
        Assert.Equal(new LibrarySpaceDto(2 * TakenSizeBytes, 2, 2, 0), group.Space);
    }

    [Fact]
    public async Task List_gives_no_estimate_when_a_file_has_a_name_the_app_never_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        var taken = await PlantFileAsync(TakenPath);
        // The film has a second name outside every folder the app reads (a download client's own folder): the
        // file system counts two names, the app knows one.
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, sha256));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // One known name of the film is no group of its names; its bytes are still the backup's.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.ByteIdentical, sha256), (group.Class, group.Evidence.Sha256));
        // Removing the names listed here would leave the bytes under the one the app never found: freeing them
        // can't be promised, and the page says why.
        Assert.Equal(new DuplicateRecoverableSpaceDto(null, RecoverableSpaceGap.UnlistedNames), group.Recoverable);
        Assert.Equal((int?)2, Assert.Single(group.Files, file => file.File.Id == taken.FileId).LinkCount);
        Assert.Equal(new LibrarySpaceDto(2 * TakenSizeBytes, 2, 2, 0), group.Space);
    }

    [Fact]
    public async Task List_gives_no_estimate_when_a_files_link_count_is_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        // The look read the film's device and inode but not how many names it has: one outside the app's folders
        // can't be ruled out.
        var takenFacts = new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, TestMedia.DeviceId, TakenInode, null);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, sha256));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An unknown count is no count of one: no estimate where the relationship is unknown.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.ByteIdentical, sha256), (group.Class, group.Evidence.Sha256));
        Assert.Equal(new DuplicateRecoverableSpaceDto(null, RecoverableSpaceGap.UnlistedNames), group.Recoverable);
        Assert.Equal([(int?)1, null], group.Files.Select(file => file.LinkCount));
    }

    [Fact]
    public async Task List_names_a_file_system_that_can_share_blocks_before_a_name_the_app_never_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        // Two reasons not to estimate: the film has a name the app never found, and the backup is on btrfs.
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3), BackupDeviceId);
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, sha256));
        _factory.Files.SetFileSystem(BackupDeviceId, "btrfs");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The file system comes first (ADR 0028): whatever names there are, a copy there may take no room of its own.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.ByteIdentical, group.Class);
        Assert.Equal(new DuplicateRecoverableSpaceDto(null, RecoverableSpaceGap.SharingFileSystem), group.Recoverable);
    }

    [Fact]
    public async Task List_makes_no_byte_identical_group_of_one_file_with_two_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(LinkPath);
        await PlantFileAsync(TakenPath);
        // The film's two names, and the hash of its bytes kept from when a copy of it was there: one identity with a
        // current hash.
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(LinkPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _hashes.Create(TestMedia.DeviceId, TakenInode, TakenSizeBytes, takenFacts.ModifiedUtc, sha256);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Two names of one file are no two copies: the names of one file's group alone, with the hash on each name.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.SameFile, group.Class);
        Assert.Equal([(LinkPath, (string?)sha256), (TakenPath, sha256)], group.Files.Select(file => (file.File.ServicePath, file.Sha256)));
        Assert.Empty(_factory.Files.DescribedDevices);
    }

    [Fact]
    public async Task List_classes_a_file_a_torrent_seeds_like_any_other_and_asks_no_download_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var download = await PlantFileAsync(LinkPath);
        var taken = await PlantFileAsync(TakenPath);
        // A download the library reports where qBittorrent left it, and the film the owner copied from it:
        // every byte the same.
        var downloadFacts = TestMedia.File(TakenInode, TakenSizeBytes);
        var takenFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(downloadFacts);
        _factory.Files.AddFile(LinkPath, downloadFacts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(takenFacts, sha256));
        // qBittorrent finished the download and seeds it from the media folders: a job of the owner's holds it,
        // and the client, which answers every call, lists the torrent and its file.
        var qbittorrent = new FakeQBittorrentServer();
        qbittorrent.AddTorrent(
            FakeQBittorrentTorrent.Of(new TorrentInfoHashes(InfoHash, null), "Taken (2008) BDRemux 1080p.mkv", "Movies", savePath: "/media/downloads") with
            {
                Files = [new FakeQBittorrentFile("Taken (2008) BDRemux 1080p.mkv", TakenSizeBytes, 1)],
            });
        qbittorrent.Arm(_factory.QBittorrentHttp);
        await PlantSeedingJobAsync(qbittorrent);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var askedBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // ADR 0018 — seeding changes nothing: the seeded file is a copy like any other, its room counted and
        // estimated the same, and no client is asked whether it holds it.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((DuplicateClass.ByteIdentical, sha256), (group.Class, group.Evidence.Sha256));
        Assert.Equal([download.FileId, taken.FileId], group.Files.Select(file => file.File.Id));
        Assert.Equal(new LibrarySpaceDto(2 * TakenSizeBytes, 2, 2, 0), group.Space);
        Assert.Equal(new DuplicateRecoverableSpaceDto(TakenSizeBytes, null), group.Recoverable);
        Assert.Equal(askedBefore, _factory.QBittorrentHttp.Requests.Count);
    }

    [Fact]
    public async Task List_makes_no_group_of_a_hash_kept_for_a_file_that_changed_since()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes);
        var copyFacts = TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3));
        var sha256 = FakeFileInventory.DefaultSha256(takenFacts);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        // The backup was written to since it was a copy, and this run couldn't read it whole: its new bytes have
        // no hash yet.
        _factory.Files.AddFile(
            CopyPath, copyFacts, TestMedia.Taken(), hash: new FileInventoryResult<FileContentHash>(FileInventoryStatus.Unreadable, null, CopyLockedError));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // What an earlier run kept of the backup while it was still a copy: the film's hash, under the key of the
        // bytes it held then — its identity and size, and the time of that older write.
        var previousModifiedUtc = copyFacts.ModifiedUtc.AddDays(-1);
        await _hashes.Create(TestMedia.DeviceId, CopyInode, TakenSizeBytes, previousModifiedUtc, sha256);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // ADR 0022 — a hash counts only under the key of the app's last look at the name: the backup's last look
        // read a later write, so the hash kept of its old bytes says nothing about the bytes it has now.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((0, 0), (list.Items.Count, list.TotalCount));
        // No byte-identical group, so no file system is asked about.
        Assert.Empty(_factory.Files.DescribedDevices);
        await _factory.WithDbAsync(async db =>
        {
            var copyName = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == CopyPath, cancellationToken);
            Assert.Equal(((long?)CopyInode, (DateTime?)copyFacts.ModifiedUtc), (copyName.Inode, copyName.ReadModifiedUtc));
            // The old hash is there, with the film's SHA-256; only its key doesn't match.
            Assert.Equal(
                [(TakenInode, takenFacts.ModifiedUtc, sha256), (CopyInode, previousModifiedUtc, sha256)],
                (await db.FileHashes.AsNoTracking().ToListAsync(cancellationToken))
                    .OrderBy(hash => hash.Inode)
                    .Select(hash => (hash.Inode, hash.ModifiedUtc, hash.Sha256)));
        });
    }

    [Fact]
    public async Task List_groups_a_scanned_copy_with_a_library_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The library's film, and a backup copy no library reports, which only the scan finds.
        var taken = await PlantFileAsync(TakenPath);
        var copyFacts = AddByteIdenticalCopies(TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3)));
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // MAM-18, ADR 0021 — a scanned name is a copy like a library's: the backup, held by no work, beside the
        // film the library holds.
        var group = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(DuplicateClass.ByteIdentical, group.Class);
        Assert.Equal(
            [
                (CopyPath, (string?)CopyPath, (string?)null, (long?)CopyInode, 0),
                (TakenPath, (string?)TakenPath, (string?)"Living room", (long?)TakenInode, 1),
            ],
            group.Files.Select(file => (file.File.ServicePath, file.File.AppPath, file.Works.FirstOrDefault()?.SourceName, file.Inode, file.Works.Count)));
        Assert.Equal(taken.WorkId, Assert.Single(group.Files[1].Works).Id);
        Assert.Null(group.Files[0].File.LocationName);
        Assert.Equal(new LibrarySpaceDto(2 * TakenSizeBytes, 2, 2, 0), group.Space);
        Assert.Equal(new DuplicateRecoverableSpaceDto(TakenSizeBytes, null), group.Recoverable);
        await _factory.WithDbAsync(async db =>
        {
            var scanned = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == CopyPath, cancellationToken);
            Assert.Equal((scanned.Id, ProvenanceSourceKind.FileInspection, (Guid?)null), (group.Files[0].File.Id, scanned.SourceKind, scanned.MediaVersionId));
            Assert.Equal((long?)copyFacts.SizeBytes, scanned.ReadSizeBytes);
        });
    }

    [Fact]
    public async Task List_asks_the_file_system_only_of_byte_identical_groups()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        await PlantFileAsync(ContinentalPath);
        await PlantFileAsync(ContinentalLinkPath);
        // The film and its backup, each on its own disk; and an episode with two names on a third disk, one
        // file with no copy.
        AddByteIdenticalCopies(TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddDays(3), BackupDeviceId));
        var episodeFacts = TestMedia.File(ContinentalInode, deviceId: SeriesDeviceId, linkCount: 2);
        _factory.Files.AddFile(ContinentalPath, episodeFacts, TestMedia.Continental());
        _factory.Files.AddFile(ContinentalLinkPath, episodeFacts, TestMedia.Continental());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Act
        var all = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);
        var askedForAll = _factory.Files.DescribedDevices;
        var others = new List<HttpResponseMessage>();
        // MAM-21 — and a page of decided matches, whatever the class: only a possible match is ever decided.
        foreach (var query in new[] { "class=SameFile", "class=SameWork", "class=Possible", "decision=Dismissed", "decision=Confirmed", "class=ByteIdentical&decision=Confirmed" })
            others.Add(await _factory.GetHttpRequest($"{BaseUrl}?{query}", _owner.AccessToken));
        var askedForOthers = _factory.Files.DescribedDevices;
        var byteIdentical = await _factory.GetHttpRequest($"{BaseUrl}?class=ByteIdentical", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.All(others, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(HttpStatusCode.OK, byteIdentical.StatusCode);
        Assert.Equal(
            [DuplicateClass.SameFile, DuplicateClass.ByteIdentical],
            (await ReadListAsync(all, cancellationToken)).Items.Select(group => group.Class));
        // Only the copies' devices, each once a request: the episode's disk decides nothing, as removing one of
        // its names frees nothing whatever its file system.
        Assert.Equal([TestMedia.DeviceId, BackupDeviceId], askedForAll.Order());
        // A page of another class, or of decided matches, shows no estimate, so it asks nothing.
        Assert.Equal(askedForAll, askedForOthers);
        Assert.Equal(
            new DuplicateRecoverableSpaceDto(TakenSizeBytes, null),
            Assert.Single((await ReadListAsync(byteIdentical, cancellationToken)).Items).Recoverable);
        // The byte-identical page asks again, for the same devices: nothing is kept between requests.
        Assert.Equal([TestMedia.DeviceId, BackupDeviceId], _factory.Files.DescribedDevices.Skip(askedForAll.Count).Order());
        Assert.DoesNotContain(SeriesDeviceId, _factory.Files.DescribedDevices);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A file name a library reported, in a work and version of its own — what a sync stores.
    private async Task<PlantedName> PlantFileAsync(string servicePath)
    {
        var work = await _works.Create();
        var version = await _versions.Create(
            work.Id, sourceItemId: Guid.NewGuid().ToString("N"), sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        var file = await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: _livingRoom.Id, sourceName: _livingRoom.Name);
        return new PlantedName(file.Id, work.Id);
    }

    // The film at TakenPath, and at CopyPath a copy with copyFacts whose bytes are the film's: both of Taken's
    // size and media facts, so the inspection reads both whole for their hash.
    private FileFacts AddByteIdenticalCopies(FileFacts copyFacts)
    {
        var takenFacts = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(TakenPath, takenFacts, TestMedia.Taken());
        _factory.Files.AddFile(CopyPath, copyFacts, TestMedia.Taken(), hash: FakeFileInventory.HashOf(copyFacts, FakeFileInventory.DefaultSha256(takenFacts)));
        return copyFacts;
    }

    // MAM-13, MAM-14 — the owner's job that sent the film to qBittorrent, finished, with its one file as the
    // client last listed it.
    private async Task PlantSeedingJobAsync(FakeQBittorrentServer qbittorrent)
    {
        var client = await _integrations.Create(
            "qbittorrent", "qBittorrent", qbittorrent.BaseUrl, userName: FakeQBittorrentServer.DefaultUserName, password: FakeQBittorrentServer.DefaultPassword);
        var destination = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Movies", "Movies");
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var release = await new ReleaseDataFactory(db, _factory.Resolve<IEncryptionService>(), _factory.Time)
            .Create(sourceConnectionId: null, title: "Taken (2008)", infoHash: InfoHash);
        var jobs = new DownloadJobDataFactory(db, _factory.Time);
        var job = await jobs.Create(
            release.Id, client.Id, client.Name, destination.Id, DownloadJobStatus.Downloaded, _owner.User.Id, infoHashV1: InfoHash, clientJobId: InfoHash, progress: 1);
        await jobs.AddFiles(job.Id, [new DownloadJobFileEntity { RelativePath = "Taken (2008) BDRemux 1080p.mkv", SizeBytes = TakenSizeBytes, Progress = 1, IsWanted = true }]);
    }

    // What the evidence of a byte-identical group holds: no identity, the hash, no ids, no titles, no durations.
    private static (DuplicateIdentityDto?, string?, int, int, bool?) EvidenceOf(LibraryDuplicateGroupDto group) =>
        (group.Evidence.Identity, group.Evidence.Sha256, group.Evidence.Identifiers.Count, group.Evidence.Titles.Count, group.Evidence.DurationsAgree);

    // ADR 0028 — a group's key: the first 16 bytes of the SHA-256 of its canonical text, in lowercase hex.
    private static string KeyOf(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, 16));

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>A planted library name: its row, and the work holding it.</summary>
    private sealed record PlantedName(Guid FileId, Guid WorkId);
}
