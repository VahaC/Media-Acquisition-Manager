using System.Net;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030 — Jellyfin is told after every move and every restore, and nothing more: IReplacementLibraryNotice, asked
// from a scope of its own as the mover's pass and the owner's Restore ask it, sends POST /Library/Media/Updated with each name
// the quarantine holds as "Deleted" and each name put back as "Created", at its old place in Jellyfin's own terms — a video
// by the path Jellyfin reported for the copy —, one request per library and change, each path once, never a quarantine path.
// It settles the notice once Jellyfin took it, keeps it owed while Jellyfin can't take it, gives it up with a Warning once
// the hour from its falling due ran out, and gives it up at once where no library can ever be told. It never changes the
// job's state, and owes nothing unless the job says so. The reader's own reasons for a notice are pinned here too.
public sealed class ReplacementLibraryNoticeTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string MediaRoot = "/media";
    private const string VideoPath = "/media/movies/Alien (1979)/Alien (1979).mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979).uk.srt";
    private const string InfoPath = "/media/movies/Alien (1979)/Alien (1979).nfo";

    // The same files as Jellyfin names them: its /data is the app's /media. Spelled out, never worked out.
    private const string JellyfinVideoPath = "/data/movies/Alien (1979)/Alien (1979).mkv";
    private const string JellyfinSubtitlePath = "/data/movies/Alien (1979)/Alien (1979).uk.srt";

    private const string UpdatedCall = "POST /Library/Media/Updated";
    private const string HeldReason = "Held by the torrent “Alien.1979” in “qBittorrent”, so it stays where it is.";

    // The reader's reasons for a notice, spelled out rather than read from product code.
    private const string SwitchedOffNoticeReason = "“Living room” is switched off, so it can't be told that the old copies moved.";
    private const string RemovedNoticeReason = "The library the old copies were in was removed from Integrations, so it can't be told that they moved.";
    private const string RefusedNoticeReason =
        "The app couldn't tell “Living room” that the old copies moved: Jellyfin answered 500 Internal Server Error. Its own log says why.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private int _jellyfinRequestsBefore;

    public ReplacementLibraryNoticeTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── what it says ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendDue_after_a_move_tells_jellyfin_in_one_request_that_the_video_and_its_companion_went_and_settles_the_notice()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film and its subtitle moved; its .nfo stayed, as a torrent lists it.
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, InfoPath, ReplacementNameState.Stayed),
            ]);
        _factory.Logs.Clear();

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        // One request, each name that went at its old path in Jellyfin's own terms, and nothing else written anywhere.
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal([JellyfinVideoPath, JellyfinSubtitlePath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Equal(["Deleted", "Deleted"], Assert.Single(_harness.JellyfinServer.ScanUpdateTypes));
        // Taken: settled, and the job stays where the move left it.
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null, (DateTime?)null), (row.State, row.Reason, row.LibraryNoticeDueUtc));
        Assert.Equal(job.StateChangedUtc, row.StateChangedUtc);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task SendDue_tells_jellyfin_of_a_video_by_the_path_it_reported_for_the_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Фільми reaches the films' folder twice — as /data/movies and as /films, two mounts of one folder — and Jellyfin
        // reported the old copy under /films: the spelling its item is found by. The subtitle, which Jellyfin never
        // reported, goes by the library's first folder.
        await AddJellyfinMountAsync("/films", "/media/movies", cancellationToken);
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
            ],
            copyServicePath: "/films/Alien (1979)/Alien (1979).mkv");

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        Assert.Equal(["/films/Alien (1979)/Alien (1979).mkv", JellyfinSubtitlePath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Null((await _harness.ReplacementAsync(cancellationToken)).LibraryNoticeDueUtc);
    }

    [Fact]
    public async Task SendDue_after_a_restore_tells_jellyfin_that_the_names_came_back_as_created()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Restored),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Restored),
            ],
            ReplacementJobState.Restored);

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal([JellyfinVideoPath, JellyfinSubtitlePath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Equal(["Created", "Created"], Assert.Single(_harness.JellyfinServer.ScanUpdateTypes));
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Restored, (DateTime?)null), (row.State, row.LibraryNoticeDueUtc));
    }

    [Fact]
    public async Task SendDue_after_a_restore_cut_short_tells_what_came_back_as_created_and_what_is_still_in_the_quarantine_as_deleted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film came back; its subtitle couldn't, and waits in the quarantine for the owner.
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Restored),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
            ],
            ReplacementJobState.NeedsReview,
            reason: "“Alien (1979).uk.srt” couldn't be put back: something is already there. 1 of 2 names is back where it was.");

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        // One request per change, each with its own update type.
        Assert.Equal([UpdatedCall, UpdatedCall], _harness.Writes());
        Assert.Equal([[JellyfinVideoPath], [JellyfinSubtitlePath]], _harness.JellyfinServer.ScanRequests.ToArray());
        Assert.Equal([["Created"], ["Deleted"]], _harness.JellyfinServer.ScanUpdateTypes.ToArray());
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, job.Reason, (DateTime?)null), (row.State, row.Reason, row.LibraryNoticeDueUtc));
    }

    [Fact]
    public async Task SendDue_to_two_libraries_of_one_jellyfin_sends_one_request_naming_each_path_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Фільми 4K lists the same folder as Фільми: each name is in two libraries of one Jellyfin, by one path.
        await new LibraryLocationSeeder(_factory.Scope).Create(_harness.Jellyfin, "Фільми 4K", ReplacementVerificationHarness.FilmsLibraryFolder);
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
            ]);

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        Assert.Equal([UpdatedCall], _harness.Writes());
        Assert.Equal([JellyfinVideoPath, JellyfinSubtitlePath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Null((await _harness.ReplacementAsync(cancellationToken)).LibraryNoticeDueUtc);
    }

    [Fact]
    public async Task SendDue_never_names_a_quarantine_path_even_where_the_quarantine_lies_in_a_library_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The allowed folder is Фільми's own folder, so its quarantine lies inside the library (ADR 0030): Jellyfin hears of
        // the old paths only.
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
            ],
            allowedRoot: ReplacementVerificationHarness.FilmsFolder);

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        var paths = Assert.Single(_harness.JellyfinServer.ScanRequests);
        Assert.Equal([JellyfinVideoPath, JellyfinSubtitlePath], paths);
        Assert.DoesNotContain(paths, path => path.Contains(".mam-quarantine", StringComparison.Ordinal));
    }

    // ── when it is owed ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendDue_of_a_job_that_owes_no_notice_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Moved, and already told.
        var job = await PlantJobAsync(
            libraryNoticeDueUtc: null,
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
            ]);

        //Act
        await SendDueAsync(job.Id, cancellationToken);
        await SendDueAsync(Guid.NewGuid(), cancellationToken);

        //Assert
        Assert.Empty(_harness.JellyfinServer.ScanRequests);
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(_jellyfinRequestsBefore));
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (DateTime?)null), (row.State, row.LibraryNoticeDueUtc));
    }

    [Fact]
    public async Task SendDue_with_no_name_moved_or_put_back_settles_the_notice_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A move that stopped before its first rename: nothing the library holds went or came back.
        var job = await PlantJobAsync(
            Now(),
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Planned),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Stayed),
                (ReplacementNameKind.Companion, InfoPath, ReplacementNameState.Lost),
            ],
            ReplacementJobState.NeedsReview);

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        Assert.Empty(_harness.JellyfinServer.ScanRequests);
        Assert.Empty(_harness.Writes());
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (DateTime?)null), (row.State, row.LibraryNoticeDueUtc));
    }

    [Fact]
    public async Task SendDue_where_jellyfin_refuses_keeps_the_notice_owed_until_the_hour_ran_out_then_gives_it_up_with_a_warning()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dueUtc = Now();
        var job = await PlantJobAsync(
            dueUtc,
            [
                (ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined),
                (ReplacementNameKind.Companion, SubtitlePath, ReplacementNameState.Quarantined),
            ]);
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.InternalServerError;
        _factory.Logs.Clear();

        //Act
        await SendDueAsync(job.Id, cancellationToken);
        var afterFirst = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(59));
        await SendDueAsync(job.Id, cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        var warningsBeforeTheHour = Warnings();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await SendDueAsync(job.Id, cancellationToken);
        var atTheHour = await _harness.ReplacementAsync(cancellationToken);
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        // Owed while the hour runs — each call sends it again —, then given up after one last try: never a third copy of the
        // request once settled.
        Assert.Equal((dueUtc, dueUtc, (DateTime?)null), (afterFirst.LibraryNoticeDueUtc, beforeTheHour.LibraryNoticeDueUtc, atTheHour.LibraryNoticeDueUtc));
        Assert.Equal(3, _harness.JellyfinServer.ScanRequests.Count);
        Assert.Equal([UpdatedCall, UpdatedCall, UpdatedCall], _harness.Writes());
        Assert.Empty(warningsBeforeTheHour);
        var warning = Assert.Single(Warnings());
        Assert.Equal($"The library notice of replacement {job.Id} was given up: a library couldn't be told within 01:00:00 of it falling due.", warning.Message);
        // A notice never moves the job.
        Assert.All([afterFirst, beforeTheHour, atTheHour], row => Assert.Equal((ReplacementJobState.Quarantined, job.StateChangedUtc), (row.State, row.StateChangedUtc)));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task SendDue_where_jellyfin_is_switched_off_keeps_the_notice_owed_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dueUtc = Now();
        var job = await PlantJobAsync(dueUtc, [(ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined)]);
        await SwitchOffJellyfinAsync(cancellationToken);

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        // Switched off is "not now": the owner may switch it on within the hour.
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(_jellyfinRequestsBefore));
        Assert.Equal(dueUtc, (await _harness.ReplacementAsync(cancellationToken)).LibraryNoticeDueUtc);
    }

    [Fact]
    public async Task SendDue_where_the_library_was_removed_settles_the_notice_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(Now(), [(ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined)]);
        await _factory.WithDbAsync(db => db.IntegrationConnections.Where(row => row.Id == _harness.Jellyfin.Id).ExecuteDeleteAsync(cancellationToken));

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        // Its libraries went with it: no library holds the old path any more, and none is left to tell.
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(_jellyfinRequestsBefore));
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (DateTime?)null), (row.State, row.LibraryNoticeDueUtc));
    }

    [Fact]
    public async Task SendDue_where_the_library_can_never_be_told_settles_the_notice_at_once_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Мультфільми is a library of a type this version can't ask to look at a file, holding the old copy's folder.
        const string cartoonPath = "/media/cartoons/Robots (2005)/Robots (2005).mkv";
        var other = await new IntegrationConnectionSeeder(_factory.Scope).Create(name: "Another library");
        await new LibraryLocationSeeder(_factory.Scope).Create(other, "Мультфільми", "/media/cartoons");
        var job = await PlantJobAsync(Now(), [(ReplacementNameKind.Video, cartoonPath, ReplacementNameState.Quarantined)]);
        _factory.Logs.Clear();

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        // No wait would cure it: given up at once, without a Warning, and Jellyfin, which doesn't hold it, hears nothing.
        Assert.Empty(_harness.JellyfinServer.ScanRequests);
        var row = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (DateTime?)null), (row.State, row.LibraryNoticeDueUtc));
        Assert.Empty(Warnings());
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Message == $"The library notice of replacement {job.Id} was given up: a library can never be told.");
    }

    [Fact]
    public async Task SendDue_settles_only_the_notice_it_read_never_one_made_due_again_meanwhile()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A restore makes the notice due again while the move's notice is being sent.
        var job = await PlantJobAsync(Now(), [(ReplacementNameKind.Video, VideoPath, ReplacementNameState.Quarantined)]);
        var laterDueUtc = Now().AddMinutes(1);
        _harness.JellyfinServer.OnScanRequested = _ => _factory.WithDbAsync(db => db.ReplacementJobs
            .Where(row => row.Id == job.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LibraryNoticeDueUtc, laterDueUtc))).GetAwaiter().GetResult();

        //Act
        await SendDueAsync(job.Id, cancellationToken);

        //Assert
        Assert.Single(_harness.JellyfinServer.ScanRequests);
        Assert.Equal(laterDueUtc, (await _harness.ReplacementAsync(cancellationToken)).LibraryNoticeDueUtc);
    }

    // ── the reader's notice ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tell_of_a_switched_off_library_waits_with_the_notices_own_reason_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SwitchOffJellyfinAsync(cancellationToken);

        //Act
        var answer = await TellAsync(_harness.Jellyfin.Id, [JellyfinVideoPath], LibraryFileChange.Deleted, cancellationToken);

        //Assert
        Assert.Equal((ReplacementCheckStatus.Wait, "Living room", (string?)SwitchedOffNoticeReason), (answer.Status, answer.LibraryName, answer.Reason));
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(_jellyfinRequestsBefore));
    }

    [Fact]
    public async Task Tell_of_a_library_that_refuses_waits_with_its_own_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.InternalServerError;

        //Act
        var answer = await TellAsync(_harness.Jellyfin.Id, [JellyfinVideoPath], LibraryFileChange.Created, cancellationToken);

        //Assert
        Assert.Equal((ReplacementCheckStatus.Wait, (string?)RefusedNoticeReason), (answer.Status, answer.Reason));
        Assert.Equal(["Created"], Assert.Single(_harness.JellyfinServer.ScanUpdateTypes));
    }

    [Fact]
    public async Task Tell_of_a_library_that_was_removed_is_given_up_with_the_notices_own_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var removed = Guid.NewGuid();

        //Act
        var answer = await TellAsync(removed, [JellyfinVideoPath], LibraryFileChange.Deleted, cancellationToken);

        //Assert
        Assert.Equal((ReplacementCheckStatus.Review, removed.ToString(), (string?)RemovedNoticeReason), (answer.Status, answer.LibraryName, answer.Reason));
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(_jellyfinRequestsBefore));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// A replacement of a downloaded release in <paramref name="state"/> — moved, with <paramref name="reason"/> when it
    /// stopped — owing a library notice since <paramref name="libraryNoticeDueUtc"/> or none, whose one ticked copy is the
    /// first name's video (Jellyfin's path of it <paramref name="copyServicePath"/>, or the one its /data spells), and its
    /// <paramref name="names"/> in their order, each in its state, waiting in <paramref name="allowedRoot"/>'s quarantine.
    /// Then every request so far is behind the marks.
    /// </summary>
    private async Task<ReplacementJobEntity> PlantJobAsync(
        DateTime? libraryNoticeDueUtc,
        IReadOnlyList<(ReplacementNameKind Kind, string AppPath, ReplacementNameState State)> names,
        ReplacementJobState state = ReplacementJobState.Quarantined,
        string? reason = null,
        string? copyServicePath = null,
        string allowedRoot = MediaRoot)
    {
        var now = Now();
        var release = await _harness.FindAsync("Alien", 1979, "Alien (1979) BDRemux 2160p");
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var download = await new DownloadJobDataFactory(db, _factory.Time).Create(
            release.Id, _harness.Client.Id, _harness.Client.Name, _harness.Movies.Id, DownloadJobStatus.Downloaded, _harness.Owner.User.Id);
        var replacements = new ReplacementJobDataFactory(db, _factory.Time);
        var job = await replacements.Create(
            download.Id,
            _harness.Owner.User.Id,
            state,
            reason,
            quarantineCheckedUtc: now,
            quarantinedUtc: state == ReplacementJobState.Quarantined ? now : null,
            libraryNoticeDueUtc: libraryNoticeDueUtc);
        var videoPath = names[0].AppPath;
        var copy = await replacements.AddFile(
            job.Id, videoPath, TestMedia.File(inode: 1), servicePath: copyServicePath ?? "/data" + videoPath[MediaRoot.Length..]);
        for (var position = 0; position < names.Count; position++)
        {
            var (kind, appPath, nameState) = names[position];
            await replacements.AddName(
                job.Id,
                copy.Id,
                position,
                kind,
                appPath,
                $"{allowedRoot}/.mam-quarantine/{job.Id}/{appPath[(allowedRoot.Length + 1)..]}",
                allowedRoot,
                TestMedia.File(inode: position + 1, sizeBytes: kind == ReplacementNameKind.Video ? 3_891_719_630 : 84_211),
                nameState,
                problem: nameState == ReplacementNameState.Stayed ? HeldReason : null,
                movedUtc: nameState is ReplacementNameState.Quarantined or ReplacementNameState.Restored ? now : null,
                restoredUtc: nameState == ReplacementNameState.Restored ? now : null);
        }

        _harness.Mark();
        _jellyfinRequestsBefore = _factory.JellyfinHttp.Requests.Count;
        return job;
    }

    /// <summary>Jellyfin's other mount of a folder: its <paramref name="servicePrefix"/> is the app's
    /// <paramref name="appPrefix"/> too, and Фільми lists it as its second folder.</summary>
    private async Task AddJellyfinMountAsync(string servicePrefix, string appPrefix, CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db =>
        {
            db.PathMappings.Add(new PathMappingEntity
            {
                IntegrationConnectionId = _harness.Jellyfin.Id,
                Position = 1,
                ServicePrefix = servicePrefix,
                AppPrefix = appPrefix,
            });
            await db.SaveChangesAsync(cancellationToken);
        });
        await new LibraryLocationFolderDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>()).Create(_harness.Films.Id, servicePrefix, position: 1);
    }

    private Task SwitchOffJellyfinAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(row => row.Id == _harness.Jellyfin.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsEnabled, false), cancellationToken));

    /// <summary>The notice the replacement <paramref name="replacementJobId"/> owes, sent from a scope of its own.</summary>
    private async Task SendDueAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IReplacementLibraryNotice>().SendDueAsync(replacementJobId, cancellationToken);
    }

    private async Task<ReplacementLibraryAnswer> TellAsync(
        Guid integrationId, IReadOnlyList<string> libraryPaths, LibraryFileChange change, CancellationToken cancellationToken)
    {
        _jellyfinRequestsBefore = _factory.JellyfinHttp.Requests.Count;
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReplacementLibraryReader>().TellAsync(integrationId, libraryPaths, change, cancellationToken);
    }

    /// <summary>The notice's own Warnings so far.</summary>
    private List<RecordedLogEntry> Warnings() =>
        [.. _factory.Logs.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Category == typeof(ReplacementLibraryNotice).FullName)];
}
