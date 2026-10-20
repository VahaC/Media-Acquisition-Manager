using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-9 — one inspection run end to end: the owner's request (POST /api/library/inspections), the worker,
// and the run as GET /api/library/inspections/{id} reads it back. Every file name the libraries reported is
// mapped to the app's path through its integration's mappings, looked at inside the allowed roots (in the
// owner's order) and read by the media inspector, and what each showed is written on the name: the
// reading with its facts and tracks and the file's identity, or the one sentence saying why the streams
// weren't read. A file that can't be read shows its status instead of failing the run; a path two names
// share is looked at once; reads run at most two at a time; the run reports its progress while it goes,
// fails with a sentence when no media folder is allowed any more or when the app throws (keeping what it
// had written), and leaves a run it didn't claim alone. What a run keeps for the next one is
// FileInspectionCacheHttpTests'.
public sealed class FileInspectionRunHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string Root = "/media";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const string LockedPath = "/media/movies/Locked (2001)/Locked (2001).mkv";
    private const string OutsidePath = "/srv/downloads/Taken (2008).mkv";
    private const string GonePath = "/media/movies/Gone (2001)/Gone (2001).mkv";
    private const long TakenInode = 1_048_577;
    private const long ContinentalInode = 1_048_578;
    private const long TakenSizeBytes = 30_870_249_472;

    private const string NoAllowedRootsError =
        "No media folders are allowed any more, so there was nothing to inspect. Add one under Media folders in the user menu.";

    private const string IdentityUnknownError =
        "This platform gives files no identity (device and inode), so the app can't keep what it reads from them.";

    private const string UnreadableError = "The app isn't allowed to read “/media/movies/Locked (2001)/Locked (2001).mkv”.";

    private const string MediaUnreadableError =
        "ffprobe couldn't read “/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv” as media: Invalid data found when processing input.";

    private const string NotFoundError = "Nothing is at “/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv”.";

    private const string GoneNotFoundError = "Nothing is at “/media/movies/Gone (2001)/Gone (2001).mkv”.";

    private const string AppErrorMessage = "The inspection stopped because of an error in the app. Its log says more.";

    private const string OutsideAllowedRootsError = "“/srv/downloads/Taken (2008).mkv” is outside the media folders the app may read.";

    private const string NoAppPathError = "No path mapping of this integration covers “/data/movies/Taken (2008).mkv”.";

    private const string InspectorUnavailableError = "ffprobe couldn't be started: “/usr/bin/ffprobe” was not found.";

    private const string ChangedWhileReadingError =
        "“/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv” changed while ffprobe was reading it.";

    /// <summary>MAM-19 — a run that never reached its hashing step: queued, failed before it, or planted.</summary>
    private static readonly FileHashingDto NotHashed = new(null, 0, 0, 0, null);

    /// <summary>MAM-19 — a run whose hashing step found no two files that could be copies of each other.</summary>
    private static readonly FileHashingDto NothingToHash = new(0, 0, 0, 0, null);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;

    public FileInspectionRunHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    public async Task Inspect_reads_every_file_and_stores_its_facts_tracks_and_identity()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        var continental = await PlantFileAsync(ContinentalPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode), TestMedia.Continental());
        var queuedUtc = Now();
        var start = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var ranUtc = Now();

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var response = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        // Both names looked at, both read by the inspector, finished when the worker ran it.
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Succeeded, "owner", queuedUtc, ranUtc, ranUtc, 2, 2, 2, NothingToHash, null),
            run);
        Assert.Equal([TakenPath, ContinentalPath], _factory.Files.DescribedPaths.Order(StringComparer.Ordinal));
        Assert.Equal([TakenPath, ContinentalPath], _factory.Files.ReadPaths.Order(StringComparer.Ordinal));

        var readings = await ReadingsAsync(cancellationToken);
        Assert.Equal(2, readings.Count);
        var takenReading = readings.Single(reading => reading.Inode == TakenInode);
        var continentalReading = readings.Single(reading => reading.Inode == ContinentalInode);
        // The key to the tick: an unsigned st_dev past long.MaxValue stored bit for bit, and a modification
        // time with sub-millisecond ticks kept exactly, or the next run could never find it again.
        Assert.Equal(ReadingKey.Of(TestMedia.File(TakenInode, TakenSizeBytes)), ReadingKey.Of(takenReading));
        Assert.Equal(ReadingKey.Of(TestMedia.File(ContinentalInode)), ReadingKey.Of(continentalReading));
        Assert.Equal(0x8000_0000_0000_0803UL, unchecked((ulong)takenReading.DeviceId));
        Assert.Equal(
            (null, 5_604_672L, 40_781_887L, ranUtc),
            (TestTexts.En(takenReading.Error), takenReading.DurationMilliseconds, takenReading.BitRate, takenReading.CreatedUtc));
        Assert.Equal(
            (null, 5_179_168L, 6_011_343L, ranUtc),
            (TestTexts.En(continentalReading.Error), continentalReading.DurationMilliseconds, continentalReading.BitRate, continentalReading.CreatedUtc));

        // Every track in the file's own order, every field as the inspector read it.
        var tracks = await TracksAsync(cancellationToken);
        Assert.Equal(TestMedia.Taken().Tracks, TracksOf(tracks, takenReading.Id));
        Assert.Equal(TestMedia.Continental().Tracks, TracksOf(tracks, continentalReading.Id));
        Assert.Equal(
            Enumerable.Range(0, 11),
            tracks.Where(track => track.MediaInspectionId == takenReading.Id).Select(track => track.Position).Order());
        Assert.Equal(20, tracks.Count);

        var files = await FileStatesAsync(cancellationToken);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, ranUtc, takenReading.Id, TestMedia.DeviceId, TakenInode, 1),
            files[taken.Id]);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, ranUtc, continentalReading.Id, TestMedia.DeviceId, ContinentalInode, 1),
            files[continental.Id]);

        // Counts only: the app's log never names a path, which says how the owner's disks are laid out.
        var finished = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionRunner).FullName);
        Assert.Equal(LogLevel.Information, finished.Level);
        Assert.Equal(
            $"File inspection {run.Id} finished: 2 of 2 files checked, 2 read by the media inspector; 0 files could be copies, 0 read whole for their hash, 0 not hashed.",
            finished.Message);
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(Root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_gives_the_inventory_the_allowed_roots_in_the_owners_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Stored second but listed first: the order is the owner's (Position), not the rows'.
        var roots = new AllowedRootDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>());
        await roots.Create("/srv/media", position: 1);
        await roots.Create(Root, position: 0);
        await PlantFileAsync(TakenPath);
        await PlantFileAsync("/srv/media/movies/Taken (2008).mkv");
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile("/srv/media/movies/Taken (2008).mkv", TestMedia.File(ContinentalInode), TestMedia.Taken());

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 2), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        // Two looks and two reads, each given the roots as they were when the run started.
        Assert.Equal(4, _factory.Files.RootsGiven.Count);
        Assert.All(_factory.Files.RootsGiven, given => Assert.Equal([Root, "/srv/media"], given));
    }

    [Fact]
    public async Task Inspect_looks_at_and_reads_a_path_two_versions_name_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var first = await PlantFileAsync(TakenPath);
        var second = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Two names checked, one path looked at, one file read.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.DescribedPaths);
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        var files = await FileStatesAsync(cancellationToken);
        var expected = new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode, 1);
        Assert.Equal(expected, files[first.Id]);
        Assert.Equal(expected, files[second.Id]);
    }

    [Fact]
    public async Task Inspect_of_an_unreadable_file_beside_a_good_one_succeeds_and_shows_it_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var locked = await PlantFileAsync(LockedPath);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddProblem(LockedPath, FileInventoryStatus.Unreadable, UnreadableError);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var works = await _factory.GetHttpRequest("/api/library/works", _owner.AccessToken);

        //Assert
        // The ticket's "done when": one file the app can't open is that file's status, not the run's failure.
        Assert.Equal(
            (FileInspectionRunStatus.Succeeded, 2, 2, 1, (string?)null),
            (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed, run.Error));
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        var files = await FileStatesAsync(cancellationToken);
        // No reading and no identity: nothing about the bytes is known.
        Assert.Equal(new FileState(FileInspectionStatus.Unreadable, UnreadableError, now, null, null, null, null), files[locked.Id]);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode, 1),
            files[taken.Id]);

        // The Library page shows the status and the owner's sentence, and no media facts: unknown, not guessed.
        Assert.Equal(HttpStatusCode.OK, works.StatusCode);
        var listed = (await works.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!
            .Items
            .SelectMany(work => work.Versions)
            .SelectMany(version => version.Files)
            .ToDictionary(file => file.Id);
        Assert.Equal(new LibraryFileInspectionDto(FileInspectionStatus.Unreadable, UnreadableError, now, null), listed[locked.Id].Inspection);
        var inspected = listed[taken.Id].Inspection!;
        Assert.Equal((FileInspectionStatus.Inspected, (string?)null, now), (inspected.Status, inspected.Problem, inspected.InspectedUtc));
        Assert.Equal((5_604_672L, 40_781_887L, 11), (inspected.Media!.DurationMilliseconds, inspected.Media.BitRate, inspected.Media.Tracks.Count));
    }

    [Fact]
    public async Task Inspect_of_a_file_the_inspector_cant_read_stores_its_sentence_as_a_reading_without_tracks()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        // Found by the file system, but not media the inspector can read.
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The inspector ran, so the file counts as read, and the run still succeeds.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        // Cached like a success under the file's key, so a damaged file is read again only once it changes.
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal(ReadingKey.Of(TestMedia.File(TakenInode, TakenSizeBytes)), ReadingKey.Of(reading));
        Assert.Equal(
            (MediaUnreadableError, (long?)null, (long?)null, now),
            (TestTexts.En(reading.Error), reading.DurationMilliseconds, reading.BitRate, reading.CreatedUtc));
        Assert.Empty(await TracksAsync(cancellationToken));
        Assert.Equal(
            new FileState(FileInspectionStatus.MediaUnreadable, MediaUnreadableError, now, reading.Id, TestMedia.DeviceId, TakenInode, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_of_a_name_its_mappings_dont_cover_is_no_app_path_and_never_looked_at()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "jellyfin", "Living room", pathMappings: [new PathMappingPair("/srv/tv", "/media/tv")]);
        var uncovered = await PlantFileAsync("/data/movies/Taken (2008).mkv", jellyfin);
        var mapped = await PlantFileAsync("/srv/tv/The Continental S01E01.mkv", jellyfin);
        // Where the mapping puts the covered one; nothing anywhere for the other.
        _factory.Files.AddFile("/media/tv/The Continental S01E01.mkv", TestMedia.File(ContinentalInode), TestMedia.Continental());
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        // ADR 0003 — the app looks where the integration's mappings send a name, and nowhere for a name they
        // don't cover: no guess, the mapper's sentence instead.
        Assert.Equal(["/media/tv/The Continental S01E01.mkv"], _factory.Files.DescribedPaths);
        Assert.Equal(["/media/tv/The Continental S01E01.mkv"], _factory.Files.ReadPaths);
        var files = await FileStatesAsync(cancellationToken);
        Assert.Equal(new FileState(FileInspectionStatus.NoAppPath, NoAppPathError, now, null, null, null, null), files[uncovered.Id]);
        Assert.Equal(
            (FileInspectionStatus.Inspected, ContinentalInode),
            (files[mapped.Id].Status!.Value, files[mapped.Id].Inode!.Value));
    }

    [Fact]
    public async Task Inspect_of_a_file_outside_the_allowed_roots_is_outside_allowed_roots_and_not_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var outside = await PlantFileAsync(OutsidePath);
        // A real file with readable media: only the roots keep the app away from it.
        _factory.Files.AddFile(OutsidePath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Empty(_factory.Files.ReadPaths);
        Assert.Empty(await ReadingsAsync(cancellationToken));
        Assert.Equal(
            new FileState(FileInspectionStatus.OutsideAllowedRoots, OutsideAllowedRootsError, now, null, null, null, null),
            (await FileStatesAsync(cancellationToken))[outside.Id]);
    }

    [Fact]
    public async Task Inspect_of_a_path_where_nothing_is_marks_the_name_not_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        Assert.Equal(
            new FileState(FileInspectionStatus.NotFound, NotFoundError, now, null, null, null, null),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_of_a_file_without_identity_is_identity_unknown_and_reads_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        // A platform without st_dev and st_ino (Windows), which still counts the file's names.
        _factory.Files.Capabilities = new FileInventoryCapabilities(ReportsFileIdentity: false);
        _factory.Files.AddFile(TakenPath, new FileFacts(TakenSizeBytes, TestMedia.ModifiedUtc, null, null, 1), TestMedia.Taken());
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        // An unknown identity beats a guessed one: nothing is read, because nothing could be kept under a key
        // that can't tell two files apart. The link count the look gave is kept.
        Assert.Empty(_factory.Files.ReadPaths);
        Assert.Empty(await ReadingsAsync(cancellationToken));
        Assert.Equal(
            new FileState(FileInspectionStatus.IdentityUnknown, IdentityUnknownError, now, null, null, null, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_without_the_inspector_gives_every_waiting_file_its_sentence_and_still_succeeds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var paths = Enumerable.Range(1, 6).Select(number => $"/media/movies/Film {number}/Film {number}.mkv").ToList();
        var fileIds = new Dictionary<string, Guid>();
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
        {
            fileIds[path] = (await PlantFileAsync(path)).Id;
            _factory.Files.AddFile(
                path,
                TestMedia.File(TakenInode + index),
                read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.InspectorUnavailable, null, Message.Legacy(InspectorUnavailableError)));
        }

        // The first two reads both under way before either answers, so the two the bound allows are known to
        // have started; the four files still waiting then never start the inspector.
        _factory.Files.DuringRead = HoldUntilUnderWay(FileInspectionRunner.MaximumConcurrentReads);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A fact about the app's host, not about any file: the run succeeds and reads nothing.
        Assert.Equal(
            (FileInspectionRunStatus.Succeeded, 6, 6, 0, (string?)null),
            (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed, run.Error));
        Assert.Equal(FileInspectionRunner.MaximumConcurrentReads, _factory.Files.ReadPaths.Count);
        // Never cached: once the inspector is installed, the next run reads every one of them.
        Assert.Empty(await ReadingsAsync(cancellationToken));
        var files = await FileStatesAsync(cancellationToken);
        Assert.All(paths.Select((path, index) => (path, index)), file => Assert.Equal(
            new FileState(FileInspectionStatus.InspectorUnavailable, InspectorUnavailableError, now, null, TestMedia.DeviceId, TakenInode + file.index, 1),
            files[fileIds[file.path]]));
    }

    [Fact]
    public async Task Inspect_of_a_file_that_changed_while_it_was_read_is_media_unreadable_and_caches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(
            TakenPath,
            TestMedia.File(TakenInode, TakenSizeBytes),
            read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.ChangedWhileReading, null, Message.Legacy(ChangedWhileReadingError)));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        // What was read belongs to neither version, so nothing is kept and the page never shows facts of
        // bytes that are gone; the identity is what the look found.
        Assert.Empty(await ReadingsAsync(cancellationToken));
        Assert.Equal(
            new FileState(FileInspectionStatus.MediaUnreadable, ChangedWhileReadingError, now, null, TestMedia.DeviceId, TakenInode, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Theory]
    [InlineData(FileInventoryStatus.MediaUnreadable, FileInspectionStatus.MediaUnreadable, "The media inspector couldn't read the file.")]
    [InlineData(FileInventoryStatus.InspectorUnavailable, FileInspectionStatus.InspectorUnavailable, "The media inspector couldn't be started.")]
    [InlineData(FileInventoryStatus.ChangedWhileReading, FileInspectionStatus.MediaUnreadable, "The file changed while it was being read; the next inspection reads it again.")]
    public async Task Inspect_of_a_read_the_inventory_gives_no_reason_for_writes_the_apps_own(
        FileInventoryStatus answer, FileInspectionStatus status, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(
            TakenPath,
            TestMedia.File(TakenInode, TakenSizeBytes),
            read: new FileInventoryResult<MediaInspection>(answer, null, null));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The file still gets a reason the owner can read, never an empty one.
        Assert.Equal(FileInspectionRunStatus.Succeeded, run.Status);
        var state = (await FileStatesAsync(cancellationToken))[taken.Id];
        Assert.Equal((status, reason), (state.Status, state.Error));
    }

    [Fact]
    public async Task Inspect_reads_exactly_two_files_at_a_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var paths = Enumerable.Range(1, 6).Select(number => $"/media/movies/Film {number}/Film {number}.mkv").ToList();
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
        {
            await PlantFileAsync(path);
            _factory.Files.AddFile(path, TestMedia.File(TakenInode + index), index % 2 == 0 ? TestMedia.Taken() : TestMedia.Continental());
        }

        // No read answers until two are under way, so two at once is reached for certain (a bound of one
        // would fail the run on the gate's timeout instead of passing).
        _factory.Files.DuringRead = HoldUntilUnderWay(FileInspectionRunner.MaximumConcurrentReads);

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 6, 6, 6), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        // Two overlapped, and no third ever joined them: a spinning disk only seeks more with more readers.
        Assert.Equal(2, FileInspectionRunner.MaximumConcurrentReads);
        Assert.Equal(FileInspectionRunner.MaximumConcurrentReads, _factory.Files.MostReadsAtOnce);
        Assert.Equal(paths, _factory.Files.ReadPaths.Order(StringComparer.Ordinal));
        Assert.Equal(6, (await ReadingsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Inspect_reports_its_progress_while_it_reads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Nothing at the first name: looked at, and written, before any read starts.
        await PlantFileAsync(GonePath);
        await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        // What the Library page reads while the inspector works: a fresh context, as another request has.
        var seen = new List<FileInspectionRunEntity>();
        _factory.Files.DuringRead = (_, token) => _factory.WithDbAsync(async db =>
            seen.Add(await db.FileInspectionRuns.AsNoTracking().SingleAsync(token)));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        var during = Assert.Single(seen);
        Assert.Equal(run.Id, during.Id);
        // "1 of 2 checked" while the second is read; nothing read yet.
        Assert.Equal(
            (FileInspectionRunStatus.Running, (int?)2, 1, 0),
            (during.Status, during.FilesTotal, during.FilesChecked, during.FilesProbed));
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
    }

    [Fact]
    public async Task Inspect_after_every_media_folder_was_removed_fails_with_the_sentence_and_touches_no_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        var queuedUtc = Now();
        var start = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        // The owner empties the list while the run waits for the worker.
        var removal = await _factory.PutHttpRequest("/api/settings/allowed-roots", _owner.AccessToken, new UpdateAllowedRootsRequest([]));
        _factory.Time.Advance(TimeSpan.FromSeconds(10));
        var ranUtc = Now();

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var response = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        // The roots are read when the run starts, not when it was queued: none then means nothing is read.
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Failed, "owner", queuedUtc, ranUtc, ranUtc, null, 0, 0, NotHashed, NoAllowedRootsError),
            run);
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        Assert.Equal(new FileState(null, null, null, null, null, null, null), (await FileStatesAsync(cancellationToken))[taken.Id]);
        var failed = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionRunner).FullName);
        Assert.Equal(
            (LogLevel.Warning, $"File inspection {run.Id} failed: no media folder is allowed any more."),
            (failed.Level, failed.Message));
    }

    [Fact]
    public async Task Inspect_that_throws_out_of_the_runner_fails_with_the_apps_sentence_and_keeps_what_it_wrote()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Nothing at the first name: written before the read that breaks.
        var gone = await PlantFileAsync(GonePath);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        // A bug, not an answer: the inventory promises results, so anything thrown is the app's fault.
        _factory.Files.DuringRead = (_, _) => throw new InvalidOperationException("A bug while reading.");
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Failed with the app's sentence, never the exception's text; the progress written so far stays.
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Failed, "owner", now, now, now, 2, 1, 0, NotHashed, AppErrorMessage),
            run);
        var files = await FileStatesAsync(cancellationToken);
        Assert.Equal(new FileState(FileInspectionStatus.NotFound, GoneNotFoundError, now, null, null, null, null), files[gone.Id]);
        Assert.Equal(new FileState(null, null, null, null, null, null, null), files[taken.Id]);
        Assert.Empty(await ReadingsAsync(cancellationToken));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionWorker).FullName);
        Assert.Equal((LogLevel.Error, $"File inspection {run.Id} threw out of the runner."), (error.Level, error.Message));
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task Inspect_of_a_file_gone_between_the_look_and_the_read_is_not_found_with_the_reads_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        // There when looked at, deleted before the inspector opened it.
        _factory.Files.AddFile(
            TakenPath,
            TestMedia.File(TakenInode, TakenSizeBytes),
            read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.NotFound, null, Message.Legacy(NotFoundError)));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        // What the read found is what counts: nothing there, so no identity either.
        Assert.Empty(await ReadingsAsync(cancellationToken));
        Assert.Equal(
            new FileState(FileInspectionStatus.NotFound, NotFoundError, now, null, null, null, null),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Theory]
    [InlineData(FileInspectionRunStatus.Running)]
    [InlineData(FileInspectionRunStatus.Succeeded)]
    [InlineData(FileInspectionRunStatus.Failed)]
    public async Task RunQueued_of_a_run_no_longer_queued_leaves_it_and_every_file_alone(FileInspectionRunStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        var planted = await new FileInspectionRunSeeder(_factory.Scope).Create(
            status,
            requestedBy: _owner.User,
            createdUtc: Now().AddMinutes(-5),
            error: status == FileInspectionRunStatus.Failed ? Message.Of("files.inspection.no-allowed-roots-any-more") : null,
            filesTotal: 1,
            filesChecked: 1,
            filesProbed: 1);
        // Its id reaches the worker again (queued twice, or by the reconciler) after it left the queued status.
        _factory.Resolve<FileInspectionQueue>().TryEnqueue(planted.Id);
        var before = await ReadRunAsync(await _factory.GetHttpRequest($"{BaseUrl}/{planted.Id}", _owner.AccessToken), cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{planted.Id}", _owner.AccessToken);

        //Assert
        // Only the claim of a queued run turns it running; a run not claimed is not touched at all.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, before.Status);
        Assert.Equal(before, await ReadRunAsync(response, cancellationToken));
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        Assert.Equal(new FileState(null, null, null, null, null, null, null), (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_cuts_a_sentence_longer_than_its_column_with_an_ellipsis()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        // An inspector that says far more than a column holds (1,000 characters).
        var sentence = $"ffprobe couldn't read “{TakenPath}” as media:"
            + string.Concat(Enumerable.Repeat(" Invalid data found when processing input.", 40));
        _factory.Files.AddFile(
            TakenPath,
            TestMedia.File(TakenInode, TakenSizeBytes),
            read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.MediaUnreadable, null, Message.Legacy(sentence)));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(FileInspectionRunStatus.Succeeded, run.Status);
        Assert.True(sentence.Length > MediaInspectionEntity.ErrorMaximumLength);
        // MAM-47 (ADR 0042) — stored as key and values: the column's converter fits the message into its 1,000
        // characters by cutting its longest value, which ends with an ellipsis, so the reader sees it was cut. Both
        // on the reading and on the name.
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        var shown = TestTexts.En(reading.Error)!;
        Assert.EndsWith("…", shown, StringComparison.Ordinal);
        Assert.StartsWith(shown[..^1], sentence, StringComparison.Ordinal);
        Assert.True(shown.Length < sentence.Length);
        Assert.Equal(
            new FileState(FileInspectionStatus.MediaUnreadable, shown, Now(), reading.Id, TestMedia.DeviceId, TakenInode, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
        await _factory.WithDbAsync(async db =>
        {
            var storedReading = await db.Database.SqlQueryRaw<string>("SELECT \"Error\" AS \"Value\" FROM \"MediaInspections\"").SingleAsync(cancellationToken);
            var storedName = await db.Database.SqlQueryRaw<string>("SELECT \"InspectionError\" AS \"Value\" FROM \"PhysicalFiles\"").SingleAsync(cancellationToken);
            Assert.True(storedReading.Length <= MediaInspectionEntity.ErrorMaximumLength, $"{storedReading.Length} characters stored");
            Assert.Equal(storedReading, storedName);
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    // A movie with one version naming servicePath, reported by source when given — what a sync stores.
    private async Task<PhysicalFileEntity> PlantFileAsync(string servicePath, IntegrationConnectionEntity? source = null)
    {
        var work = await _works.Create();
        var version = await _versions.Create(work.Id, $"{Guid.NewGuid():N}", sourceConnectionId: source?.Id, sourceName: source?.Name);
        return await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: source?.Id, sourceName: source?.Name);
    }

    // Holds every read until `count` are under way at once, then lets them all go; later reads pass. The
    // timeout only turns a broken bound into a failed run instead of a hung test.
    private static Func<string, CancellationToken, Task> HoldUntilUnderWay(int count)
    {
        var arrived = 0;
        var underWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref arrived) == count)
                underWay.TrySetResult();
            await underWay.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        };
    }

    private async Task<Dictionary<Guid, FileState>> FileStatesAsync(CancellationToken cancellationToken)
    {
        var states = new Dictionary<Guid, FileState>();
        await _factory.WithDbAsync(async db =>
            states = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.Id, FileState.Of, cancellationToken));
        return states;
    }

    private async Task<List<MediaInspectionEntity>> ReadingsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<MediaInspectionEntity>();
        await _factory.WithDbAsync(async db => readings = await db.MediaInspections.AsNoTracking().ToListAsync(cancellationToken));
        return readings;
    }

    private async Task<List<MediaInspectionTrackEntity>> TracksAsync(CancellationToken cancellationToken)
    {
        var tracks = new List<MediaInspectionTrackEntity>();
        await _factory.WithDbAsync(async db => tracks = await db.MediaInspectionTracks.AsNoTracking().ToListAsync(cancellationToken));
        return tracks;
    }

    // A reading's tracks in position order, as the inspector's facts, so they compare field by field.
    private static IEnumerable<MediaTrackFacts> TracksOf(IEnumerable<MediaInspectionTrackEntity> tracks, Guid readingId) =>
        tracks
            .Where(track => track.MediaInspectionId == readingId)
            .OrderBy(track => track.Position)
            .Select(track => new MediaTrackFacts(
                track.Kind,
                track.Codec,
                track.Profile,
                track.Width,
                track.Height,
                track.DynamicRange,
                track.Channels,
                track.ChannelLayout,
                track.Language,
                track.IsDefault,
                track.IsForced,
                track.IsHearingImpaired));

    private static async Task<FileInspectionRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>What a run writes on a file name.</summary>
    private sealed record FileState(
        FileInspectionStatus? Status, string? Error, DateTime? InspectedUtc, Guid? MediaInspectionId, long? DeviceId, long? Inode, int? LinkCount)
    {
        public static FileState Of(PhysicalFileEntity file) =>
            new(file.InspectionStatus, TestTexts.En(file.InspectionError), file.InspectedUtc, file.MediaInspectionId, file.DeviceId, file.Inode, file.LinkCount);
    }

    /// <summary>A reading's cache key, the modification time in ticks so a lost tick shows.</summary>
    private sealed record ReadingKey(long DeviceId, long Inode, long SizeBytes, long ModifiedTicks)
    {
        public static ReadingKey Of(MediaInspectionEntity reading) =>
            new(reading.DeviceId, reading.Inode, reading.SizeBytes, reading.ModifiedUtc.Ticks);

        public static ReadingKey Of(FileFacts facts) =>
            new(facts.DeviceId!.Value, facts.Inode!.Value, facts.SizeBytes, facts.ModifiedUtc.Ticks);
    }
}
