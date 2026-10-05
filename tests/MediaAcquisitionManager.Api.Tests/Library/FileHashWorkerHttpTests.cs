using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
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
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-19 — hashing in the background survives a restart the way the inspection it ends does (Vasyl's decision
// 1, MAM-9's Queue/Worker + reconciler): a shutdown while a file is read whole leaves the run running with the
// hashes it stored and its counts so far; the reconciler at the next start fails it and queues the start's own
// run, which answers the stored hashes from the cache and reads only the rest. A bug while hashing fails that
// run with the app's sentence, keeps what it stored and forgets no hash. Driven the way the host drives it: the
// worker's pass over its queue and the reconciler's startup pass, with runs asked for over real HTTP. What one
// run does is FileInspectionHashHttpTests'; what it keeps for the next FileHashCacheHttpTests'.
public sealed class FileHashWorkerHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string MediaRoot = "/media";
    private const string AppErrorMessage = "The inspection stopped because of an error in the app. Its log says more.";
    private const string RestartedError = "The app restarted before this inspection finished. What it had read is kept.";
    private const long TakenSizeBytes = 30_870_249_472;

    /// <summary>The inode of a file whose name a sync dropped: its hash is left behind.</summary>
    private const long LeftBehindInode = 1_048_617;

    // Three films of one size whose media facts agree, in path order: each one's bytes need a hash.
    private static readonly string[] FilmPaths =
    [
        "/media/movies/Film 1/Film 1.mkv",
        "/media/movies/Film 2/Film 2.mkv",
        "/media/movies/Film 3/Film 3.mkv",
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;

    public FileHashWorkerHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    public async Task RunQueued_stopped_by_a_shutdown_while_hashing_keeps_what_it_hashed_and_the_start_run_hashes_only_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var films = await PlantFilmsAsync();
        var runId = await StartAsync(cancellationToken);
        using var shutdown = new CancellationTokenSource();
        // The host stops while the second film is read whole: the read is cancelled under the worker.
        _factory.Files.DuringHash = (path, token) =>
        {
            if (path != FilmPaths[1] || shutdown.IsCancellationRequested)
                return Task.CompletedTask;
            shutdown.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var stopped = await Record.ExceptionAsync(() => _factory.Resolve<FileInspectionWorker>().RunQueuedAsync(shutdown.Token));
        var afterStop = await RunRowAsync(runId, cancellationToken);
        var hashesAfterStop = await HashesAsync(cancellationToken);
        var hashedBeforeRestart = _factory.Files.HashedPaths.Count;
        await _factory.Resolve<FileInspectionReconciler>().ReconcileAsync(cancellationToken);
        var startRunId = await StartRunIdAsync(cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var stoppedRun = await _factory.GetHttpRequest($"{BaseUrl}/{runId}", _owner.AccessToken);
        var startRun = await _factory.GetHttpRequest($"{BaseUrl}/{startRunId}", _owner.AccessToken);

        //Assert
        // A shutdown is not a bug: nothing logged as an error, no outcome invented. The run is left running with
        // the first film's hash stored and counted, every file looked at and read before the step began.
        Assert.IsAssignableFrom<OperationCanceledException>(stopped);
        Assert.Equal((FileInspectionRunStatus.Running, null, null), (afterStop.Status, afterStop.FinishedUtc, afterStop.Error));
        Assert.Equal(
            ((int?)3, 3, 3, (int?)3, 1, 1, 0),
            (afterStop.FilesTotal, afterStop.FilesChecked, afterStop.FilesProbed, afterStop.HashCandidates, afterStop.HashCandidatesChecked, afterStop.HashCandidatesRead, afterStop.HashProblemCount));
        Assert.Equal([StoredHash.Of(films[0], now)], hashesAfterStop);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        // The next start's reconciler settles it, with how far its hashing got...
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (stoppedRun.StatusCode, startRun.StatusCode));
        var stoppedDto = await ReadRunAsync(stoppedRun, cancellationToken);
        Assert.Equal(
            (FileInspectionRunStatus.Failed, RestartedError, new FileHashingDto(3, 1, 1, 0, null)),
            (stoppedDto.Status, stoppedDto.Error, stoppedDto.Hashing));
        // ...and the start's own run carries on: every reading and the stored hash answered from the caches, the
        // two films not hashed yet read whole — and only they.
        Assert.Equal(
            new FileInspectionRunDto(
                startRunId, FileInspectionTrigger.AppStarted, FileInspectionRunStatus.Succeeded, null, now, now, now, 3, 3, 0,
                new FileHashingDto(3, 3, 2, 0, null), null),
            await ReadRunAsync(startRun, cancellationToken));
        Assert.Equal([FilmPaths[1], FilmPaths[2]], _factory.Files.HashedPaths.Skip(hashedBeforeRestart));
        Assert.Equal([.. films.Select(film => StoredHash.Of(film, now))], await HashesAsync(cancellationToken));
    }

    [Fact]
    public async Task RunQueued_with_a_run_that_throws_while_hashing_fails_it_with_the_app_error_keeps_what_it_hashed_and_forgets_no_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var films = await PlantFilmsAsync();
        // A row left behind, which only a run that finishes forgets.
        var earlier = _factory.Time.GetUtcNow().UtcDateTime.AddDays(-1);
        await new FileHashDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time)
            .Create(TestMedia.DeviceId, LeftBehindInode, 3_891_719_630, TestMedia.ModifiedUtc, new string('c', 64), earlier);
        // A bug, not an answer the inventory describes: nothing in the run catches it.
        _factory.Files.DuringHash = (path, _) =>
            path == FilmPaths[1] ? throw new InvalidOperationException("A bug while hashing.") : Task.CompletedTask;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var run = await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Failed with the app's sentence, never the exception's text; how far the hashing got stays on the run.
        Assert.Equal(
            new FileInspectionRunDto(
                run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Failed, "owner", now, now, now, 3, 3, 3,
                new FileHashingDto(3, 1, 1, 0, null), AppErrorMessage),
            run);
        Assert.Equal([FilmPaths[0], FilmPaths[1]], _factory.Files.HashedPaths);
        // The first film's hash is kept for the next run; the row left behind waits for a run that finishes.
        Assert.Equal(
            [StoredHash.Of(films[0], now), new StoredHash(TestMedia.DeviceId, LeftBehindInode, 3_891_719_630, TestMedia.ModifiedUtc.Ticks, new string('c', 64), earlier)],
            await HashesAsync(cancellationToken));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionWorker).FullName);
        Assert.Equal((LogLevel.Error, $"File inspection {run.Id} threw out of the runner."), (error.Level, error.Message));
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The three films, each a movie with one version naming it and a file of Taken's size and facts on disk.
    private async Task<List<FileFacts>> PlantFilmsAsync()
    {
        var films = new List<FileFacts>();
        foreach (var (path, index) in FilmPaths.Select((path, index) => (path, index)))
        {
            var work = await _works.Create();
            var version = await _versions.Create(work.Id, $"{Guid.NewGuid():N}");
            await _physicalFiles.Create(version.Id, path);
            var facts = TestMedia.File(1_048_577 + (index * 10), TakenSizeBytes);
            _factory.Files.AddFile(path, facts, TestMedia.Taken());
            films.Add(facts);
        }

        return films;
    }

    // The owner's request over HTTP; the queued run's id.
    private async Task<Guid> StartAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await ReadRunAsync(response, cancellationToken)).Id;
    }

    // The run the reconciler queued for the app's start.
    private async Task<Guid> StartRunIdAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.Empty;
        await _factory.WithDbAsync(async db => runId = await db.FileInspectionRuns
            .Where(run => run.Trigger == FileInspectionTrigger.AppStarted)
            .Select(run => run.Id)
            .SingleAsync(cancellationToken));
        return runId;
    }

    private async Task<FileInspectionRunEntity> RunRowAsync(Guid runId, CancellationToken cancellationToken)
    {
        FileInspectionRunEntity? run = null;
        await _factory.WithDbAsync(async db => run = await db.FileInspectionRuns.AsNoTracking().SingleAsync(row => row.Id == runId, cancellationToken));
        return run!;
    }

    // Every stored hash, in inode order.
    private async Task<List<StoredHash>> HashesAsync(CancellationToken cancellationToken)
    {
        var hashes = new List<StoredHash>();
        await _factory.WithDbAsync(async db =>
            hashes = [.. (await db.FileHashes.AsNoTracking().ToListAsync(cancellationToken)).Select(StoredHash.Of).OrderBy(hash => hash.Inode)]);
        return hashes;
    }

    private static async Task<FileInspectionRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

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
