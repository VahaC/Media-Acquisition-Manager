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

// MAM-19 — what one inspection's hashing keeps for the next (ADR 0022, specification §3.3): a hash is kept by
// the file's identity, size and modification time, like MAM-9's readings, so an unchanged file is read whole
// once — a renamed one finds its hash again, and so does one whose mount was away for any number of runs —
// while a change of any of the four reads it again under the new key. A hash goes once neither a name carries
// its key nor a reading of its bytes is kept: a changed file's old hash with its old reading, in the run that
// found the change, and a row left behind by the next run; a hash a sync orphaned while a run went stays with
// the reading ADR 0009 keeps for the next run, which finds it again. A run that fails — no media folder allowed
// any more, or a bug before its last step — hashes nothing and forgets nothing. Each run is the owner's Inspect files over HTTP. What one run does is
// FileInspectionHashHttpTests'; a run stopped or broken while hashing FileHashWorkerHttpTests'.
public sealed class FileHashCacheHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string Root = "/media";

    // In path order: the backup first, two films of another size, the library's film, a new film last.
    private const string CopyPath = "/media/backup/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string LinkPath = "/media/downloads/Taken (2008) BDRemux 1080p.mkv";
    private const string FirstFilmPath = "/media/movies/Film 1/Film 1.mkv";
    private const string SecondFilmPath = "/media/movies/Film 2/Film 2.mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008) BDRemux 1080p.mkv";
    private const string ContinentalPath = "/media/tv/The Continental (2023)/Season 1/The Continental S01E01.mkv";
    private const long TakenInode = 1_048_577;
    private const long ContinentalInode = 1_048_578;
    private const long CopyInode = 1_048_587;
    private const long FirstFilmInode = 1_048_597;
    private const long SecondFilmInode = 1_048_607;

    /// <summary>The inode of a file whose name a sync dropped: its hash is left behind.</summary>
    private const long LeftBehindInode = 1_048_617;
    private const long TakenSizeBytes = 30_870_249_472;

    private const string NoAllowedRootsError =
        "No media folders are allowed any more, so there was nothing to inspect. Add one under Media folders in the user menu.";

    private const string AppErrorMessage = "The inspection stopped because of an error in the app. Its log says more.";

    /// <summary>A run that never reached its hashing step.</summary>
    private static readonly FileHashingDto NotHashed = new(null, 0, 0, 0, null);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private IFileHashDataFactory _hashes = null!;
    private AuthorizedUser _owner = null!;

    public FileHashCacheHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        _hashes = new FileHashDataFactory(db, _factory.Time);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Inspect_again_with_nothing_changed_reads_no_file_whole_and_keeps_each_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        var hashedUtc = Now();
        var first = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(new FileHashingDto(2, 2, 2, 0, null), first.Hashing);
        // Both still candidates, both answered from the cache: a whole read costs minutes per film.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 0, new FileHashingDto(2, 2, 0, 0, null)), (run.Status, run.FilesProbed, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        // The same rows, hashed when they were.
        Assert.Equal([StoredHash.Of(taken, hashedUtc), StoredHash.Of(copy, hashedUtc)], await HashesAsync(cancellationToken));
    }

    [Theory]
    [InlineData("size")]
    [InlineData("modification time")]
    [InlineData("inode")]
    [InlineData("device")]
    public async Task Inspect_after_a_change_of_one_key_part_hashes_the_file_again_and_forgets_the_old_hash(string changedPart)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        // Two pairs of one size each, so the changed file still shares a size when it changes its own.
        foreach (var path in new[] { CopyPath, FirstFilmPath, SecondFilmPath, TakenPath })
            await PlantFileAsync(path);
        var copy = AddTaken(CopyPath, CopyInode);
        var firstFilm = AddTaken(FirstFilmPath, FirstFilmInode, TakenSizeBytes + 1);
        var secondFilm = AddTaken(SecondFilmPath, SecondFilmInode, TakenSizeBytes + 1);
        var taken = AddTaken(TakenPath, TakenInode);
        var hashedUtc = Now();
        var first = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // The same name now leads to other bytes: re-encoded, touched, replaced, or on another disk.
        var changed = ChangedFacts(changedPart);
        _factory.Files.AddFile(TakenPath, changed, TestMedia.Taken());
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var changedUtc = Now();
        var changing = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var afterChange = await HashesAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(new FileHashingDto(4, 4, 4, 0, null), first.Hashing);
        // The ticket's "done when": a changed size or modification time (or identity) invalidates the cached
        // hash, so the file is read whole again — only it. Grown by a byte, it shares the other pair's size and
        // leaves the backup with a size of its own.
        var candidates = changedPart == "size" ? 3 : 4;
        Assert.Equal(new FileHashingDto(candidates, candidates, 1, 0, null), changing.Hashing);
        Assert.Equal([CopyPath, FirstFilmPath, SecondFilmPath, TakenPath, TakenPath], _factory.Files.HashedPaths);
        // Kept under the new key; the old one goes in the run that found the change, with the reading of the
        // old bytes: neither a name nor a reading stands for them any more.
        var kept = Ordered(
            [
                StoredHash.Of(copy, hashedUtc),
                StoredHash.Of(firstFilm, hashedUtc),
                StoredHash.Of(secondFilm, hashedUtc),
                StoredHash.Of(changed, changedUtc),
            ]);
        Assert.Equal(kept, afterChange);
        Assert.DoesNotContain(StoredHash.Of(taken, hashedUtc), afterChange);
        // The next run reads nothing whole and keeps them all.
        Assert.Equal((FileInspectionRunStatus.Succeeded, 0), (run.Status, run.Hashing.Read));
        Assert.Equal(5, _factory.Files.HashedPaths.Count);
        Assert.Equal(kept, await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_deletes_a_hash_no_name_has_and_trusts_the_one_a_name_has()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        // A hash an earlier run kept for the film, and one left behind: its file's name was dropped by a sync since.
        var earlier = Now().AddDays(-1);
        await _hashes.Create(taken.DeviceId!.Value, TakenInode, TakenSizeBytes, taken.ModifiedUtc, FakeFileInventory.DefaultSha256(taken), earlier);
        await _hashes.Create(TestMedia.DeviceId, LeftBehindInode, 3_891_719_630, TestMedia.ModifiedUtc, new string('c', 64), earlier);
        var now = Now();

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The film is answered from the stored hash; only the backup is read whole.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 1, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath], _factory.Files.HashedPaths);
        // The row neither a name's key nor a reading matches is gone at the end of the run; the ones the names
        // have stay.
        Assert.Equal([StoredHash.Of(taken, earlier), StoredHash.Of(copy, now)], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_while_a_sync_replaces_a_name_keeps_its_hash_and_the_next_run_finds_it_without_reading()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var original = await PlantFileAsync(TakenPath);
        await PlantFileAsync(CopyPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        var hashedUtc = Now();
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // A new film keeps the next run reading while a sync lands: the film was renamed on disk, and the sync
        // replaces its name with a new row no run has looked at yet.
        await PlantFileAsync(ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode), TestMedia.Continental());
        _factory.Files.DuringRead = async (_, token) =>
        {
            await _factory.WithDbAsync(db => db.PhysicalFiles.Where(file => file.Id == original.Id).ExecuteDeleteAsync(token));
            await PlantFileAsync(LinkPath);
            _factory.Files.Remove(TakenPath);
            AddTaken(LinkPath, TakenInode);
        };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var syncedOver = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var afterSync = await HashesAsync(cancellationToken);
        _factory.Files.DuringRead = null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The hash lost its only name to the sync, not to this run, which keeps the film's reading for the next
        // (ADR 0009) and the hash with it; and the backup has no partner this run knows of: nothing is hashed,
        // and nothing is forgotten.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(0, 0, 0, 0, null)), (syncedOver.Status, syncedOver.Hashing));
        Assert.Equal([StoredHash.Of(taken, hashedUtc), StoredHash.Of(copy, hashedUtc)], afterSync);
        // The run the sync queued looks at the new name and finds the film's bytes in it instead of reading them.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 0, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal(afterSync, await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_of_a_renamed_file_finds_its_hash_without_reading_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        var original = await PlantFileAsync(TakenPath);
        await PlantFileAsync(CopyPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        var hashedUtc = Now();
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Renamed on disk, and a sync replaced the old name with the new one: the same bytes, a new row.
        await _factory.WithDbAsync(db => db.PhysicalFiles.Where(file => file.Id == original.Id).ExecuteDeleteAsync(cancellationToken));
        await PlantFileAsync(LinkPath);
        _factory.Files.Remove(TakenPath);
        AddTaken(LinkPath, TakenInode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Kept by identity, not by name: no name had its key when the run started, yet the new name has it now,
        // so it survives the end of the run without a second read.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 0, 0, null)), (run.Status, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([StoredHash.Of(taken, hashedUtc), StoredHash.Of(copy, hashedUtc)], await HashesAsync(cancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Inspect_of_a_file_back_unchanged_after_runs_without_it_reads_nothing_whole(int runsAway)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        var copy = AddTaken(CopyPath, CopyInode);
        var taken = AddTaken(TakenPath, TakenInode);
        var hashedUtc = Now();
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // The mount is away for a run or several — a sync and a restart can each queue one meanwhile...
        _factory.Files.Remove(TakenPath);
        var away = new List<FileHashingDto>();
        for (var run = 0; run < runsAway; run++)
        {
            _factory.Time.Advance(TimeSpan.FromMinutes(1));
            away.Add((await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken)).Hashing);
        }

        // ...and is back with the same bytes.
        AddTaken(TakenPath, TakenInode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var back = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // The runs without it hashed nothing — the backup had no partner — and forgot nothing: the film's name
        // keeps its reading while it is away (ADR 0009), and the hash goes only with that.
        Assert.All(away, hashing => Assert.Equal(new FileHashingDto(0, 0, 0, 0, null), hashing));
        // So the film is answered from its hash: read whole once in all those runs.
        Assert.Equal((FileInspectionRunStatus.Succeeded, new FileHashingDto(2, 2, 0, 0, null)), (back.Status, back.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal([StoredHash.Of(taken, hashedUtc), StoredHash.Of(copy, hashedUtc)], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_after_every_media_folder_was_removed_hashes_nothing_and_deletes_no_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        AddTaken(CopyPath, CopyInode);
        AddTaken(TakenPath, TakenInode);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // A row left behind, which a successful run would forget.
        await _hashes.Create(TestMedia.DeviceId, LeftBehindInode, 3_891_719_630, TestMedia.ModifiedUtc, new string('c', 64));
        var before = await HashesAsync(cancellationToken);
        var start = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        // The owner empties the list while the run waits for the worker.
        var removal = await _factory.PutHttpRequest("/api/settings/allowed-roots", _owner.AccessToken, new UpdateAllowedRootsRequest([]));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var response = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.NoContent, HttpStatusCode.OK), (start.StatusCode, removal.StatusCode, response.StatusCode));
        // A failed run never reaches its last step: nothing read whole, nothing forgotten.
        var run = (await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((FileInspectionRunStatus.Failed, NoAllowedRootsError, NotHashed), (run.Status, run.Error, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal(3, before.Count);
        Assert.Equal(before, await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task Inspect_that_throws_before_its_hashing_step_hashes_nothing_and_deletes_no_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(Root);
        await PlantFileAsync(CopyPath);
        await PlantFileAsync(TakenPath);
        AddTaken(CopyPath, CopyInode);
        AddTaken(TakenPath, TakenInode);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _hashes.Create(TestMedia.DeviceId, LeftBehindInode, 3_891_719_630, TestMedia.ModifiedUtc, new string('c', 64));
        var before = await HashesAsync(cancellationToken);
        // The backup was rewritten, so it is a candidate with a hash to read again; but a bug while the new
        // film is read stops the run first.
        _factory.Files.AddFile(CopyPath, TestMedia.File(CopyInode, TakenSizeBytes, TestMedia.ModifiedUtc.AddHours(1)), TestMedia.Taken());
        await PlantFileAsync(ContinentalPath);
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(ContinentalInode), TestMedia.Continental());
        _factory.Files.DuringRead = (path, _) =>
            path == ContinentalPath ? throw new InvalidOperationException("A bug while reading.") : Task.CompletedTask;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((FileInspectionRunStatus.Failed, AppErrorMessage, NotHashed), (run.Status, run.Error, run.Hashing));
        Assert.Equal([CopyPath, TakenPath], _factory.Files.HashedPaths);
        Assert.Equal(before, await HashesAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    // The Taken file's facts with one part of the cache key changed. The modification time by a single tick:
    // the key keeps the file system's time exactly, so the smallest change a write leaves shows.
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

    // A file at appPath of sizeBytes (Taken's unless given) whose media facts are Taken's: as far as anything
    // but its bytes can tell, a copy of the film.
    private FileFacts AddTaken(string appPath, long inode, long sizeBytes = TakenSizeBytes)
    {
        var facts = TestMedia.File(inode, sizeBytes);
        _factory.Files.AddFile(appPath, facts, TestMedia.Taken());
        return facts;
    }

    // Every stored hash, in the order HashesAsync gives them.
    private static List<StoredHash> Ordered(IEnumerable<StoredHash> hashes) =>
        [.. hashes.OrderBy(hash => hash.Inode).ThenBy(hash => hash.DeviceId).ThenBy(hash => hash.SizeBytes).ThenBy(hash => hash.ModifiedTicks)];

    private async Task<List<StoredHash>> HashesAsync(CancellationToken cancellationToken)
    {
        var hashes = new List<StoredHash>();
        await _factory.WithDbAsync(async db =>
            hashes = Ordered((await db.FileHashes.AsNoTracking().ToListAsync(cancellationToken)).Select(StoredHash.Of)));
        return hashes;
    }

    /// <summary>A stored hash: its key, the modification time in ticks so a lost tick shows, the hash and when
    /// it was read.</summary>
    private sealed record StoredHash(long DeviceId, long Inode, long SizeBytes, long ModifiedTicks, string Sha256, DateTime CreatedUtc)
    {
        public static StoredHash Of(FileHashEntity hash) =>
            new(hash.DeviceId, hash.Inode, hash.SizeBytes, hash.ModifiedUtc.Ticks, hash.Sha256, hash.CreatedUtc);

        /// <summary>What a run keeps for a file with <paramref name="facts"/> read at
        /// <paramref name="createdUtc"/>: the fake's own hash of the facts.</summary>
        public static StoredHash Of(FileFacts facts, DateTime createdUtc) =>
            new(facts.DeviceId!.Value, facts.Inode!.Value, facts.SizeBytes, facts.ModifiedUtc.Ticks, FakeFileInventory.DefaultSha256(facts), createdUtc);
    }
}
