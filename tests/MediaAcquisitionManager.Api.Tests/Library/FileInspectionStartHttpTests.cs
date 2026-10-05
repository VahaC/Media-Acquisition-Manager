using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-9 — asking for an inspection of the library's files (POST /api/library/inspections) and reading one
// run (GET /api/library/inspections/{id}). Asking is the owner's alone and only queues a run (202 + run row,
// Queue/Worker): no file is looked at before the worker takes it. At most one run waits at a time, so a
// request that finds one queued joins it, whoever queued it, while a run under way gets a new one behind it.
// With no media folder allowed nothing is written; a full queue keeps its run as the failed attempt. A run
// is read by every signed-in user, its requester and error by the owner only.
public sealed class FileInspectionStartHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/inspections";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string MediaRoot = "/media";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string QueueFullError = "Too many file inspections are waiting. Try again in a minute.";
    private const string RestartedError = "The app restarted before this inspection finished. What it had read is kept.";

    private const string NoAllowedRootsError =
        "No media folders are allowed yet. Add the folders the app may read under Media folders in the user menu, then inspect again.";

    // Where MediaWorkSeeder's one file is, as the library reports it and, with no mapping, as the app finds it.
    private const string AliensPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    /// <summary>MAM-19 — a run that never reached its hashing step: queued, failed before it, or planted.</summary>
    private static readonly FileHashingDto NotHashed = new(null, 0, 0, 0, null);

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IAllowedRootSeeder _roots = null!;
    private IFileInspectionRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public FileInspectionStartHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _roots = new AllowedRootSeeder(_factory.Scope);
        _runs = new FileInspectionRunSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Start_as_the_owner_is_202_with_the_queued_run_and_looks_at_no_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new MediaWorkSeeder(_factory.Scope).Create(jellyfin);
        // A file the run could read: none looked at means none was.
        _factory.Files.AddFile(AliensPath, TestMedia.File(inode: 101), TestMedia.Taken());
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.Equal($"{BaseUrl}/{run.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(
            new FileInspectionRunDto(run.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Queued, "owner", now, null, null, null, 0, 0, NotHashed, null),
            run);
        // Only queued: the worker takes it, and nothing has been looked at or read yet.
        Assert.Equal([run.Id], DrainQueue());
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Empty(_factory.Files.ReadPaths);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.FileInspectionRuns.SingleAsync(cancellationToken);
            Assert.Equal(run.Id, row.Id);
            Assert.Equal((FileInspectionTrigger.Requested, FileInspectionRunStatus.Queued), (row.Trigger, row.Status));
            Assert.Equal((_owner.User.Id, "owner"), (row.RequestedByUserId, row.RequestedByUserName));
            Assert.Equal((now, null, null, null), (row.CreatedUtc, row.StartedUtc, row.FinishedUtc, row.FilesTotal));
            Assert.Null((await db.PhysicalFiles.SingleAsync(cancellationToken)).InspectedUtc);
            // The run row records who asked; a request writes no audit row of its own, like a sync's.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Start_without_an_allowed_root_is_409_with_the_sentence_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // No folder is allowed by default, so the app may read nothing: the owner is told where to allow one.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoAllowedRootsError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task Start_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // An inspection reads the owner's disks: allowed folders and all, it is the owner's to ask for.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task Start_without_a_token_is_401_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task Start_twice_answers_both_with_the_one_queued_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);

        //Act
        var first = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);
        var second = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The waiting run reads every file there is when it starts, so a second would only read them again.
        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (first.StatusCode, second.StatusCode));
        var firstRun = await ReadRunAsync(first, cancellationToken);
        Assert.Equal(firstRun, await ReadRunAsync(second, cancellationToken));
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        // Joined, not queued again: the worker gets the id once.
        Assert.Equal([firstRun.Id], DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(firstRun.Id, (await db.FileInspectionRuns.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Start_twice_at_once_answers_both_with_one_queued_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);

        //Act
        // A double click: whichever way the two interleave, one run is queued and both requests get it.
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken),
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken));

        //Assert
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var runIds = new List<Guid>();
        foreach (var response in responses)
            runIds.Add((await ReadRunAsync(response, cancellationToken)).Id);
        var runId = Assert.Single(runIds.Distinct());
        Assert.Equal([runId], DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(runId, (await db.FileInspectionRuns.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Start_while_a_run_is_running_queues_a_new_run_behind_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var running = await _runs.Create(
            FileInspectionRunStatus.Running, requestedBy: _owner.User, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-5), filesTotal: 870, filesChecked: 120);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The running one listed its files when it started; files a sync named since are only read by a
        // run behind it.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.NotEqual(running.Id, run.Id);
        Assert.Equal((FileInspectionRunStatus.Queued, now), (run.Status, run.CreatedUtc));
        Assert.Equal([run.Id], DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.FileInspectionRuns.ToDictionaryAsync(row => row.Id, cancellationToken);
            Assert.Equal(2, rows.Count);
            // The running one is left alone.
            Assert.Equal((FileInspectionRunStatus.Running, 870, 120), (rows[running.Id].Status, rows[running.Id].FilesTotal, rows[running.Id].FilesChecked));
            Assert.Equal(FileInspectionRunStatus.Queued, rows[run.Id].Status);
        });
    }

    [Theory]
    [InlineData(FileInspectionTrigger.LibrarySynced)]
    [InlineData(FileInspectionTrigger.AppStarted)]
    public async Task Start_while_a_run_queued_by_the_app_waits_is_202_with_that_run(FileInspectionTrigger trigger)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var queuedAt = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-1);
        var waiting = await _runs.Create(FileInspectionRunStatus.Queued, trigger, createdUtc: queuedAt);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Joined as it is: still the sync's or the start's, with nobody named as having asked for it.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"{BaseUrl}/{waiting.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(
            new FileInspectionRunDto(waiting.Id, trigger, FileInspectionRunStatus.Queued, null, queuedAt, null, null, null, 0, 0, NotHashed, null),
            await ReadRunAsync(response, cancellationToken));
        // Whoever queued it queued its id; joining adds no second one.
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.FileInspectionRuns.SingleAsync(cancellationToken);
            Assert.Equal((waiting.Id, trigger, null), (row.Id, row.Trigger, row.RequestedByUserId));
        });
    }

    [Theory]
    [InlineData(FileInspectionRunStatus.Succeeded, null)]
    [InlineData(FileInspectionRunStatus.Failed, RestartedError)]
    public async Task Start_after_a_finished_run_is_202_and_adds_a_second_run(FileInspectionRunStatus status, string? error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var finished = await _runs.Create(status, requestedBy: _owner.User, createdUtc: _factory.Time.GetUtcNow().UtcDateTime.AddHours(-1), error: error);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Only a queued run is joined; a finished one is history.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.NotEqual(finished.Id, run.Id);
        Assert.Equal([run.Id], DrainQueue());
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.FileInspectionRuns.ToDictionaryAsync(row => row.Id, cancellationToken);
            Assert.Equal(2, rows.Count);
            Assert.Equal((status, error), (rows[finished.Id].Status, rows[finished.Id].Error));
            Assert.Equal(FileInspectionRunStatus.Queued, rows[run.Id].Status);
        });
    }

    [Fact]
    public async Task Start_with_the_queue_full_is_503_and_keeps_the_run_as_failed_with_the_same_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        var queue = _factory.Resolve<FileInspectionQueue>();
        for (var index = 0; index < FileInspectionQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(QueueFullError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // Failed, not left queued: a queued run no worker will take would be joined by every later
            // request and never run.
            var row = await db.FileInspectionRuns.SingleAsync(cancellationToken);
            Assert.Equal((FileInspectionRunStatus.Failed, QueueFullError, now), (row.Status, row.Error, row.FinishedUtc));
            Assert.Equal((FileInspectionTrigger.Requested, "owner"), (row.Trigger, row.RequestedByUserName));
        });
        // Warning, so it ships off the box: a full queue means the worker is stuck.
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(FileInspectionQueue).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task Start_when_a_sync_queues_its_run_first_is_202_with_that_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _roots.Allow(MediaRoot);
        FileInspectionRunEntity? competing = null;
        // Deterministic race: both found nothing queued, and a sync finishing as the owner clicks inserts
        // its run just before this request's — through a context of its own, like the sync's runner.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<FileInspectionRunEntity>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                competing = await new FileInspectionRunSeeder(scope.ServiceProvider).Create(FileInspectionRunStatus.Queued, FileInspectionTrigger.LibrarySynced);
            });

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // The partial unique index keeps one queued run, and the loser joins it: a 202, not a 500.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(competing);
        var run = await ReadRunAsync(response, cancellationToken);
        Assert.Equal((competing.Id, FileInspectionTrigger.LibrarySynced), (run.Id, run.Trigger));
        // The winner queues its own id; the request that joined it queues none.
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(competing.Id, (await db.FileInspectionRuns.SingleAsync(cancellationToken)).Id));
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(FileInspectionService).FullName && entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task GetRun_as_the_owner_is_200_with_who_asked_and_the_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var failed = await _runs.Create(
            FileInspectionRunStatus.Failed, requestedBy: _owner.User, createdUtc: at, error: RestartedError, filesTotal: 870, filesChecked: 412, filesProbed: 37);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{failed.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new FileInspectionRunDto(failed.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Failed, "owner", at, at, at, 870, 412, 37, NotHashed, RestartedError),
            await ReadRunAsync(response, cancellationToken));
    }

    [Fact]
    public async Task GetRun_as_a_signed_in_non_owner_is_200_without_who_asked_or_the_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var at = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        var failed = await _runs.Create(
            FileInspectionRunStatus.Failed, requestedBy: _owner.User, createdUtc: at, error: RestartedError, filesTotal: 870, filesChecked: 412, filesProbed: 37);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{failed.Id}", _member.AccessToken);

        //Assert
        // Everyone reads the library, so everyone may see where its inspection stands; who asked (the
        // owner's sign-in name) and why it failed stay the owner's, like a sync's.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("owner", body, StringComparison.Ordinal);
        Assert.DoesNotContain("restarted", body, StringComparison.Ordinal);
        Assert.Equal(
            new FileInspectionRunDto(failed.Id, FileInspectionTrigger.Requested, FileInspectionRunStatus.Failed, null, at, at, at, 870, 412, 37, NotHashed, null),
            await ReadRunAsync(response, cancellationToken));
    }

    [Fact]
    public async Task GetRun_of_an_unknown_run_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _runs.Create(FileInspectionRunStatus.Succeeded, requestedBy: _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task GetRun_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = await _runs.Create(FileInspectionRunStatus.Succeeded, requestedBy: _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{run.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task<FileInspectionRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    // Every run id waiting for the worker, in queue order; the queue is empty afterwards.
    private List<Guid> DrainQueue()
    {
        var queue = _factory.Resolve<FileInspectionQueue>();
        var runIds = new List<Guid>();
        while (queue.Reader.TryRead(out var runId))
            runIds.Add(runId);
        return runIds;
    }

    private async Task AssertNothingQueuedAsync(CancellationToken cancellationToken)
    {
        Assert.Empty(DrainQueue());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken)));
    }
}
