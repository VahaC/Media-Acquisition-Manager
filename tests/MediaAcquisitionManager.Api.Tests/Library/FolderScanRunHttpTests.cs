using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-18, ADR 0021 — one scan of the media folders end to end: the owner's request (POST /api/library/scans),
// the worker, and the run as GET /api/library/scans/{id} reads it back. The walk lists every media file name
// under the allowed roots; a file is reported when any name it was reached by is where the app finds a name
// a library reported (through the library's current mappings, a movie's or an episode's alike); every other
// file keeps exactly one stored name, with the app as its source and no version. The scan only finds names —
// nothing is looked at or read — and then queues the usual file inspection. A stored name the walk looked
// for and didn't find goes; one it couldn't look for (outside the roots, under a place it couldn't list) stays.
// A walk that stops short — at its limit, on a bug — stores nothing and queues nothing, and so does a scan
// whose media folders were all removed. Logs carry counts and ids, never a path. Shutdowns, the reconciler
// and the worker's own rules are FolderScanWorkerHttpTests'; what the queued inspection reads is
// FolderScanInspectionHttpTests'.
public sealed class FolderScanRunHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/scans";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string InterstellarPath = "/media/downloads/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv";
    private const string BirthdayPath = "/media/home/Birthday 2019.MP4";
    private const string InterstellarLinkPath = "/media/links/Interstellar (2014).mkv";
    private const string TakenLinkPath = "/media/links/Taken (2008).mkv";
    private const string UsbPath = "/media/usb";
    private const string HolidayPath = "/media/usb/Holiday 2018.mkv";
    private const string BackupLinkPath = "/media/links/backup";
    private const string BackupFilmPath = "/media/links/backup/Old Film (1999).mkv";
    private const string GonePath = "/media/downloads/Gone (2001)/Gone (2001).mkv";
    private const string LockedPath = "/media/downloads/Locked (2001)/Locked (2001).mkv";
    private const string DuneLinkPath = "/media/links/Dune (2021).mkv";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private const string YellowstoneEpisodePath = "/media/tv-shows/Yellowstone/Season 1/Yellowstone S01E01 Daybreak.mkv";

    private const string NoAllowedRootsError =
        "No media folders are allowed any more, so there was nothing to scan. Add one under Media folders in the user menu.";

    private const string AppErrorMessage = "The scan stopped because of an error in the app. Its log says more.";

    private const string LimitReachedError =
        "The media folders hold more than 200,000 media files or 100,000 folders, or links inside them lead to the same folders many times over, so the scan stopped.";

    private const string LinkLeadsOutProblem = "“/media/links/backup” leads out of the media folders, so the app doesn't follow it.";
    private const string UsbUnlistedProblem = "The app may not read “/media/usb”: permission denied.";
    private const string RootMissingProblem = "Nothing is at “/media”.";
    private const string RootEmptyProblem = "“/media” is empty; if it is a mount, it may be away, so the files found in it before are kept.";
    private const string LockedError = "The app may not read “/media/downloads/Locked (2001)/Locked (2001).mkv”: permission denied.";
    private const string DuneOutsideError = "“/media/links/Dune (2021).mkv” is outside the media folders the app may read.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public FolderScanRunHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Scan_finds_every_media_file_stores_a_name_for_each_one_no_library_reported_and_reads_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantLibraryFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(inode: 101), TestMedia.Taken());
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102), TestMedia.Taken());
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103), TestMedia.Continental());
        // Beside them, names that are not media files: a poster, a note, a download not yet finished.
        _factory.Files.AddFile("/media/downloads/Interstellar (2014)/poster.jpg", TestMedia.File(inode: 104));
        _factory.Files.AddFile("/media/downloads/Interstellar (2014)/Interstellar.nfo", TestMedia.File(inode: 105));
        _factory.Files.AddFile("/media/downloads/Dune (2021).mkv.part", TestMedia.File(inode: 106));
        var queuedUtc = Now();
        var start = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var ranUtc = Now();

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);
        var response = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        // Three media files, two of them in no library; the rest are no media files at all.
        Assert.Equal(new FolderScanRunDto(run.Id, FolderScanStatus.Succeeded, "owner", queuedUtc, ranUtc, ranUtc, 3, 2, 0, null, null), run);
        Assert.Equal([Root], Assert.Single(_factory.Files.ListedRoots));
        // The scan only finds names: nothing is looked at or read until the inspection it queued runs.
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);

        var rows = await ScanRowsAsync(cancellationToken);
        Assert.Equal([InterstellarPath, BirthdayPath], rows.Select(row => row.ServicePath));
        // The app is the source of these names: no connection, no item, no version, no library, no size a
        // library reported; the path is the app's own, and nothing has been read from it yet.
        Assert.All(rows, row =>
        {
            Assert.Equal(
                (ProvenanceSourceKind.FileInspection, null, null, null, null, null, null),
                (row.SourceKind, row.SourceConnectionId, row.SourceName, row.SourceItemId, row.MediaVersionId, row.LibraryLocationId, row.SizeBytes));
            Assert.Equal(LibrarySearchText.Normalize(row.ServicePath), row.NormalizedServicePath);
            Assert.Equal((ranUtc, ranUtc), (row.CreatedUtc, row.ObservedUtc));
            Assert.Equal(
                (null, null, null, null, null),
                (row.DeviceId, row.Inode, row.LinkCount, row.ReadSizeBytes, row.ReadModifiedUtc));
            Assert.Equal((null, null, null, null), (row.InspectionStatus, row.InspectionError, row.InspectedUtc, row.MediaInspectionId));
        });
        await _factory.WithDbAsync(async db =>
        {
            // The library's own name is the library's: the scan doesn't touch it.
            var library = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.Id == taken.Id, cancellationToken);
            Assert.Equal((taken.MediaVersionId, taken.ObservedUtc), (library.MediaVersionId, library.ObservedUtc));
            Assert.Equal(3, await db.PhysicalFiles.CountAsync(cancellationToken));
            // MAM-18 — and the usual inspection is queued to read what it found, nobody named as having asked.
            var inspection = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (FileInspectionTrigger.FolderScanned, FileInspectionRunStatus.Queued, null, ranUtc),
                (inspection.Trigger, inspection.Status, inspection.RequestedByUserId, inspection.CreatedUtc));
            Assert.Equal([inspection.Id], DrainInspectionQueue());
        });

        // Counts only: the app's log never names a path, which says how the owner's disks are laid out.
        var finished = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanRunner).FullName);
        Assert.Equal(
            (LogLevel.Information, $"Folder scan {run.Id} finished: 3 files found, 2 in no library, 0 places not scanned."),
            (finished.Level, finished.Message));
        AssertNoPathLogged();
    }

    [Fact]
    public async Task Scan_gives_the_inventory_the_allowed_roots_in_the_owners_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Stored second but listed first: the order is the owner's (Position), not the rows'.
        var roots = new AllowedRootDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>());
        await roots.Create("/srv/media", position: 1);
        await roots.Create(Root, position: 0);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddFile("/srv/media/Dune (2021).mkv", TestMedia.File(inode: 106));

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 2), (run.Status, run.FilesFound, run.FilesUnreported));
        Assert.Equal([Root, "/srv/media"], Assert.Single(_factory.Files.ListedRoots));
        Assert.Equal([InterstellarPath, "/srv/media/Dune (2021).mkv"], (await ScanRowsAsync(cancellationToken)).Select(row => row.ServicePath));
    }

    [Fact]
    public async Task Scan_counts_a_movies_and_an_episodes_file_as_reported_and_stores_no_name_for_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = new FakeJellyfinServer { ApiKey = ApiKey };
        server.Series.Add(JellyfinSamples.SeriesWith(ItemId('a'), "Yellowstone", 2018, "/media/tv-shows/Yellowstone", "tt4236770", "73586", "341164"));
        server.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('b'), "Daybreak", ItemId('a'), 1, 1, YellowstoneEpisodePath, "tt7529124", "6507316"));
        server.Arm(_factory.JellyfinHttp);
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        // Synced before any folder is allowed, so the sync queues no inspection of its own.
        var sync = await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        await _roots.Allow(Root);
        // The four sample movies and the episode where Jellyfin says they are, and one file it doesn't list.
        string[] libraryPaths = [ContinentalPath, District13Path, AliensTheatricalPath, AliensSpecialEditionPath, YellowstoneEpisodePath];
        foreach (var (path, index) in libraryPaths.Select((path, index) => (path, index)))
            _factory.Files.AddFile(path, TestMedia.File(inode: 200 + index));
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 5), (sync.Status, sync.FilesCount));
        // ADR 0017 — an episode's file is a file of the library like a movie's: matched by its path alone.
        Assert.Equal((FolderScanStatus.Succeeded, 6, 1), (run.Status, run.FilesFound, run.FilesUnreported));
        Assert.Equal([InterstellarPath], (await ScanRowsAsync(cancellationToken)).Select(row => row.ServicePath));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                libraryPaths.Order(StringComparer.Ordinal),
                (await db.PhysicalFiles.Where(file => file.MediaVersionId != null).Select(file => file.ServicePath).ToListAsync(cancellationToken))
                    .Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Scan_matches_by_the_librarys_current_mappings_and_by_path_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // The library sees the disk as /data; the app as /media.
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/data", "/media")]);
        await PlantLibraryFileAsync("/data/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv", jellyfin);
        // No mapping covers this one, so the app can't say where it is: nothing on the disk is its file.
        await PlantLibraryFileAsync("/data2/Interstellar (2014).mkv", jellyfin);
        _factory.Files.AddFile(TakenPath, TestMedia.File(inode: 101));
        _factory.Files.AddFile("/media/Interstellar (2014).mkv", TestMedia.File(inode: 102));
        // The same name as the library's in other letters: another file on Linux, never matched by its text.
        _factory.Files.AddFile("/media/Movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv", TestMedia.File(inode: 103));

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 3, 2), (run.Status, run.FilesFound, run.FilesUnreported));
        Assert.Equal(
            ["/media/Interstellar (2014).mkv", "/media/Movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv"],
            (await ScanRowsAsync(cancellationToken)).Select(row => row.ServicePath));
    }

    [Fact]
    public async Task Scan_counts_a_library_file_reached_through_a_folder_link_too_once_and_stores_no_name_for_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // An earlier scan found the film through the link alone, before the library listed it.
        await _physicalFiles.CreateUnreported(TakenLinkPath);
        await PlantLibraryFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(inode: 101));
        // Walked first, ordinally: the link's name is no library's, the file it leads to is.
        _factory.Files.AddLinkedName(TakenLinkPath, TakenPath);

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A file is reported when any name it was reached by is a library's, and counted once however many
        // names lead to it; the name stored for it before goes.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 0), (run.Status, run.FilesFound, run.FilesUnreported));
        Assert.Empty(await ScanRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Scan_of_a_file_no_library_reported_reached_by_two_names_stores_the_first_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddLinkedName(InterstellarLinkPath, InterstellarPath);
        var now = Now();

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // One file, one name stored: the first the walk reached it by.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (run.Status, run.FilesFound, run.FilesUnreported));
        var row = Assert.Single(await ScanRowsAsync(cancellationToken));
        Assert.Equal((InterstellarPath, now), (row.ServicePath, row.CreatedUtc));
    }

    [Fact]
    public async Task Scan_keeps_the_row_a_later_name_of_the_file_already_has_instead_of_storing_its_first_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // An earlier scan reached the file through the link only.
        var stored = await _physicalFiles.CreateUnreported(InterstellarLinkPath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddLinkedName(InterstellarLinkPath, InterstellarPath);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = Now();

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Stable across scans: the same row, first seen when it was, now seen again.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (run.Status, run.FilesFound, run.FilesUnreported));
        var row = Assert.Single(await ScanRowsAsync(cancellationToken));
        Assert.Equal((stored.Id, InterstellarLinkPath, stored.CreatedUtc, now), (row.Id, row.ServicePath, row.CreatedUtc, row.ObservedUtc));
    }

    [Fact]
    public async Task Scan_of_a_file_whose_two_names_both_have_rows_keeps_the_first_and_forgets_the_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var first = await _physicalFiles.CreateUnreported(InterstellarPath);
        await _physicalFiles.CreateUnreported(InterstellarLinkPath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddLinkedName(InterstellarLinkPath, InterstellarPath);

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // One file keeps one name, or the Library page would count it twice.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (run.Status, run.FilesFound, run.FilesUnreported));
        Assert.Equal((first.Id, InterstellarPath), Assert.Single((await ScanRowsAsync(cancellationToken)).Select(row => (row.Id, row.ServicePath))));
    }

    [Fact]
    public async Task Scan_counts_a_link_leading_out_as_a_problem_the_owner_reads_and_forgets_what_was_stored_under_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // An earlier scan stored a name under it, before the link was pointed out of the media folders.
        await _physicalFiles.CreateUnreported(BackupFilmPath);
        _factory.Files.AddFile(BackupFilmPath, TestMedia.File(inode: 107));
        _factory.Files.AddListingProblem(BackupLinkPath, FileListingStatus.Skipped, LinkLeadsOutProblem);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}/{run.Id}", _member.AccessToken);

        //Assert
        // Never followed: nothing under a link leading out is a name of the media folders, so the walk looked
        // and found nothing there. The owner reads which place it was; everyone else only that there was one.
        Assert.Equal(
            (FolderScanStatus.Succeeded, 1, 1, 1, LinkLeadsOutProblem),
            (run.Status, run.FilesFound, run.FilesUnreported, run.ProblemCount, run.Problem));
        var forMember = await ReadRunAsync(members, cancellationToken);
        Assert.Equal((1, null), (forMember.ProblemCount, forMember.Problem));
        Assert.Equal([InterstellarPath], (await ScanRowsAsync(cancellationToken)).Select(row => row.ServicePath));
        var finished = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanRunner).FullName);
        Assert.Equal($"Folder scan {run.Id} finished: 1 files found, 1 in no library, 1 places not scanned.", finished.Message);
        AssertNoPathLogged();
    }

    [Fact]
    public async Task Scan_stores_a_listed_name_the_inspection_then_finds_outside_the_roots_and_never_reads_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Listed inside the folders, but the look finds it leads out of them (a link changed in between).
        _factory.Files.AddProblem(DuneLinkPath, FileInventoryStatus.OutsideAllowedRoots, DuneOutsideError);
        var now = Now();

        //Act
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        // Stored as the walk found it; the inspection's look is what refuses it, and nothing is ever read.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal([DuneLinkPath], _factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        var row = Assert.Single(await ScanRowsAsync(cancellationToken));
        Assert.Equal(
            (DuneLinkPath, FileInspectionStatus.OutsideAllowedRoots, DuneOutsideError, now, null),
            (row.ServicePath, row.InspectionStatus, row.InspectionError, row.InspectedUtc, row.MediaInspectionId));
    }

    [Fact]
    public async Task Scan_stores_an_unreadable_file_and_the_inspection_after_it_shows_it_unreadable_both_succeeding()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddProblem(LockedPath, FileInventoryStatus.Unreadable, LockedError);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102), TestMedia.Taken());
        var now = Now();

        //Act
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        // The walk lists names without opening them, so a locked file is found like any other; that it can't
        // be read is that file's status after the inspection, never a failure of either run.
        Assert.Equal((FolderScanStatus.Succeeded, 2, 2, 0), (scan.Status, scan.FilesFound, scan.FilesUnreported, scan.ProblemCount));
        Assert.Equal([InterstellarPath], _factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            var inspection = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (FileInspectionTrigger.FolderScanned, FileInspectionRunStatus.Succeeded, 2, 2, 1),
                (inspection.Trigger, inspection.Status, inspection.FilesTotal, inspection.FilesChecked, inspection.FilesProbed));
        });
        var rows = (await ScanRowsAsync(cancellationToken)).ToDictionary(row => row.ServicePath);
        Assert.Equal(
            (FileInspectionStatus.Unreadable, LockedError, now, null),
            (rows[LockedPath].InspectionStatus, rows[LockedPath].InspectionError, rows[LockedPath].InspectedUtc, rows[LockedPath].MediaInspectionId));
        Assert.Equal(FileInspectionStatus.Inspected, rows[InterstellarPath].InspectionStatus);
    }

    [Fact]
    public async Task Scan_with_a_folder_it_cant_list_counts_the_problem_and_keeps_the_names_stored_under_it_as_they_were()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var holiday = await _physicalFiles.CreateUnreported(HolidayPath);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        // The file is still there, but the folder can't be listed now: nothing under it is walked.
        _factory.Files.AddFile(HolidayPath, TestMedia.File(inode: 108));
        _factory.Files.AddListingProblem(UsbPath, FileListingStatus.Unlisted, UsbUnlistedProblem);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A folder that is away costs no names: kept with its id and both of its times, not seen again.
        Assert.Equal(
            (FolderScanStatus.Succeeded, 1, 1, 1, UsbUnlistedProblem),
            (run.Status, run.FilesFound, run.FilesUnreported, run.ProblemCount, run.Problem));
        var rows = (await ScanRowsAsync(cancellationToken)).ToDictionary(row => row.ServicePath);
        Assert.Equal([InterstellarPath, HolidayPath], rows.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((holiday.Id, holiday.CreatedUtc, holiday.ObservedUtc), (rows[HolidayPath].Id, rows[HolidayPath].CreatedUtc, rows[HolidayPath].ObservedUtc));
    }

    [Theory]
    [InlineData(RootMissingProblem)]
    [InlineData(RootEmptyProblem)]
    public async Task Scan_of_a_root_it_cant_list_keeps_every_name_stored_under_it_with_its_reading(string problem)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var interstellar = await _physicalFiles.CreateUnreported(InterstellarPath);
        var birthday = await _physicalFiles.CreateUnreported(BirthdayPath);
        var reading = await new MediaInspectionDataFactory(db, _factory.Time).Create(TestMedia.DeviceId, 102, 3_891_719_630, TestMedia.ModifiedUtc);
        await _physicalFiles.Link(interstellar.Id, reading.Id);
        var before = await ScanRowsAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        // A mount that is away: missing, or an empty mount point.
        _factory.Files.AddListingProblem(Root, FileListingStatus.Unlisted, problem);

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(
            (FolderScanStatus.Succeeded, 0, 0, 1, problem),
            (run.Status, run.FilesFound, run.FilesUnreported, run.ProblemCount, run.Problem));
        // Every name and what was read from it, exactly as before.
        var after = await ScanRowsAsync(cancellationToken);
        Assert.Equal(before.Select(Snapshot), after.Select(Snapshot));
        Assert.Equal(new[] { birthday.Id, interstellar.Id }.Order(), after.Select(row => row.Id).Order());
        Assert.Equal(reading.Id, after.Single(row => row.Id == interstellar.Id).MediaInspectionId);
        await _factory.WithDbAsync(async check =>
            Assert.Equal(reading.Id, (await check.MediaInspections.AsNoTracking().SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Scan_keeps_a_name_stored_outside_every_current_root()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Found when /srv/old was allowed; the owner has since allowed /media only, perhaps by mistake.
        var outside = await _physicalFiles.CreateUnreported("/srv/old/Film (1999).mkv");
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        // On the disk still, but the walk never goes there.
        _factory.Files.AddFile("/srv/old/Film (1999).mkv", TestMedia.File(inode: 109));

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Not looked for, so not forgotten: the inspection shows it outside the media folders instead.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1), (run.Status, run.FilesFound, run.FilesUnreported));
        var rows = (await ScanRowsAsync(cancellationToken)).ToDictionary(row => row.ServicePath);
        Assert.Equal([InterstellarPath, "/srv/old/Film (1999).mkv"], rows.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((outside.Id, outside.ObservedUtc), (rows["/srv/old/Film (1999).mkv"].Id, rows["/srv/old/Film (1999).mkv"].ObservedUtc));
    }

    [Fact]
    public async Task Scan_again_forgets_a_gone_name_keeps_the_others_rows_and_forgets_one_a_library_now_reports()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(GonePath, TestMedia.File(inode: 110));
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        var firstUtc = Now();
        var first = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        var firstRows = (await ScanRowsAsync(cancellationToken)).ToDictionary(row => row.ServicePath);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        // One file deleted, and a library that now reports another.
        _factory.Files.Remove(GonePath);
        var reported = await PlantLibraryFileAsync(InterstellarPath);
        var secondUtc = Now();

        //Act
        var second = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 3, 3), (first.Status, first.FilesFound, first.FilesUnreported));
        Assert.Equal((FolderScanStatus.Succeeded, 2, 1), (second.Status, second.FilesFound, second.FilesUnreported));
        // The one still in no library keeps its row and when it was first seen, and is seen again now.
        var row = Assert.Single(await ScanRowsAsync(cancellationToken));
        Assert.Equal(
            (firstRows[BirthdayPath].Id, BirthdayPath, firstUtc, secondUtc),
            (row.Id, row.ServicePath, row.CreatedUtc, row.ObservedUtc));
        await _factory.WithDbAsync(async db =>
        {
            // Only the library's own name is left for the file a library now reports.
            var names = await db.PhysicalFiles.AsNoTracking().Where(file => file.ServicePath == InterstellarPath).ToListAsync(cancellationToken);
            Assert.Equal((reported.Id, reported.MediaVersionId), Assert.Single(names.Select(file => (file.Id, file.MediaVersionId))));
            Assert.Equal(0, await db.PhysicalFiles.CountAsync(file => file.ServicePath == GonePath, cancellationToken));
        });
    }

    [Fact]
    public async Task Scan_that_reaches_the_walks_limit_fails_with_its_sentence_and_stores_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        // Walked after the download and before the birthday: the walk stops there.
        _factory.Files.AddListingProblem("/media/extras", FileListingStatus.LimitReached, LimitReachedError);
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        var now = Now();

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // An incomplete walk replaces nothing: every name it didn't reach would be forgotten.
        Assert.Equal(
            new FolderScanRunDto(run.Id, FolderScanStatus.Failed, "owner", now, now, now, 0, null, 0, null, LimitReachedError),
            run);
        Assert.Equal([(gone.Id, GonePath, gone.ObservedUtc)], (await ScanRowsAsync(cancellationToken)).Select(row => (row.Id, row.ServicePath, row.ObservedUtc)));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
        Assert.Empty(DrainInspectionQueue());
        var failed = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanRunner).FullName);
        Assert.Equal(
            (LogLevel.Warning, $"Folder scan {run.Id} failed: the walk reached its limit after 1 files; nothing was stored."),
            (failed.Level, failed.Message));
    }

    [Fact]
    public async Task Scan_after_every_media_folder_was_removed_fails_with_the_sentence_and_walks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        var queuedUtc = Now();
        var start = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        // The owner empties the list while the scan waits for the worker.
        var removal = await _factory.PutHttpRequest("/api/settings/allowed-roots", _owner.AccessToken, new UpdateAllowedRootsRequest([]));
        _factory.Time.Advance(TimeSpan.FromSeconds(10));
        var ranUtc = Now();

        //Act
        await _factory.RunQueuedScansAsync(cancellationToken);
        var response = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        // The roots are read when the scan starts, not when it was queued: none then means nothing is walked.
        Assert.Equal(
            new FolderScanRunDto(run.Id, FolderScanStatus.Failed, "owner", queuedUtc, ranUtc, ranUtc, 0, null, 0, null, NoAllowedRootsError),
            run);
        Assert.Empty(_factory.Files.ListedRoots);
        Assert.Equal([gone.Id], (await ScanRowsAsync(cancellationToken)).Select(row => row.Id));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
        var failed = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanRunner).FullName);
        Assert.Equal(
            (LogLevel.Warning, $"Folder scan {run.Id} failed: no media folder is allowed any more."),
            (failed.Level, failed.Message));
    }

    [Fact]
    public async Task Scan_that_throws_mid_walk_fails_with_the_apps_sentence_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var gone = await _physicalFiles.CreateUnreported(GonePath);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(inode: 102));
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(inode: 103));
        // A bug, not an entry the inventory describes: nothing in the scan catches it.
        _factory.Files.DuringListing = (entry, _) => entry.AppPath == BirthdayPath
            ? throw new InvalidOperationException("A bug while walking.")
            : Task.CompletedTask;
        var now = Now();

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Failed with the app's sentence, never the exception's text; what the scans stored before stays.
        Assert.Equal(
            new FolderScanRunDto(run.Id, FolderScanStatus.Failed, "owner", now, now, now, 0, null, 0, null, AppErrorMessage),
            run);
        Assert.Equal([(gone.Id, GonePath)], (await ScanRowsAsync(cancellationToken)).Select(row => (row.Id, row.ServicePath)));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FolderScanWorker).FullName);
        Assert.Equal((LogLevel.Error, $"Folder scan {run.Id} threw out of the runner."), (error.Level, error.Message));
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task Scan_reports_how_many_files_it_found_while_it_walks()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var paths = Enumerable.Range(1, 120).Select(number => $"/media/downloads/Film {number:D3}.mkv").ToList();
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
            _factory.Files.AddFile(path, TestMedia.File(inode: 1_000 + index));
        // A link to the first file: a second name is no new file, so the count doesn't move for it.
        _factory.Files.AddLinkedName("/media/downloads/Film 050 link.mkv", paths[0]);
        // What the Library page reads while the folders are walked: a fresh context, as another request has.
        var seen = new Dictionary<string, (FolderScanStatus Status, int FilesFound, int? FilesUnreported)>();
        _factory.Files.DuringListing = async (entry, token) =>
        {
            if (entry.AppPath is "/media/downloads/Film 001.mkv" or "/media/downloads/Film 051.mkv" or "/media/downloads/Film 101.mkv")
            {
                await _factory.WithDbAsync(async db =>
                {
                    var run = await db.FolderScanRuns.AsNoTracking().SingleAsync(token);
                    seen[entry.AppPath] = (run.Status, run.FilesFound, run.FilesUnreported);
                });
            }
        };

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Written every fifty new files, so the page's count moves while a large disk is walked; distinct
        // files only, the link's name not counted.
        Assert.Equal((FolderScanStatus.Running, 0, (int?)null), seen["/media/downloads/Film 001.mkv"]);
        Assert.Equal((FolderScanStatus.Running, 50, (int?)null), seen["/media/downloads/Film 051.mkv"]);
        Assert.Equal((FolderScanStatus.Running, 100, (int?)null), seen["/media/downloads/Film 101.mkv"]);
        Assert.Equal((FolderScanStatus.Succeeded, 120, 120), (run.Status, run.FilesFound, run.FilesUnreported));
        Assert.Equal(120, (await ScanRowsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Scan_names_the_first_problem_in_walk_order_and_cuts_a_sentence_longer_than_its_column()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // A listing failure that says far more than a column holds (1,000 characters), walked first.
        var sentence = "The app couldn't list “/media/a”:" + string.Concat(Enumerable.Repeat(" The device is not ready.", 60));
        _factory.Files.AddListingProblem("/media/a", FileListingStatus.Unlisted, sentence);
        _factory.Files.AddListingProblem(BackupLinkPath, FileListingStatus.Skipped, LinkLeadsOutProblem);
        var cut = string.Concat(sentence.AsSpan(0, FolderScanRunEntity.ErrorMaximumLength - 1), "…");

        //Act
        var run = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.True(sentence.Length > FolderScanRunEntity.ErrorMaximumLength);
        Assert.Equal(1_000, cut.Length);
        // Both counted; the first named, cut where it is stored so the reader sees it was cut.
        Assert.Equal((FolderScanStatus.Succeeded, 2, cut), (run.Status, run.ProblemCount, run.Problem));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(cut, (await db.FolderScanRuns.AsNoTracking().SingleAsync(cancellationToken)).Problem));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    private static string ItemId(char letter) => new(letter, 32);

    // A movie with one version naming servicePath, reported by source when given — what a sync stores.
    private async Task<PhysicalFileEntity> PlantLibraryFileAsync(string servicePath, IntegrationConnectionEntity? source = null)
    {
        var work = await _works.Create();
        var version = await _versions.Create(work.Id, $"{Guid.NewGuid():N}", sourceConnectionId: source?.Id, sourceName: source?.Name);
        return await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: source?.Id, sourceName: source?.Name);
    }

    // The names the scans stored, in path order.
    private async Task<List<PhysicalFileEntity>> ScanRowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<PhysicalFileEntity>();
        await _factory.WithDbAsync(async db => rows = await db.PhysicalFiles
            .AsNoTracking()
            .Where(file => file.SourceKind == ProvenanceSourceKind.FileInspection && file.MediaVersionId == null)
            .ToListAsync(cancellationToken));
        return [.. rows.OrderBy(row => row.ServicePath, StringComparer.Ordinal)];
    }

    // Every column of a stored name, so "kept as it was" compares all of them.
    private static object Snapshot(PhysicalFileEntity row) =>
        (row.Id, row.ServicePath, row.NormalizedServicePath, row.CreatedUtc, row.ObservedUtc, row.DeviceId, row.Inode, row.LinkCount,
            (row.ReadSizeBytes, row.ReadModifiedUtc, row.InspectionStatus, row.InspectionError, row.InspectedUtc, row.MediaInspectionId));

    private static async Task<FolderScanRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<FolderScanRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    // Every inspection run id waiting for its worker; the queue is empty afterwards.
    private List<Guid> DrainInspectionQueue()
    {
        var queue = _factory.Resolve<FileInspectionQueue>();
        var runIds = new List<Guid>();
        while (queue.Reader.TryRead(out var runId))
            runIds.Add(runId);
        return runIds;
    }

    private void AssertNoPathLogged() =>
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(Root, StringComparison.Ordinal));
}
