using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-9 — an inspection follows every library sync that stored a snapshot, end to end over real HTTP: the
// owner's sync of the fake Jellyfin, the sync worker, and the inspection worker reading the files where the
// library says they are (no path mapping, so where the app finds them too). A sync queues the run — or joins
// the waiting one — only when a media folder is allowed and only when it succeeded. A sync never touches
// what an inspection wrote on a file name it keeps, and removing a library forgets its names, after which
// the next inspection forgets the readings no name points at any more.
public sealed class FileInspectionTriggerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";
    private const string MediaRoot = "/media";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private const string MovedContinentalPath = "/media/movies/The Continental (2023)/The Continental (2023) - S01E01.mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IAllowedRootSeeder _roots = null!;
    private IFileInspectionRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public FileInspectionTriggerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _roots = new AllowedRootSeeder(_factory.Scope);
        _runs = new FileInspectionRunSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_with_a_root_allowed_queues_a_library_synced_inspection_and_looks_at_no_file_yet()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await CreateJellyfinAsync();
        PutTheSamplesOnDisk();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var sync = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, sync.Status);
        // Queued for the inspection worker, which the sync doesn't wait for.
        var runId = Assert.Single(DrainQueue());
        Assert.Empty(_factory.Files.DescribedPaths);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            // The sync's, not the owner's who started the sync: nobody asked for this one.
            Assert.Equal(
                (runId, FileInspectionTrigger.LibrarySynced, FileInspectionRunStatus.Queued, null, null, now),
                (run.Id, run.Trigger, run.Status, run.RequestedByUserId, run.RequestedByUserName, run.CreatedUtc));
        });
    }

    [Fact]
    public async Task RunQueued_after_a_sync_inspects_every_synced_file_at_the_path_the_library_reported()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await CreateJellyfinAsync();
        PutTheSamplesOnDisk();
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        string[] samplePaths = [AliensTheatricalPath, AliensSpecialEditionPath, District13Path, ContinentalPath];
        // No mapping: the app looks where Jellyfin says, inside the allowed folder only.
        Assert.Equal(samplePaths.Order(StringComparer.Ordinal), _factory.Files.DescribedPaths.Order(StringComparer.Ordinal));
        Assert.Equal(samplePaths.Order(StringComparer.Ordinal), _factory.Files.ReadPaths.Order(StringComparer.Ordinal));
        Assert.All(_factory.Files.RootsGiven, roots => Assert.Equal([MediaRoot], roots));
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (FileInspectionTrigger.LibrarySynced, FileInspectionRunStatus.Succeeded, 4, 4, 4),
                (run.Trigger, run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
            var files = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.ServicePath, cancellationToken);
            Assert.Equal(samplePaths.Order(StringComparer.Ordinal), files.Keys.Order(StringComparer.Ordinal));
            Assert.All(files.Values, file =>
            {
                Assert.Equal((FileInspectionStatus.Inspected, null, TestMedia.DeviceId, 1), (file.InspectionStatus, file.InspectionError, file.DeviceId, file.LinkCount));
                Assert.NotNull(file.MediaInspectionId);
            });
            Assert.Equal(101L, files[ContinentalPath].Inode);
            // What the inspector read for the Continental's identity is what its name points at.
            var continental = await db.MediaInspections.AsNoTracking().SingleAsync(reading => reading.Id == files[ContinentalPath].MediaInspectionId, cancellationToken);
            Assert.Equal((101L, 5_179_168L, 6_011_343L), (continental.Inode, continental.DurationMilliseconds, continental.BitRate));
            Assert.Equal(9, await db.MediaInspectionTracks.CountAsync(track => track.MediaInspectionId == continental.Id, cancellationToken));
        });
    }

    [Fact]
    public async Task Sync_without_an_allowed_root_queues_no_inspection()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        PutTheSamplesOnDisk();

        //Act
        var sync = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // No folder allowed, which is the default: the sync succeeds and the app reads nothing, quietly.
        Assert.Equal(LibrarySyncStatus.Succeeded, sync.Status);
        await AssertNoInspectionAsync(cancellationToken);
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(LibrarySyncRunner).FullName && entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Sync_that_fails_queues_no_inspection()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await CreateJellyfinAsync();
        PutTheSamplesOnDisk();
        _factory.JellyfinHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError);

        //Act
        var sync = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Nothing new was stored, so there is nothing new to look at.
        Assert.Equal(LibrarySyncStatus.Failed, sync.Status);
        await AssertNoInspectionAsync(cancellationToken);
    }

    [Fact]
    public async Task Sync_while_an_inspection_is_queued_joins_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await CreateJellyfinAsync();
        var waiting = await _runs.Create(FileInspectionRunStatus.Queued, requestedBy: _owner.User, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        //Act
        var sync = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // The waiting run lists every file name when it starts, the ones this sync stored included.
        Assert.Equal(LibrarySyncStatus.Succeeded, sync.Status);
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Queued), (run.Id, run.Trigger, run.Status));
        });
    }

    [Fact]
    public async Task Sync_while_an_inspection_is_running_queues_one_behind_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await CreateJellyfinAsync();
        var running = await _runs.Create(
            FileInspectionRunStatus.Running, FileInspectionTrigger.AppStarted, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1), filesTotal: 0);

        //Act
        var sync = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // The running one listed its files before this sync stored any: only a run behind it reads them.
        Assert.Equal(LibrarySyncStatus.Succeeded, sync.Status);
        var queuedId = Assert.Single(DrainQueue());
        Assert.NotEqual(running.Id, queuedId);
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.FileInspectionRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            Assert.Equal(2, rows.Count);
            Assert.Equal(FileInspectionRunStatus.Running, rows[running.Id].Status);
            Assert.Equal((FileInspectionTrigger.LibrarySynced, FileInspectionRunStatus.Queued), (rows[queuedId].Trigger, rows[queuedId].Status));
        });
    }

    [Fact]
    public async Task Sync_again_keeps_what_the_last_inspection_wrote_on_a_name_whose_path_did_not_change()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await CreateJellyfinAsync();
        // Every kind of outcome: read, read as unreadable media (a cached failure with an identity), and
        // not found at all (a sentence, no identity).
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        _factory.Files.AddFile(AliensTheatricalPath, TestMedia.File(inode: 103));
        _factory.Files.AddFile(AliensSpecialEditionPath, TestMedia.File(inode: 104, linkCount: 2), TestMedia.Taken());
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var before = await ReadInspectionColumnsAsync(cancellationToken);
        // Jellyfin now names the Continental somewhere else; the other three where they were.
        var continental = _jellyfin.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.ContinentalId);
        continental["Path"] = MovedContinentalPath;
        continental["MediaSources"]![0]!["Path"] = MovedContinentalPath;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var again = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, again.Status);
        var after = await ReadInspectionColumnsAsync(cancellationToken);
        Assert.Equal(
            [AliensTheatricalPath, AliensSpecialEditionPath, District13Path, MovedContinentalPath],
            after.Keys.Order(StringComparer.Ordinal));
        // The sync owns the names, the inspection what is behind them: a kept name keeps its row and every
        // column the last inspection wrote.
        Assert.Equal(FileInspectionStatus.Inspected, before[AliensSpecialEditionPath].Status);
        Assert.Equal(FileInspectionStatus.MediaUnreadable, before[AliensTheatricalPath].Status);
        Assert.Equal(FileInspectionStatus.NotFound, before[District13Path].Status);
        Assert.Equal(before[AliensSpecialEditionPath], after[AliensSpecialEditionPath]);
        Assert.Equal(before[AliensTheatricalPath], after[AliensTheatricalPath]);
        Assert.Equal(before[District13Path], after[District13Path]);
        // A new name starts unlooked-at: nothing of the old name's reading is carried over to it.
        Assert.NotEqual(before[ContinentalPath].Id, after[MovedContinentalPath].Id);
        Assert.Equal(
            (null, null, null, null, null, null, null),
            (after[MovedContinentalPath].Status, after[MovedContinentalPath].Error, after[MovedContinentalPath].InspectedUtc,
                after[MovedContinentalPath].MediaInspectionId, after[MovedContinentalPath].DeviceId, after[MovedContinentalPath].Inode,
                after[MovedContinentalPath].LinkCount));
        // ...and the inspection this sync queued is the one that will look at it.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                FileInspectionTrigger.LibrarySynced,
                (await db.FileInspectionRuns.AsNoTracking().SingleAsync(run => run.Status == FileInspectionRunStatus.Queued, cancellationToken)).Trigger));
    }

    [Fact]
    public async Task Delete_of_a_library_forgets_its_names_and_the_next_inspection_forgets_the_readings_no_name_points_at()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        // Two libraries over the same disk: the bedroom lists only the Continental, at the same path.
        var bedroomServer = new FakeJellyfinServer(SecondJellyfinAddress) { ApiKey = ApiKey };
        bedroomServer.Movies.RemoveAll(movie => (string?)movie["Id"] != JellyfinSamples.ContinentalId);
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, bedroomServer);
        var livingRoom = await CreateJellyfinAsync();
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", SecondJellyfinAddress, apiKey: ApiKey);
        PutTheSamplesOnDisk();
        await _factory.SyncLibraryAsync(livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(bedroom.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Act
        var removal = await _factory.DeleteHttpRequest($"/api/settings/integrations/{livingRoom.Id}", _owner.AccessToken);
        var readingsAfterRemoval = 0;
        await _factory.WithDbAsync(async db => readingsAfterRemoval = await db.MediaInspections.CountAsync(cancellationToken));
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        // Removing a library forgets the names it reported, never what was read from the bytes...
        Assert.Equal(4, readingsAfterRemoval);
        // ...the next inspection does, once no name points at a reading. The bedroom still names the
        // Continental, so its reading stays and answers for it without a read.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 1, 1, 0), (run.Status, run.FilesTotal, run.FilesChecked, run.FilesProbed));
        await _factory.WithDbAsync(async db =>
        {
            var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((bedroom.Id, ContinentalPath, FileInspectionStatus.Inspected), (file.SourceConnectionId, file.ServicePath, file.InspectionStatus));
            var reading = await db.MediaInspections.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((file.MediaInspectionId, 101L), (reading.Id, reading.Inode));
            // A reading's tracks go with it.
            Assert.Equal(9, await db.MediaInspectionTracks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.MediaInspectionTracks.CountAsync(track => track.MediaInspectionId != reading.Id, cancellationToken));
        });
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() =>
        _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

    // The four sample movies on the disk, where Jellyfin says they are, each its own file.
    private void PutTheSamplesOnDisk()
    {
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        _factory.Files.AddFile(District13Path, TestMedia.File(inode: 102), TestMedia.Taken());
        _factory.Files.AddFile(AliensTheatricalPath, TestMedia.File(inode: 103), TestMedia.Taken());
        _factory.Files.AddFile(AliensSpecialEditionPath, TestMedia.File(inode: 104), TestMedia.Taken());
    }

    // What the inspection wrote on each file name, by the name's path.
    private async Task<Dictionary<string, (Guid Id, FileInspectionStatus? Status, string? Error, DateTime? InspectedUtc, Guid? MediaInspectionId, long? DeviceId, long? Inode, int? LinkCount)>> ReadInspectionColumnsAsync(
        CancellationToken cancellationToken)
    {
        List<PhysicalFileEntity> files = [];
        await _factory.WithDbAsync(async db => files = await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken));
        return files.ToDictionary(
            file => file.ServicePath,
            file => (file.Id, file.InspectionStatus, TestTexts.En(file.InspectionError), file.InspectedUtc, file.MediaInspectionId, file.DeviceId, file.Inode, file.LinkCount));
    }

    // Every run id waiting for the worker, in queue order; the queue is empty afterwards.
    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<FileInspectionQueue>();
        var runIds = new List<Guid>();
        while (queue.Reader.TryRead(out var runId))
            runIds.Add(runId);
        return runIds;
    }

    private async Task AssertNoInspectionAsync(CancellationToken cancellationToken)
    {
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
        Assert.Empty(_factory.Files.DescribedPaths);
    }
}
