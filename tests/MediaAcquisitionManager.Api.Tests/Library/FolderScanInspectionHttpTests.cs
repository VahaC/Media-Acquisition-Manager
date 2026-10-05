using MediaAcquisitionManager.Api.Services.Files;
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
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-18 — what follows a scan: the usual file inspection (MAM-9), queued with the FolderScanned trigger once
// the scan stored what it found, reads the new names with the media inspector through MAM-9's cache — as it
// reads a library's — and records on each name the file's identity and what the disk said of its size and
// last write. An unchanged file is never read twice; a changed one is. A library's names get the size and
// time the disk said too, beside the size the library reported, and lose them when the file is gone. The scan
// joins an inspection already waiting, gets one behind a running one, and stays succeeded when none can be
// queued. Each scan is the owner's Scan folders over HTTP; each inspection the worker's pass over its queue.
public sealed class FolderScanInspectionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string Root = "/media";
    private const string InterstellarPath = "/media/downloads/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv";
    private const string BirthdayPath = "/media/home/Birthday 2019.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const long InterstellarInode = 2_097_153;
    private const long BirthdayInode = 2_097_154;
    private const long InterstellarSizeBytes = 30_870_249_472;
    private const string NotFoundError = "Nothing is at “/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv”.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private AuthorizedUser _owner = null!;

    public FolderScanInspectionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunQueued_after_a_scan_reads_the_new_names_and_records_their_identity_size_and_modification_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(InterstellarInode, InterstellarSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(BirthdayInode, linkCount: 2), TestMedia.Continental());
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var inspectedUtc = Now();

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 2), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        Assert.Equal([InterstellarPath, BirthdayPath], _factory.Files.ReadPaths.Order(StringComparer.Ordinal));
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (FileInspectionTrigger.FolderScanned, FileInspectionRunStatus.Succeeded, null, 2, 2, 2),
                (run.Trigger, run.Status, run.RequestedByUserName, run.FilesTotal, run.FilesChecked, run.FilesProbed));
            var files = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.ServicePath, cancellationToken);
            var readings = await db.MediaInspections.AsNoTracking().ToDictionaryAsync(reading => reading.Id, cancellationToken);
            var interstellar = files[InterstellarPath];
            var birthday = files[BirthdayPath];
            // The identity, the size and the last write as the disk said them — the time to the tick — on a
            // name no library reported, which has no size a library reported.
            Assert.Equal(
                (FileInspectionStatus.Inspected, inspectedUtc, TestMedia.DeviceId, InterstellarInode, 1),
                (interstellar.InspectionStatus!.Value, interstellar.InspectedUtc!.Value, interstellar.DeviceId!.Value, interstellar.Inode!.Value, interstellar.LinkCount!.Value));
            Assert.Equal(
                (InterstellarSizeBytes, TestMedia.ModifiedUtc.Ticks, null),
                (interstellar.ReadSizeBytes!.Value, interstellar.ReadModifiedUtc!.Value.Ticks, interstellar.SizeBytes));
            Assert.Equal(
                (FileInspectionStatus.Inspected, BirthdayInode, 2, 3_891_719_630L, TestMedia.ModifiedUtc.Ticks),
                (birthday.InspectionStatus!.Value, birthday.Inode!.Value, birthday.LinkCount!.Value, birthday.ReadSizeBytes!.Value, birthday.ReadModifiedUtc!.Value.Ticks));
            // ...and what the media inspector read, the duration included, as for a library's file.
            Assert.Equal(5_604_672L, readings[interstellar.MediaInspectionId!.Value].DurationMilliseconds);
            Assert.Equal(5_179_168L, readings[birthday.MediaInspectionId!.Value].DurationMilliseconds);
            Assert.Equal(20, await db.MediaInspectionTracks.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Scan_and_inspect_again_reads_only_the_file_that_changed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(InterstellarInode, InterstellarSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(BirthdayPath, TestMedia.File(BirthdayInode), TestMedia.Continental());
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var before = await FilesAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        // The birthday video was trimmed: one byte fewer, written a minute later.
        var trimmed = TestMedia.File(BirthdayInode, 3_891_719_629, TestMedia.ModifiedUtc.AddMinutes(1));
        _factory.Files.AddFile(BirthdayPath, trimmed, TestMedia.Taken());

        //Act
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 2, 2), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        // Three reads in all: each file once at first, then only the changed one.
        Assert.Equal(3, _factory.Files.ReadPaths.Count);
        Assert.Equal(BirthdayPath, _factory.Files.ReadPaths[^1]);
        var after = await FilesAsync(cancellationToken);
        // The unchanged file keeps its row and its reading; the changed one keeps its row and gets a new reading
        // and what the disk says of it now.
        Assert.Equal(
            (before[InterstellarPath].Id, before[InterstellarPath].MediaInspectionId, InterstellarSizeBytes),
            (after[InterstellarPath].Id, after[InterstellarPath].MediaInspectionId, after[InterstellarPath].ReadSizeBytes!.Value));
        Assert.Equal(before[BirthdayPath].Id, after[BirthdayPath].Id);
        Assert.NotEqual(before[BirthdayPath].MediaInspectionId, after[BirthdayPath].MediaInspectionId);
        Assert.Equal((3_891_719_629L, trimmed.ModifiedUtc), (after[BirthdayPath].ReadSizeBytes!.Value, after[BirthdayPath].ReadModifiedUtc!.Value));
        await _factory.WithDbAsync(async db =>
        {
            var second = await db.FileInspectionRuns.AsNoTracking().OrderByDescending(run => run.CreatedUtc).FirstAsync(cancellationToken);
            Assert.Equal((FileInspectionTrigger.FolderScanned, 2, 2, 1), (second.Trigger, second.FilesTotal, second.FilesChecked, second.FilesProbed));
            // The old reading of the trimmed file, which no name points at any more, is gone.
            Assert.Equal(2, await db.MediaInspections.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Inspect_writes_the_size_and_time_the_disk_said_on_a_librarys_name_too_and_clears_them_once_the_file_is_gone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var work = await new MediaWorkDataFactory(db, _factory.Time).Create();
        var version = await new MediaVersionDataFactory(db, _factory.Time).Create(work.Id, $"{Guid.NewGuid():N}");
        // The library reported another size than the disk has: its word, kept as it said it.
        var taken = await new PhysicalFileDataFactory(db, _factory.Time).Create(version.Id, TakenPath, sizeBytes: 30_000_000_000);
        _factory.Files.AddFile(TakenPath, TestMedia.File(InterstellarInode, InterstellarSizeBytes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var read = (await FilesAsync(cancellationToken))[TakenPath];
        _factory.Files.Remove(TakenPath);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(
            (30_000_000_000L, InterstellarSizeBytes, TestMedia.ModifiedUtc),
            (read.SizeBytes!.Value, read.ReadSizeBytes!.Value, read.ReadModifiedUtc!.Value));
        // Nothing there any more: the identity, size and time the last look read go with it, as nothing is
        // known of the bytes; the size the library reported is still the library's.
        Assert.Equal(FileInspectionRunStatus.Succeeded, run.Status);
        var gone = (await FilesAsync(cancellationToken))[TakenPath];
        Assert.Equal((taken.Id, FileInspectionStatus.NotFound, NotFoundError), (gone.Id, gone.InspectionStatus!.Value, gone.InspectionError));
        Assert.Equal((null, null, null, null), (gone.DeviceId, gone.Inode, gone.ReadSizeBytes, gone.ReadModifiedUtc));
        Assert.Equal(30_000_000_000L, gone.SizeBytes);
    }

    [Fact]
    public async Task Scan_while_an_inspection_waits_joins_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(InterstellarInode), TestMedia.Taken());
        var waiting = await new FileInspectionRunSeeder(_factory.Scope).Create(
            FileInspectionRunStatus.Queued, requestedBy: _owner.User, createdUtc: Now().AddMinutes(-1));

        //Act
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The waiting run lists every name when it starts, the ones this scan stored included.
        Assert.Equal(FolderScanStatus.Succeeded, scan.Status);
        Assert.Empty(DrainInspectionQueue());
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Queued), (run.Id, run.Trigger, run.Status));
        });
    }

    [Fact]
    public async Task Scan_while_an_inspection_is_running_queues_one_behind_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(InterstellarInode), TestMedia.Taken());
        var running = await new FileInspectionRunSeeder(_factory.Scope).Create(
            FileInspectionRunStatus.Running, FileInspectionTrigger.LibrarySynced, createdUtc: Now().AddMinutes(-1), filesTotal: 0);

        //Act
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The running one listed its names before this scan stored any: only a run behind it reads them.
        Assert.Equal(FolderScanStatus.Succeeded, scan.Status);
        var queuedId = Assert.Single(DrainInspectionQueue());
        Assert.NotEqual(running.Id, queuedId);
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.FileInspectionRuns.AsNoTracking().ToDictionaryAsync(run => run.Id, cancellationToken);
            Assert.Equal(2, rows.Count);
            Assert.Equal(FileInspectionRunStatus.Running, rows[running.Id].Status);
            Assert.Equal((FileInspectionTrigger.FolderScanned, FileInspectionRunStatus.Queued), (rows[queuedId].Trigger, rows[queuedId].Status));
        });
    }

    [Fact]
    public async Task Scan_stays_succeeded_with_what_it_stored_when_no_inspection_can_be_queued_after_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        _factory.Files.AddFile(InterstellarPath, TestMedia.File(InterstellarInode), TestMedia.Taken());
        // A bug, not a race: the inspection's insert fails for a reason no join answers.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<FileInspectionRunEntity>().Any(entry => entry.State == EntityState.Added),
            _ => throw new InvalidOperationException("A bug while queuing the inspection."));

        //Act
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The names are stored and the scan's outcome is written: the next sync, start or Inspect files reads them.
        Assert.Equal((FolderScanStatus.Succeeded, 1, 1, (string?)null), (scan.Status, scan.FilesFound, scan.FilesUnreported, scan.Error));
        Assert.Contains(InterstellarPath, (await FilesAsync(cancellationToken)).Keys);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
        var error = Assert.Single(
            _factory.Logs.Entries, entry => entry.Category == typeof(FolderScanRunner).FullName && entry.Level >= LogLevel.Warning);
        Assert.Equal(
            (LogLevel.Error, $"Folder scan {scan.Id} succeeded, but no file inspection could be queued after it."),
            (error.Level, error.Message));
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    private async Task<Dictionary<string, PhysicalFileEntity>> FilesAsync(CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, PhysicalFileEntity>();
        await _factory.WithDbAsync(async db =>
            files = await db.PhysicalFiles.AsNoTracking().ToDictionaryAsync(file => file.ServicePath, cancellationToken));
        return files;
    }

    // Every inspection run id waiting for its worker; the queue is empty afterwards.
    private List<Guid> DrainInspectionQueue()
    {
        var queue = _factory.Resolve<FileInspectionQueue>();
        var runIds = new List<Guid>();
        while (queue.Reader.TryRead(out var runId))
            runIds.Add(runId);
        return runIds;
    }
}
