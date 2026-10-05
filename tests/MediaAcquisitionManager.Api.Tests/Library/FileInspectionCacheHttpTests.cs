using System.Net;
using System.Net.Http.Json;
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

// MAM-9 — what one inspection keeps for the next (specification §3.3): a reading is kept by the file's
// identity, size and modification time, so an unchanged file is never read twice — a cached failure
// included, and a renamed file finds its reading again — while a change of any of the four reads it again
// and forgets the old reading. Two names of one inode are read once. What wasn't the file's fault (no
// inspector) or belongs to neither version (a file that changed under the read) is never kept, so the next
// run reads it; a read is kept under the key it read, not the one the look saw; and a reading no name
// points at any more — a file gone, or a row left behind — is deleted at the end of the run. Each run is
// the owner's Inspect files over HTTP (POST /api/library/inspections, the worker, GET the run).
public sealed class FileInspectionCacheHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string Root = "/media";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const string TakenLinkPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const long TakenInode = 1_048_577;
    private const long ContinentalInode = 1_048_578;
    private const long TakenSizeBytes = 30_870_249_472;

    private const string MediaUnreadableError =
        "ffprobe couldn't read “/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv” as media: Invalid data found when processing input.";

    private const string NotFoundError = "Nothing is at “/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv”.";

    private const string InspectorUnavailableError = "ffprobe couldn't be started: “/usr/bin/ffprobe” was not found.";

    private const string ChangedWhileReadingError =
        "“/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv” changed while ffprobe was reading it.";

    /// <summary>MAM-19 — a run whose hashing step found no two files that could be copies of each other.</summary>
    private static readonly FileHashingDto NothingToHash = new(0, 0, 0, 0, null);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private IMediaInspectionDataFactory _readings = null!;
    private AuthorizedUser _owner = null!;

    public FileInspectionCacheHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        _readings = new MediaInspectionDataFactory(db, _factory.Time);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Inspect_again_with_nothing_changed_reads_nothing_and_keeps_each_reading()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        var continental = await PlantFileAsync(ContinentalPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode), TestMedia.Continental());
        var readUtc = Now();
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var firstLinks = (await FileStatesAsync(cancellationToken)).ToDictionary(file => file.Key, file => file.Value.MediaInspectionId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var lookedUtc = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{run.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Both names looked at again, neither read: the cache answered for both.
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Succeeded, "owner", lookedUtc, lookedUtc, lookedUtc, 2, 2, 0, NothingToHash, null),
            await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken));
        Assert.Equal([TakenPath, ContinentalPath], _factory.Files.ReadPaths.Order(StringComparer.Ordinal));
        Assert.Equal(4, _factory.Files.DescribedPaths.Count);
        // The same readings, read when they were; the look's time is the new run's.
        var readings = await ReadingsAsync(cancellationToken);
        Assert.Equal(2, readings.Count);
        Assert.All(readings, reading => Assert.Equal(readUtc, reading.CreatedUtc));
        var files = await FileStatesAsync(cancellationToken);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, lookedUtc, firstLinks[taken.Id], TestMedia.DeviceId, TakenInode, 1),
            files[taken.Id]);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, lookedUtc, firstLinks[continental.Id], TestMedia.DeviceId, ContinentalInode, 1),
            files[continental.Id]);
        Assert.Equal(readings.Select(reading => (Guid?)reading.Id).Order(), firstLinks.Values.Order());
    }

    [Theory]
    [InlineData("size")]
    [InlineData("modification time")]
    [InlineData("inode")]
    [InlineData("device")]
    public async Task Inspect_after_a_change_of_one_key_part_reads_the_file_again_and_forgets_the_old_reading(string changedPart)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var oldReadingId = (await FileStatesAsync(cancellationToken))[taken.Id].MediaInspectionId;
        // The same name now leads to other bytes: re-encoded, touched, replaced, or on another disk.
        var changed = ChangedFacts(changedPart);
        _factory.Files.AddFile(TakenPath, changed, TestMedia.Continental());
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath, TakenPath], _factory.Files.ReadPaths);
        // One reading under the new key, with the new bytes' facts; the old one, which no name points at any
        // more, is gone with its tracks.
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.NotEqual(oldReadingId, reading.Id);
        Assert.Equal(ReadingKey.Of(changed), ReadingKey.Of(reading));
        Assert.Equal((5_179_168L, 6_011_343L, now), (reading.DurationMilliseconds!.Value, reading.BitRate!.Value, reading.CreatedUtc));
        var tracks = await TracksAsync(cancellationToken);
        Assert.Equal(9, tracks.Count);
        Assert.All(tracks, track => Assert.Equal(reading.Id, track.MediaInspectionId));
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, changed.DeviceId, changed.Inode, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_reads_two_names_of_one_file_once_and_links_both_to_one_reading()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var library = await PlantFileAsync(TakenPath);
        var download = await PlantFileAsync(TakenLinkPath);
        // Hardlinks: one inode with two names, the way a finished download is linked into the library.
        var facts = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Files.AddFile(TakenLinkPath, facts, TestMedia.Taken());
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Two names looked at, the bytes read once.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal(2, _factory.Files.DescribedPaths.Count);
        Assert.Contains(Assert.Single(_factory.Files.ReadPaths), new[] { TakenPath, TakenLinkPath });
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal(11, (await TracksAsync(cancellationToken)).Count);
        // Both name the same identity and count its two names, for Phase B to count the bytes once.
        var files = await FileStatesAsync(cancellationToken);
        var expected = new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode, 2);
        Assert.Equal(expected, files[library.Id]);
        Assert.Equal(expected, files[download.Id]);
    }

    [Fact]
    public async Task Inspect_again_does_not_read_a_file_the_inspector_could_not_read_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes));
        var readUtc = Now();
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var lookedUtc = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // A damaged file costs one read, not one per inspection, until it changes.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal((MediaUnreadableError, readUtc), (reading.Error, reading.CreatedUtc));
        // The cached sentence is the file's again.
        Assert.Equal(
            new FileState(FileInspectionStatus.MediaUnreadable, MediaUnreadableError, lookedUtc, reading.Id, TestMedia.DeviceId, TakenInode, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_of_a_file_gone_since_the_last_run_clears_its_identity_and_keeps_its_reading()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var before = (await FileStatesAsync(cancellationToken))[taken.Id];
        _factory.Files.Remove(TakenPath);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal(FileInspectionStatus.Inspected, before.Status);
        // Nothing is there now: no identity — a stale one would join unrelated names in Phase B. The
        // name still points at its last reading, which the status says doesn't apply now: a mount that
        // was away for one run must not cost the whole cache (FileInspectionRunner.WriteAsync).
        Assert.Equal(
            new FileState(FileInspectionStatus.NotFound, NotFoundError, now, before.MediaInspectionId, null, null, null),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
        Assert.Equal(before.MediaInspectionId, Assert.Single(await ReadingsAsync(cancellationToken)).Id);
        Assert.Equal(11, (await TracksAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Inspect_of_a_file_back_unchanged_after_a_run_without_it_reads_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        var facts = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var readingId = (await FileStatesAsync(cancellationToken))[taken.Id].MediaInspectionId;
        // The mount was away for one run...
        _factory.Files.Remove(TakenPath);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // ...and is back with the same bytes.
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Answered from the cache: ffprobe read the file once in all three runs.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, now, readingId, facts.DeviceId, facts.Inode, facts.LinkCount),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
        Assert.Equal(readingId, Assert.Single(await ReadingsAsync(cancellationToken)).Id);
    }

    [Fact]
    public async Task Inspect_of_a_renamed_file_finds_its_reading_without_reading_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var original = await PlantFileAsync(TakenPath);
        var facts = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var readingId = (await FileStatesAsync(cancellationToken))[original.Id].MediaInspectionId;
        // Renamed on disk, and a sync replaced the old name with the new one: the same bytes, a new row.
        await _factory.WithDbAsync(db => db.PhysicalFiles.Where(file => file.Id == original.Id).ExecuteDeleteAsync(cancellationToken));
        var renamed = await PlantFileAsync(TakenLinkPath);
        _factory.Files.Remove(TakenPath);
        _factory.Files.AddFile(TakenLinkPath, facts, TestMedia.Taken());
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Kept by identity, not by name: the new name is answered from the cache, and the reading it now
        // points at survives the end-of-run cleanup.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath], _factory.Files.ReadPaths);
        Assert.Equal(readingId, Assert.Single(await ReadingsAsync(cancellationToken)).Id);
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, now, readingId, TestMedia.DeviceId, TakenInode, 1),
            (await FileStatesAsync(cancellationToken))[renamed.Id]);
    }

    [Fact]
    public async Task Inspect_with_the_inspector_back_reads_the_files_it_could_not_read_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var paths = Enumerable.Range(1, 3).Select(number => $"/media/movies/Film {number}/Film {number}.mkv").ToList();
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
        {
            await PlantFileAsync(path);
            _factory.Files.AddFile(
                path,
                TestMedia.File(TakenInode + index),
                read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.InspectorUnavailable, null, InspectorUnavailableError));
        }

        var unavailable = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // ffprobe installed: the same files, now readable.
        foreach (var (path, index) in paths.Select((path, index) => (path, index)))
            _factory.Files.AddFile(path, TestMedia.File(TakenInode + index), TestMedia.Taken());
        var readBefore = _factory.Files.ReadPaths.Count;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 0), (unavailable.Status, unavailable.FilesProbed));
        // Nothing was kept from the run without an inspector, so every file is read now.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 3, 3, 3), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal(paths, _factory.Files.ReadPaths.Skip(readBefore).Order(StringComparer.Ordinal));
        Assert.Equal(3, (await ReadingsAsync(cancellationToken)).Count);
        Assert.All((await FileStatesAsync(cancellationToken)).Values, file =>
            Assert.Equal((FileInspectionStatus.Inspected, (string?)null, now), (file.Status!.Value, file.Error, file.InspectedUtc!.Value)));
    }

    [Fact]
    public async Task Inspect_again_reads_a_file_that_changed_while_it_was_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        var facts = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(
            TakenPath,
            facts,
            read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.ChangedWhileReading, null, ChangedWhileReadingError));
        var changing = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var changingState = (await FileStatesAsync(cancellationToken))[taken.Id];
        // Written, and still since.
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 0), (changing.Status, changing.FilesProbed));
        Assert.Equal((FileInspectionStatus.MediaUnreadable, (Guid?)null), (changingState.Status!.Value, changingState.MediaInspectionId));
        // Nothing was kept from the read that raced a write, so the file is read again, and its facts kept.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenPath, TakenPath], _factory.Files.ReadPaths);
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal(ReadingKey.Of(facts), ReadingKey.Of(reading));
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_of_a_file_replaced_between_the_look_and_the_read_keeps_the_reading_under_the_reads_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        var looked = TestMedia.File(TakenInode, TakenSizeBytes);
        // Replaced after the look and still during the read: the inventory's facts agree before and after the
        // read, and they are the new file's.
        var read = TestMedia.File(TakenInode + 7, TakenSizeBytes + 4_096, TestMedia.ModifiedUtc.AddSeconds(5));
        _factory.Files.AddFile(
            TakenPath,
            looked,
            read: new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(read, TestMedia.Taken()), null));
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        // Kept under the bytes it was read from, never under the key of the file the look saw.
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal(ReadingKey.Of(read), ReadingKey.Of(reading));
        Assert.Equal(
            new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode + 7, 1),
            (await FileStatesAsync(cancellationToken))[taken.Id]);
    }

    [Fact]
    public async Task Inspect_of_two_names_that_became_the_same_bytes_while_read_keeps_one_reading_for_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var library = await PlantFileAsync(TakenPath);
        var download = await PlantFileAsync(TakenLinkPath);
        // Two files when looked at (two keys, so two reads), linked into one before either was read: both
        // reads come back with the one file's facts.
        var linked = TestMedia.File(TakenInode + 9, TakenSizeBytes, linkCount: 2);
        var read = new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(linked, TestMedia.Taken()), null);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), read: read);
        _factory.Files.AddFile(TakenLinkPath, TestMedia.File(ContinentalInode, TakenSizeBytes), read: read);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 2), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenLinkPath, TakenPath], _factory.Files.ReadPaths.Order(StringComparer.Ordinal));
        // The unique key settles it: the first reading stands, and the second read joins it.
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal(ReadingKey.Of(linked), ReadingKey.Of(reading));
        Assert.Equal(11, (await TracksAsync(cancellationToken)).Count);
        var files = await FileStatesAsync(cancellationToken);
        var expected = new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode + 9, 2);
        Assert.Equal(expected, files[library.Id]);
        Assert.Equal(expected, files[download.Id]);
    }

    [Fact]
    public async Task Inspect_deletes_a_reading_no_name_points_at()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var taken = await PlantFileAsync(TakenPath);
        _factory.Files.AddFile(TakenPath, TestMedia.File(TakenInode, TakenSizeBytes), TestMedia.Taken());
        // A reading left behind: its file's name was dropped by a sync since.
        var orphanId = (await _readings.Create(
            TestMedia.DeviceId,
            ContinentalInode,
            3_891_719_630,
            TestMedia.ModifiedUtc,
            [new MediaInspectionTrackEntity { Kind = MediaTrackKind.Video, Codec = "hevc" }],
            durationMilliseconds: 5_179_168)).Id;

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(FileInspectionRunStatus.Succeeded, run.Status);
        // Only the reading a name points at is left, with its own tracks; the other went with its track.
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.NotEqual(orphanId, reading.Id);
        Assert.Equal(reading.Id, (await FileStatesAsync(cancellationToken))[taken.Id].MediaInspectionId);
        var tracks = await TracksAsync(cancellationToken);
        Assert.Equal(11, tracks.Count);
        Assert.DoesNotContain(tracks, track => track.MediaInspectionId == orphanId);
    }

    [Fact]
    public async Task Inspect_while_a_sync_replaces_a_name_keeps_its_reading_for_the_next_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var original = await PlantFileAsync(TakenPath);
        var facts = TestMedia.File(TakenInode, TakenSizeBytes);
        _factory.Files.AddFile(TakenPath, facts, TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var readingId = (await FileStatesAsync(cancellationToken))[original.Id].MediaInspectionId!.Value;
        // A new file keeps the next run reading while a sync lands: the file was renamed on disk, and the
        // sync replaces its name with a new row no run has looked at yet.
        await PlantFileAsync(ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode), TestMedia.Continental());
        PhysicalFileEntity? renamed = null;
        _factory.Files.DuringRead = async (_, token) =>
        {
            await _factory.WithDbAsync(db => db.PhysicalFiles.Where(file => file.Id == original.Id).ExecuteDeleteAsync(token));
            renamed = await PlantFileAsync(TakenLinkPath);
            _factory.Files.Remove(TakenPath);
            _factory.Files.AddFile(TakenLinkPath, facts, TestMedia.Taken());
        };

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The reading lost its only name to the sync, not to this run: it waits for the run the sync
        // queued, which finds the renamed file's bytes in it instead of reading them again.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.NotNull(renamed);
        Assert.Contains(await ReadingsAsync(cancellationToken), reading => reading.Id == readingId);
        var files = await FileStatesAsync(cancellationToken);
        Assert.DoesNotContain(original.Id, files.Keys);
        Assert.Null(files[renamed.Id].MediaInspectionId);
        Assert.DoesNotContain(files.Values, file => file.MediaInspectionId == readingId);
    }

    [Fact]
    public async Task Inspect_of_two_hardlinked_names_whose_bytes_were_replaced_before_the_read_links_only_the_name_it_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var library = await PlantFileAsync(TakenPath);
        var download = await PlantFileAsync(TakenLinkPath);
        var looked = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        // The download client replaced its copy with a re-encode after the look: the read, through the first
        // name in path order, finds new bytes, while the library's name still holds the old ones.
        var replaced = TestMedia.File(TakenInode + 7, TakenSizeBytes + 4_096, TestMedia.ModifiedUtc.AddSeconds(5));
        var read = new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(replaced, TestMedia.Taken()), null);
        _factory.Files.AddFile(TakenPath, looked, read: read);
        _factory.Files.AddFile(TakenLinkPath, looked, read: read);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Only the name read is known to lead to the new bytes; the other keeps what it showed, never the
        // re-encode's facts and identity, and is still counted, so the run's progress completes.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 1), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenLinkPath], _factory.Files.ReadPaths);
        var reading = Assert.Single(await ReadingsAsync(cancellationToken));
        Assert.Equal(ReadingKey.Of(replaced), ReadingKey.Of(reading));
        var files = await FileStatesAsync(cancellationToken);
        Assert.Equal(new FileState(FileInspectionStatus.Inspected, null, now, reading.Id, TestMedia.DeviceId, TakenInode + 7, 1), files[download.Id]);
        Assert.Equal(new FileState(null, null, null, null, null, null, null), files[library.Id]);
    }

    [Theory]
    [InlineData(FileInventoryStatus.ChangedWhileReading)]
    [InlineData(FileInventoryStatus.NotFound)]
    public async Task Inspect_of_two_hardlinked_names_whose_read_fails_writes_on_the_name_read_only_and_counts_both(FileInventoryStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var library = await PlantFileAsync(TakenPath);
        var download = await PlantFileAsync(TakenLinkPath);
        var looked = TestMedia.File(TakenInode, TakenSizeBytes, linkCount: 2);
        var error = status == FileInventoryStatus.ChangedWhileReading ? ChangedWhileReadingError : NotFoundError;
        var read = new FileInventoryResult<MediaInspection>(status, null, error);
        _factory.Files.AddFile(TakenPath, looked, read: read);
        _factory.Files.AddFile(TakenLinkPath, looked, read: read);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // What the read through one name found says nothing sure about the other: the bytes it leads to
        // may be the old ones still. It keeps what it showed until the next run looks again.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 2, 2, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        Assert.Equal([TakenLinkPath], _factory.Files.ReadPaths);
        Assert.Empty(await ReadingsAsync(cancellationToken));
        var files = await FileStatesAsync(cancellationToken);
        Assert.Equal(
            status == FileInventoryStatus.ChangedWhileReading
                ? new FileState(FileInspectionStatus.MediaUnreadable, error, now, null, TestMedia.DeviceId, TakenInode, 2)
                : new FileState(FileInspectionStatus.NotFound, error, now, null, null, null, null),
            files[download.Id]);
        Assert.Equal(new FileState(null, null, null, null, null, null, null), files[library.Id]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    // The Taken file's facts with one part of the cache key changed. The modification time by a single
    // tick: the key keeps the file system's time exactly, so the smallest change a write leaves shows.
    private static FileFacts ChangedFacts(string part) => part switch
    {
        "size" => TestMedia.File(TakenInode, TakenSizeBytes + 1),
        "modification time" => TestMedia.File(TakenInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddTicks(1)),
        "inode" => TestMedia.File(TakenInode + 1, TakenSizeBytes),
        "device" => TestMedia.File(TakenInode, TakenSizeBytes, deviceId: 2_051),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Not a part of the cache key."),
    };

    // A movie with one version naming servicePath — what a sync stores for a file it reported.
    private async Task<PhysicalFileEntity> PlantFileAsync(string servicePath)
    {
        var work = await _works.Create();
        var version = await _versions.Create(work.Id, $"{Guid.NewGuid():N}");
        return await _physicalFiles.Create(version.Id, servicePath);
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

    /// <summary>What a run writes on a file name.</summary>
    private sealed record FileState(
        FileInspectionStatus? Status, string? Error, DateTime? InspectedUtc, Guid? MediaInspectionId, long? DeviceId, long? Inode, int? LinkCount)
    {
        public static FileState Of(PhysicalFileEntity file) =>
            new(file.InspectionStatus, file.InspectionError, file.InspectedUtc, file.MediaInspectionId, file.DeviceId, file.Inode, file.LinkCount);
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
