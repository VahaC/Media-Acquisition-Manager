using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-27, ADR 0014, ADR 0030 — the owner's Keep both (POST /api/replacements/{id}/cancel): it cancels only the replacement,
// in every state before an old copy starts to move — following its download, being checked, verified, or waiting for
// review while none of its names left its folder —: Cancelled with "You cancelled the replacement…", the owner's
// ReplacementCancelledAlone row and nothing else — its download job untouched, no DownloadCancelled row, nothing sent to
// qBittorrent or Jellyfin, nothing renamed —, its names that never left their folders forgotten and the ones put back or
// lost kept. The download goes on afterwards and no check or move takes the replacement up; its copies can be ticked again;
// a later Cancel job writes no second replacement row; a Keep both landing inside a check's pass wins over it. It works while
// replacement is switched off. Refused with 409 and nothing written: an ended replacement, one part of which left its
// folders (Restore puts that back), one being moved, restored or deleted, one someone is working on — a second click
// included —, and one the follower ended meanwhile. A bare 404 for another user's or a missing one.
public sealed class ReplacementCancelHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string DownloadsUrl = "/api/downloads";
    private const string OptionsUrl = "/api/replacements/options";
    private const string AlienImdbId = "tt0078748";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string ExtendedPath = "/media/movies/Alien (1979)/Alien (1979) Extended.mkv";
    private const string WorkprintPath = "/media/movies/Alien (1979)/Alien (1979) Workprint.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string KeptBothReason = "You cancelled the replacement, so nothing will be replaced.";
    private const string AlreadyEndedError = "This replacement has already ended, so there's nothing to cancel.";
    private const string LeftFoldersError =
        "Part of this replacement has left its folders already, so it can't be cancelled: Restore puts back what is in the quarantine.";
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string FailedReason = "The download failed, so nothing will be replaced.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementCancelHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    // The states before any old copy starts to move, spelled out rather than read from product code: Keep both applies in each.
    private static readonly ReplacementJobState[] BeforeMoveStates =
    [
        ReplacementJobState.Scheduled,
        ReplacementJobState.Submitted,
        ReplacementJobState.Downloading,
        ReplacementJobState.Paused,
        ReplacementJobState.Downloaded,
        ReplacementJobState.Inspecting,
        ReplacementJobState.Verified,
        ReplacementJobState.NeedsReview,
    ];

    /// <summary>Every state Keep both applies in.</summary>
    public static TheoryData<ReplacementJobState> BeforeMove => [.. BeforeMoveStates];

    /// <summary>Every other state, with the sentence that refuses it: a state added later fails here until it has one.</summary>
    public static TheoryData<ReplacementJobState, string> NotBeforeMove
    {
        get
        {
            var data = new TheoryData<ReplacementJobState, string>();
            foreach (var state in Enum.GetValues<ReplacementJobState>().Except(BeforeMoveStates))
            {
                data.Add(state, state switch
                {
                    ReplacementJobState.Failed or ReplacementJobState.Cancelled or ReplacementJobState.Completed or ReplacementJobState.Restored => AlreadyEndedError,
                    ReplacementJobState.Quarantined => LeftFoldersError,
                    ReplacementJobState.Quarantining or ReplacementJobState.Restoring or ReplacementJobState.Deleting => BeingWorkedOnError,
                    _ => throw new InvalidOperationException($"No sentence is spelled out for a Keep both of a {state} replacement."),
                });
            }

            return data;
        }
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── kept both ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BeforeMove))]
    public async Task Cancel_of_a_replacement_before_any_copy_moved_ends_it_alone_and_is_200(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(state, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var before = await SnapshotAsync(cancellationToken);
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count);

        //Act
        var response = await CancelAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(
            (planted.Download.Id, planted.Download.Status, ReplacementJobState.Cancelled, (string?)KeptBothReason, planted.Replacement.Id),
            (job.Id, job.Status, job.Replacement!.State, job.Replacement.Reason, job.Replacement.Id));
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Cancelled, (string?)KeptBothReason, now), (replacement.State, replacement.Reason, replacement.StateChangedUtc));
        // Only the replacement's row: the download job and the ticked copies are as they were.
        var after = await SnapshotAsync(cancellationToken);
        Assert.Equal((before.Downloads, before.Copies, before.NewFiles), (after.Downloads, after.Copies, after.NewFiles));
        // One row, the owner's, of its own action — no DownloadCancelled beside it.
        Assert.Equal(
            [(AuditAction.ReplacementCancelledAlone, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, planted.Replacement.Id, ReplacementQuarantinePlanter.ReleaseTitle)],
            await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.Equal(before.AuditEvents + 1, after.AuditEvents);
        // Nothing sent to qBittorrent or Jellyfin, nothing renamed.
        Assert.Equal(sentBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(planted.Replacement.Id));
    }

    [Fact]
    public async Task Cancel_of_a_review_whose_names_never_left_their_folders_forgets_the_planned_and_stayed_ones_and_keeps_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The mover stopped it with the theatrical cut and its subtitle still where they were; the extended cut's name was
        // put back, and the workprint's was found nowhere.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), ReplacementNameState.Planned),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 98_304), ReplacementNameState.Stayed, HeldReason),
                new PlantedName(ReplacementNameKind.Video, ExtendedPath, TestMedia.File(inode: 3), ReplacementNameState.Restored),
                new PlantedName(ReplacementNameKind.Video, WorkprintPath, TestMedia.File(inode: 4), ReplacementNameState.Lost),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: HeldReason);

        //Act
        var response = await CancelAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReplacementJobState.Cancelled, (await JobAsync(planted.Replacement.Id, cancellationToken)).State);
        // A planned or stayed name would read "waits to move" on an ended job; one put back or lost says what became of it.
        Assert.Equal(
            [(ExtendedPath, ReplacementNameState.Restored), (WorkprintPath, ReplacementNameState.Lost)],
            (await NamesAsync(planted.Replacement.Id, cancellationToken)).Select(name => (name.AppPath, name.State)));
        Assert.Single(await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Cancel_while_replacement_is_switched_off_ends_it_and_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Verified, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

        //Act
        var response = await CancelAsync(planted.Replacement.Id);

        //Assert
        // ADR 0033 — the switch guards writes to the media; Keep both writes none.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Cancelled, (string?)KeptBothReason), (replacement.State, replacement.Reason));
    }

    // ── afterwards ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_of_a_downloading_replacement_lets_the_download_go_on_and_no_check_or_move_takes_it_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        _harness.QBittorrent.UpdateTorrent(job.ClientJobId!, torrent => torrent with { State = "downloading", Progress = 0.4 });
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PollDownloadJobsAsync(cancellationToken);
        var downloading = (await _harness.ReplacementAsync(cancellationToken)).State;
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        _harness.Mark();

        //Act
        var response = await CancelAsync((await _harness.ReplacementAsync(cancellationToken)).Id);
        _harness.QBittorrent.UpdateTorrent(
            job.ClientJobId!, torrent => torrent with { State = "stalledUP", Progress = 1.0, Files = [new FakeQBittorrentFile(NewName, NewSize, 1.0)] });
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PollDownloadJobsAsync(cancellationToken);
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.Downloading, HttpStatusCode.OK), (downloading, response.StatusCode));
        // The download went on to the end; the replacement stayed where Keep both left it.
        var done = (await _harness.LineAsync(job.Id, cancellationToken))!;
        Assert.Equal((ReplacementJobState.Cancelled, (string?)KeptBothReason), (done.State, done.Reason));
        await _factory.WithDbAsync(async db => Assert.Equal(
            DownloadJobStatus.Downloaded,
            (await db.DownloadJobs.AsNoTracking().SingleAsync(row => row.Id == job.Id, cancellationToken)).Status));
        // After Keep both nothing more about it: no check started, no move.
        Assert.Equal(
            [AuditAction.ReplacementCancelledAlone],
            (await _harness.ReplacementAuditsAsync(cancellationToken)).Select(audit => audit.Action).SkipWhile(action => action != AuditAction.ReplacementCancelledAlone));
        // Only reads since: nothing sent to qBittorrent, Jellyfin never asked to look, nothing renamed.
        Assert.Empty(_harness.Writes());
        Assert.Empty(_harness.JellyfinServer.ScanRequests);
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Cancel_frees_its_copies_for_another_download()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        var held = await TheatricalUnavailableAsync(job.ReleaseId, cancellationToken);

        //Act
        var response = await CancelAsync((await _harness.ReplacementAsync(cancellationToken)).Id);
        var freed = await TheatricalUnavailableAsync(job.ReleaseId, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A cancelled replacement holds nothing: the Download dialog offers the theatrical cut again.
        Assert.Equal((ReplacementCopyUnavailableReason?)ReplacementCopyUnavailableReason.AlreadyScheduled, held);
        Assert.Null(freed);
    }

    [Fact]
    public async Task Cancel_job_after_keep_both_cancels_the_download_and_writes_no_second_replacement_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        var replacementId = (await _harness.ReplacementAsync(cancellationToken)).Id;
        await CancelAsync(replacementId);
        var keptBoth = await JobAsync(replacementId, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest($"{DownloadsUrl}/{job.Id}/cancel", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DownloadJobStatus.Cancelled, (await ReadJobAsync(response, cancellationToken)).Status);
        // MAM-24's cancel ends only a replacement still followed: Keep both's stays as it was written.
        var replacement = await JobAsync(replacementId, cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Cancelled, (string?)KeptBothReason, keptBoth.StateChangedUtc),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.ReplacementCancelledAlone, cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.ReplacementCancelled, cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.DownloadCancelled, cancellationToken));
        });
    }

    [Fact]
    public async Task Cancel_landing_while_a_check_pass_decides_wins_and_the_pass_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        var release = await _harness.FindAsync("Alien", 1979, ReplacementQuarantinePlanter.ReleaseTitle, imdbId: AlienImdbId);
        await _harness.DownloadedAsync(release, _harness.Movies, [TheatricalPath], [new FakeQBittorrentFile(NewName, NewSize, 1.0)], cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        // The first pass checks the files and asks Jellyfin to look; the second would verify it.
        await _harness.VerifyAsync(cancellationToken);
        var replacementId = (await _harness.ReplacementAsync(cancellationToken)).Id;
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        HttpResponseMessage? keptBoth = null;
        // Keep both arrives once the pass has read Jellyfin and paired, just before it writes its decision.
        _factory.TransactionHook.BeforeNextTransaction(_ => true, async _ => keptBoth = await CancelAsync(replacementId));

        //Act
        await _harness.VerifyAsync(cancellationToken);
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, keptBoth?.StatusCode);
        var replacement = await JobAsync(replacementId, cancellationToken);
        Assert.Equal((ReplacementJobState.Cancelled, (string?)KeptBothReason), (replacement.State, replacement.Reason));
        // The pass's decision and its evidence went with its write: no Verified row, no pair; and no move after.
        Assert.Equal(
            [AuditAction.ReplacementCheckStarted, AuditAction.ReplacementCancelledAlone],
            (await _harness.ReplacementAuditsAsync(cancellationToken)).Select(audit => audit.Action).Where(action => action != AuditAction.ReplacementScheduled));
        Assert.Null(Assert.Single(await _harness.CopiesAsync(cancellationToken)).PairedNewFileId);
        Assert.Empty(_factory.Quarantine.Renames);
    }

    // ── refused ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotBeforeMove))]
    public async Task Cancel_of_a_replacement_that_ended_or_whose_copies_are_moving_is_409_and_writes_nothing(ReplacementJobState state, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            state, [], quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime, copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await CancelAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Theory]
    [InlineData(ReplacementNameState.Moving)]
    [InlineData(ReplacementNameState.Quarantined)]
    [InlineData(ReplacementNameState.Restoring)]
    [InlineData(ReplacementNameState.Deleting)]
    [InlineData(ReplacementNameState.Deleted)]
    public async Task Cancel_of_a_review_holding_a_name_that_left_its_folder_is_409_and_writes_nothing(ReplacementNameState nameState)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Stopped part-way by the mover: the theatrical cut's name is out of its folder, the extended cut's still in it.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), nameState),
                new PlantedName(ReplacementNameKind.Video, ExtendedPath, TestMedia.File(inode: 3), ReplacementNameState.Planned),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: HeldReason);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await CancelAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LeftFoldersError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Cancel_of_a_replacement_someone_is_working_on_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Verified, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var before = await SnapshotAsync(cancellationToken);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        // The mover holds it between its checks and its claim.
        var taken = inHand.TryTake(planted.Replacement.Id);

        //Act
        HttpResponseMessage response;
        try
        {
            response = await CancelAsync(planted.Replacement.Id);
        }
        finally
        {
            inHand.Release(planted.Replacement.Id);
        }

        //Assert
        Assert.True(taken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BeingWorkedOnError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Cancel_clicked_twice_at_once_cancels_once_and_answers_one_200_and_one_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Verified, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var id = planted.Replacement.Id;
        HttpResponseMessage? second = null;
        // The second click arrives once the first passed its checks and is about to write.
        _factory.TransactionHook.BeforeNextTransaction(_ => true, async _ => second = await CancelAsync(id));

        //Act
        var first = await CancelAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second?.StatusCode);
        Assert.Equal(BeingWorkedOnError, await second!.ReadErrorAsync(cancellationToken));
        Assert.Equal([AuditAction.ReplacementCancelledAlone], (await _harness.ReplacementAuditsAsync(cancellationToken)).Select(audit => audit.Action));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    [Fact]
    public async Task Cancel_of_a_replacement_the_follower_ended_between_the_read_and_the_write_is_409_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Downloading, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var id = planted.Replacement.Id;
        // Its download failed: the follower ends the replacement once Keep both passed its checks, just before it writes.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.ReplacementJobs
                .Where(job => job.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.State, ReplacementJobState.Failed).SetProperty(job => job.Reason, FailedReason),
                    CancellationToken.None)));

        //Act
        var response = await CancelAsync(id);

        //Assert
        // The write found it ended, and the answer says so as a fresh read would.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadyEndedError, await response.ReadErrorAsync(cancellationToken));
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.Failed, (string?)FailedReason), (replacement.State, replacement.Reason));
        Assert.Empty(await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    [Fact]
    public async Task Cancel_of_a_replacement_the_follower_moved_on_between_the_read_and_the_write_cancels_it_and_logs_the_state_it_left()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Downloading, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var id = planted.Replacement.Id;
        // Its download finished: the follower moves it on once Keep both passed its checks, just before it writes — to a
        // state Keep both still applies in.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.ReplacementJobs
                .Where(job => job.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.State, ReplacementJobState.Downloaded), CancellationToken.None)));

        //Act
        var response = await CancelAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.Cancelled, (string?)KeptBothReason), (replacement.State, replacement.Reason));
        Assert.Equal([AuditAction.ReplacementCancelledAlone], (await _harness.ReplacementAuditsAsync(cancellationToken)).Select(audit => audit.Action));
        // The line names the state the write found, never the one the request read before the follower moved it on.
        Assert.Equal(
            [$"Replacement job {id} went from Downloaded to Cancelled by its owner's Keep both; its download is left as it is."],
            _factory.Logs.Entries
                .Where(entry => entry.Category == typeof(ReplacementJobActions).FullName && entry.Message.Contains("Keep both", StringComparison.Ordinal))
                .Select(entry => entry.Message));
    }

    // ── whose ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Cancel_of_an_unknown_or_another_users_replacement_is_a_bare_404_and_writes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        // ADR 0014 — a job is its sender's; this one is the member's, planted as a hand-edited row would leave it, and Keep
        // both would apply to it.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Verified, [], requestedByUserId: member.User.Id, copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await CancelAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> CancelAsync(Guid replacementJobId) =>
        _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/cancel", _harness.Owner.AccessToken);

    /// <summary>Alien's theatrical cut in the ticked Фільми, inspected, so the Download dialog can tick it.</summary>
    private async Task PlantAlienAsync(CancellationToken cancellationToken)
    {
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        await _harness.InspectAsync(cancellationToken);
    }

    /// <summary>
    /// The owner schedules the replacement of Alien's theatrical cut by a release of its director's cut through the real
    /// route; qBittorrent took the torrent and nothing was followed yet: the replacement is Scheduled, its download
    /// Submitted.
    /// </summary>
    private async Task<DownloadJobDto> ScheduleAsync(CancellationToken cancellationToken)
    {
        await PlantAlienAsync(cancellationToken);
        var release = await _harness.FindAsync("Alien", 1979, ReplacementQuarantinePlanter.ReleaseTitle, imdbId: AlienImdbId);
        _harness.Feed.TorrentFile = TestTorrent.V1(NewName, TestTorrent.Bytes(NewSize, seed: 1));
        var fileId = await _harness.FileIdAsync(TheatricalPath, cancellationToken);
        using var response = await _factory.PostHttpRequest(
            BaseUrl, _harness.Owner.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _harness.Client.Id, _harness.Movies.Id, [fileId]));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The schedule answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await ReadJobAsync(response, cancellationToken);
    }

    /// <summary>Why the Download dialog of <paramref name="releaseId"/> can't tick the theatrical cut now; <c>null</c> when it
    /// can.</summary>
    private async Task<ReplacementCopyUnavailableReason?> TheatricalUnavailableAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest(
            $"{OptionsUrl}?releaseId={releaseId}&destinationId={_harness.Movies.Id}", _harness.Owner.AccessToken);
        var options = (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        return options.Works.SelectMany(work => work.Copies).Single(copy => copy.File.AppPath == TheatricalPath).Unavailable;
    }

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task<ReplacementJobEntity> JobAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity job = null!;
        await _factory.WithDbAsync(async db => job = await db.ReplacementJobs.AsNoTracking().SingleAsync(row => row.Id == replacementJobId, cancellationToken));
        return job;
    }

    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> names = [];
        await _factory.WithDbAsync(async db => names = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken));
        return names;
    }

    private Task<ReplacementRowsSnapshot> SnapshotAsync(CancellationToken cancellationToken) => ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);

    /// <summary>That every replacement row, download job and audit row is as <paramref name="before"/> holds them, and nothing
    /// was renamed or sent to qBittorrent.</summary>
    private async Task AssertNothingWrittenAsync(ReplacementRowsSnapshot before, CancellationToken cancellationToken)
    {
        Assert.Equal(before, await SnapshotAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_harness.Writes());
    }
}
