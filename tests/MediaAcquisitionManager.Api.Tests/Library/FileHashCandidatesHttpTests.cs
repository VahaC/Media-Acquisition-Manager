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

// MAM-19 — which files an inspection run reads whole for a hash (ADR 0022): a file is hashed only when a file
// of another identity has its size and could be its copy. Names count by identity: the names of one file
// (hardlinks) are one file, and the same file twice is no copy, so it is hashed once, through its first name in
// path order, and only when another file shares its size. Among files of one size the media inspector's facts
// a copy shares — how long it plays and, track by track, its kind, codec, picture size, channel count and
// language — tell files apart; a fact unknown on either side, or a file ffprobe couldn't read, tells nothing,
// and the facts another ffprobe may word differently (bit rate, profile, dynamic range, channel layout, flags)
// are not compared. So on the reference installation (MAM-17), where no two video files have one size and no
// file has a second name, nothing is read. A name the scan found counts like a library's; a file a torrent
// seeds is hashed like any other, with no download client asked (ADR 0018); a platform without file identity
// hashes nothing. Each run is the owner's Inspect files over HTTP. What a run records of the step is
// FileInspectionHashHttpTests'.
public sealed class FileHashCandidatesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string Root = "/media";

    // In path order: the backup first, the download client's copy, the library's film, a spare last.
    private const string CopyPath = "/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string DownloadPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string SparePath = "/media/usb/Taken (2008) BDRemux 1080p.mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const long CopyInode = 1_048_579;
    private const long DownloadInode = 1_048_583;
    private const long TakenInode = 1_048_577;
    private const long SpareInode = 1_048_581;
    private const long ContinentalInode = 1_048_578;
    private const long TakenSizeBytes = 30_870_249_472;

    /// <summary>The torrent's v1 info hash, as qBittorrent names it.</summary>
    private const string InfoHash = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    /// <summary>A run whose hashing step found no two files that could be copies of each other.</summary>
    private static readonly FileHashingDto NothingToHash = new(0, 0, 0, 0, null);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;

    public FileHashCandidatesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Inspect_never_hashes_a_file_whose_size_no_other_file_shares()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(CopyPath, TakenPath, SparePath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        // Taken's facts, but a size of its own, nobody else's: only the size tells it from the others, and no
        // file can be its copy, so its bytes are never read whole.
        _factory.Files.AddFile(SparePath, TestMedia.File(SpareInode, TakenSizeBytes + 1), TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The ticket's "done when": a file of a unique size is never hashed.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 3, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.FilesProbed, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(taken), KeyOf(copy)], await HashKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_of_files_all_of_different_sizes_hashes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The reference installation (MAM-17, 2026-10-04): no two video files have the same size and no file
        // a second name — even where the media facts agree, the size alone rules every copy out.
        var paths = new[] { CopyPath, DownloadPath, TakenPath, SparePath };
        await PlantFilesAsync(paths);
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
            _factory.Files.AddFile(path, TestMedia.File(TakenInode + (index * 10), TakenSizeBytes + index), TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 4, 4, NothingToHash), (run.Status, run.FilesChecked, run.FilesProbed, run.Hashing));
        Assert.Empty(_factory.Files.HashedPaths);
        Assert.Empty(await HashKeysAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, (await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken)).HashCandidates));
    }

    [Fact]
    public async Task Inspect_never_hashes_two_hardlink_names_of_one_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(DownloadPath, TakenPath);
        // One inode with two names, the way a finished download is linked into the library: their size is
        // shared by nothing but the file itself, and the same file twice is no copy.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(DownloadPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 1, NothingToHash), (run.Status, run.FilesChecked, run.FilesProbed, run.Hashing));
        Assert.Empty(_factory.Files.HashedPaths);
        Assert.Empty(await HashKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_of_two_hardlink_names_and_a_file_of_another_identity_of_their_size_hashes_each_file_once_through_its_first_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(TakenPath, DownloadPath, CopyPath);
        var linked = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(DownloadPath, linked, TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, linked, TestMedia.Taken());
        // A backup copy: another inode of that size, so it and the linked file could be copies of each other.
        var copy = AddTaken(CopyPath, CopyInode);

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Two files, not three names: the linked file is read once, through its first name in path order (the
        // download's), and the backup once.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, DownloadPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(linked), KeyOf(copy)], await HashKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_never_hashes_two_different_films_of_one_size()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(TakenPath, ContinentalPath);
        // A size two films happen to share: one plays 93 minutes, the other 86, with other tracks.
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode, TakenSizeBytes), TestMedia.Continental());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The same bytes give the same facts, so facts that differ rule a copy out without a read.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, NothingToHash), (run.Status, run.FilesProbed, run.Hashing));
        Assert.Empty(_factory.Files.HashedPaths);
        Assert.Empty(await HashKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("duration")]
    [InlineData("track count")]
    [InlineData("track kind")]
    [InlineData("codec")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("channels")]
    [InlineData("language")]
    public async Task Inspect_never_hashes_two_files_of_one_size_whose_compared_fact_differs(string fact)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(CopyPath, TakenPath);
        // One fact a byte-identical copy shares, told apart: everything else the same.
        _factory.Files.AddFile(CopyPath, TestMedia.File(CopyInode, TakenSizeBytes), TakenWith(fact));
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, NothingToHash), (run.Status, run.FilesProbed, run.Hashing));
        Assert.Empty(_factory.Files.HashedPaths);
        Assert.Empty(await HashKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("duration")]
    [InlineData("codec")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("channels")]
    [InlineData("language")]
    public async Task Inspect_hashes_two_files_of_one_size_whose_fact_only_one_of_them_knows(string fact)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(CopyPath, TakenPath);
        // ffprobe gave one of them no value for the fact: unknown tells nothing apart.
        var copy = TestMedia.File(CopyInode, TakenSizeBytes);
        var taken = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(CopyPath, copy, TakenWithout(fact));
        _factory.Files.AddFile(TakenPath, taken, TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(taken), KeyOf(copy)], await HashKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("bit rate")]
    [InlineData("profile")]
    [InlineData("dynamic range")]
    [InlineData("channel layout")]
    [InlineData("default track")]
    public async Task Inspect_hashes_two_files_of_one_size_whose_facts_differ_only_where_another_ffprobe_may_word_them_differently(string fact)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(CopyPath, TakenPath);
        // Read by two versions of ffprobe a year apart, say: one copy's facts word these differently.
        var copy = TestMedia.File(CopyInode, TakenSizeBytes);
        var taken = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(CopyPath, copy, TakenWith(fact));
        _factory.Files.AddFile(TakenPath, taken, TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Not compared, so they never set two copies apart.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(taken), KeyOf(copy)], await HashKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_hashes_a_file_of_a_shared_size_the_media_inspector_couldnt_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(CopyPath, TakenPath);
        // A damaged copy: found on disk, but ffprobe couldn't read its streams, so none of its facts are known.
        var copy = TestMedia.File(CopyInode, TakenSizeBytes);
        var taken = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(CopyPath, copy, media: null);
        _factory.Files.AddFile(TakenPath, taken, TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Unknown tells nothing apart: its bytes may still be the film's.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.FilesProbed, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(taken), KeyOf(copy)], await HashKeysAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                FileInspectionStatus.MediaUnreadable,
                (await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == CopyPath, cancellationToken)).InspectionStatus));
    }

    [Fact]
    public async Task Inspect_on_a_platform_without_file_identity_hashes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(CopyPath, TakenPath);
        // A platform without st_dev and st_ino (Windows): two files of one size, and no way to tell whether they
        // are one file under two names.
        _factory.Files.Capabilities = new FileInventoryCapabilities(ReportsFileIdentity: false);
        _factory.Files.AddFile(CopyPath, new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, null, null, 1), TestMedia.Taken());
        _factory.Files.AddFile(TakenPath, new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, null, null, 1), TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Nothing could be kept under a key that can't tell two files apart, so nothing is read for it.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, NothingToHash), (run.Status, run.FilesChecked, run.Hashing));
        Assert.Empty(_factory.Files.HashedPaths);
        Assert.Empty(await HashKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_hashes_a_file_a_torrent_seeds_like_any_other_and_asks_no_download_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFilesAsync(DownloadPath, TakenPath);
        var download = AddTaken(DownloadPath, DownloadInode);
        var taken = AddTaken(TakenPath, TakenInode);
        // qBittorrent finished the download and seeds it from the media folders: a job of the owner's holds
        // it, and the client, which answers every call, lists the torrent and its file.
        var qbittorrent = new FakeQBittorrentServer();
        qbittorrent.AddTorrent(
            FakeQBittorrentTorrent.Of(new TorrentInfoHashes(InfoHash, null), "Taken (2008) BDRemux 1080p.mkv", "Movies", savePath: "/media/downloads") with
            {
                Files = [new FakeQBittorrentFile("Taken (2008) BDRemux 1080p.mkv", TakenSizeBytes, 1)],
            });
        qbittorrent.Arm(_factory.QBittorrentHttp);
        await PlantSeedingJobAsync(qbittorrent);

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // ADR 0018 — seeding changes nothing: the file is read whole like any other, and no client is asked
        // whether it holds it.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 2, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([DownloadPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(taken), KeyOf(download)], await HashKeysAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task RunQueued_after_a_scan_hashes_a_scanned_file_and_a_library_file_of_one_size()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The library's film, and a backup copy no library reports, which only the scan finds.
        await PlantFilesAsync(TakenPath);
        var taken = AddTaken(TakenPath, TakenInode);
        var copy = AddTaken(CopyPath, CopyInode);
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        // MAM-18, ADR 0021 — a scanned name is read like a library's, and is a candidate like one.
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([KeyOf(taken), KeyOf(copy)], await HashKeysAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (FileInspectionTrigger.FolderScanned, FileInspectionRunStatus.Succeeded, (int?)2, 2, 2, 0),
                (run.Trigger, run.Status, run.HashCandidates, run.HashCandidatesChecked, run.HashCandidatesRead, run.HashProblemCount));
            var scanned = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.ServicePath == CopyPath, cancellationToken);
            Assert.Equal((ProvenanceSourceKind.FileInspection, (Guid?)null), (scanned.SourceKind, scanned.MediaVersionId));
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A movie with one version naming each of servicePaths — what a sync stores for the files it reported.
    private async Task PlantFilesAsync(params string[] servicePaths)
    {
        foreach (var servicePath in servicePaths)
        {
            var work = await _works.Create();
            var version = await _versions.Create(work.Id, $"{Guid.NewGuid():N}");
            await _physicalFiles.Create(version.Id, servicePath);
        }
    }

    // A file at appPath of Taken's size whose media facts are Taken's: as far as anything but its bytes can
    // tell, a copy of the film.
    private FileFacts AddTaken(string appPath, long inode)
    {
        var facts = TestMedia.File(inode, TakenSizeBytes);
        _factory.Files.AddFile(appPath, facts, TestMedia.Taken());
        return facts;
    }

    // MAM-13, MAM-14 — the owner's job that sent the film to qbittorrent, finished, with its one file as the
    // client last listed it.
    private async Task PlantSeedingJobAsync(FakeQBittorrentServer qbittorrent)
    {
        var client = await new IntegrationConnectionSeeder(_factory.Scope).Create(
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

    // Taken's media facts with one fact changed, as another file's reading would give them.
    private static MediaFacts TakenWith(string fact) => fact switch
    {
        // A millisecond is enough: the reading keeps the duration in whole milliseconds.
        "duration" => TestMedia.Taken() with { Duration = TimeSpan.FromMilliseconds(5_604_673) },
        "track count" => TestMedia.Taken() with { Tracks = [.. TestMedia.Taken().Tracks.SkipLast(1)] },
        "track kind" => WithTrack(5, track => track with { Kind = MediaTrackKind.Audio }),
        "codec" => WithTrack(0, track => track with { Codec = "hevc" }),
        "width" => WithTrack(0, track => track with { Width = 1918 }),
        "height" => WithTrack(0, track => track with { Height = 800 }),
        "channels" => WithTrack(1, track => track with { Channels = 2 }),
        "language" => WithTrack(1, track => track with { Language = "eng" }),
        "bit rate" => TestMedia.Taken() with { BitRate = 40_781_888 },
        "profile" => WithTrack(0, track => track with { Profile = "High 10" }),
        // A value on both sides, so it is a disagreement, not an unknown: a newer ffprobe that reads Dolby
        // Vision side data says so where an older one said HDR10 or SDR.
        "dynamic range" => WithTrack(0, track => track with { DynamicRange = DynamicRange.Hdr10 }),
        "channel layout" => WithTrack(1, track => track with { ChannelLayout = "5.1" }),
        "default track" => WithTrack(1, track => track with { IsDefault = false }),
        _ => throw new ArgumentOutOfRangeException(nameof(fact), fact, "Not a fact of a reading."),
    };

    // Taken's media facts with one fact ffprobe gave no value for.
    private static MediaFacts TakenWithout(string fact) => fact switch
    {
        "duration" => TestMedia.Taken() with { Duration = null },
        "codec" => WithTrack(0, track => track with { Codec = null }),
        "width" => WithTrack(0, track => track with { Width = null }),
        "height" => WithTrack(0, track => track with { Height = null }),
        "channels" => WithTrack(1, track => track with { Channels = null }),
        "language" => WithTrack(1, track => track with { Language = null }),
        _ => throw new ArgumentOutOfRangeException(nameof(fact), fact, "Not a fact a reading can lack."),
    };

    private static MediaFacts WithTrack(int position, Func<MediaTrackFacts, MediaTrackFacts> change)
    {
        var taken = TestMedia.Taken();
        return taken with { Tracks = [.. taken.Tracks.Select((track, index) => index == position ? change(track) : track)] };
    }

    // The key of every stored hash, in inode order.
    private async Task<List<HashKey>> HashKeysAsync(CancellationToken cancellationToken)
    {
        var keys = new List<HashKey>();
        await _factory.WithDbAsync(async db =>
            keys = [.. (await db.FileHashes.AsNoTracking().ToListAsync(cancellationToken))
                .Select(hash => new HashKey(hash.DeviceId, hash.Inode, hash.SizeBytes, hash.ModifiedUtc.Ticks))
                .OrderBy(key => key.Inode)]);
        return keys;
    }

    private static HashKey KeyOf(FileFacts facts) => new(facts.DeviceId!.Value, facts.Inode!.Value, facts.SizeBytes, facts.ModifiedUtc.Ticks);

    /// <summary>A hash's cache key, the modification time in ticks so a lost tick shows.</summary>
    private sealed record HashKey(long DeviceId, long Inode, long SizeBytes, long ModifiedTicks);
}
