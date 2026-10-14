using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0031 — the owner's Retry (POST /api/replacements/{id}/retry) of a replacement the mover stopped —
// a torrent holding the old copy, a rename refused, a move, restore or deletion cut —: back to Verified with its reason
// gone and the owner's ReplacementRetried row, so the mover's next pass checks the copies again; nothing is written to the
// media and no client or library is asked. MAM-27, ADR 0035 — and of a replacement the check of its new files stopped: back
// to Downloaded with its reason gone and the owner's ReplacementCheckRetried row, the last check's evidence forgotten at
// once — its new files and their ids, every undropped copy's pair, problem and drop-only mark; a dropped copy keeps its
// own as history —, so the verifier's next pass starts the check again from the files and asks the library again. Refused
// with 409 and nothing written: a replacement not waiting for review (Failed included), one whose download the app can't
// follow — a review its following stopped, or one whose download is no longer done and checked —, one with an undropped
// copy only a drop settles (named, the first by its place; Retry goes through once it is dropped), and one that moved on
// between the read and the write. One request at a time per job; a bare 404 for another user's. The pass after a Retry of
// a job with nothing left to rename asks no client and settles it where it rests: Quarantined while the quarantine holds
// something of it, Completed when its videos were deleted from it for good.
public sealed class ReplacementRetryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string ExtendedPath = "/media/movies/Alien (1979)/Alien (1979) Extended.mkv";
    private const string WorkprintPath = "/media/movies/Alien (1979)/Alien (1979) Workprint.mkv";
    private const string NewUhdName = "Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewHdName = "Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv";
    private const string NewUhdPath = "/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewHdPath = "/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv";
    private const string EpisodeFivePath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05.mkv";
    private const string EpisodeSixPath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E06.mkv";
    private const string AlienImdbId = "tt0078748";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string NothingToRetryError = "This replacement isn't waiting for review, so there's nothing to retry here.";
    private const string DownloadNotFollowedError =
        "The app can't follow this replacement's download, so its new file can't be checked again: keep both copies, or cancel the job.";
    private const string EpisodeFiveDropOnlyError =
        "“Yellowstone S02E05.mkv” can only be dropped from this replacement, or both copies kept: checking again wouldn't change it.";
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";
    private const string ChangedMeanwhileError =
        "This replacement changed while the app was working on it, so it stopped. Reload the page to see where it stands.";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string CheckReason = "“qBittorrent” no longer has this download, so the app can't confirm its files are complete.";
    private const string NeedsLinkingReason =
        "“qBittorrent”'s downloads couldn't be told apart for this release, so the app can't follow it to verify the new file.";
    private const string TwoNewFilesReason =
        "Two new files are Alien (1979): “/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv” and "
        + "“/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv”; the app can't tell which one replaces the old copy.";
    private const string TooShortProblem =
        "“/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv” runs 1:06:40, less than three quarters of the old copy's 1:33:24.";
    private const string RenumberedProblem =
        "“Living room” now numbers the old copy “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05.mkv” season 2, episode 7, "
        + "not season 2, episode 5 as when it was scheduled.";
    private const string EpisodeSixRenumberedProblem =
        "“Living room” now numbers the old copy “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E06.mkv” season 2, episode 8, "
        + "not season 2, episode 6 as when it was scheduled.";
    private const string FailedReason = "The download failed, so nothing will be replaced.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementRetryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every state but a review: none is retried, a state added later included.</summary>
    public static TheoryData<ReplacementJobState> NotWaitingForReview =>
        [.. Enum.GetValues<ReplacementJobState>().Where(state => state != ReplacementJobState.NeedsReview)];

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the mover's stop ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_of_a_replacement_the_mover_stopped_puts_it_back_in_its_queue_and_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var checkedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        // Check 6 stopped it before anything moved: no name row was written.
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], quarantineCheckedUtc: checkedUtc, reason: HeldReason);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.Verified, (string?)null, false, planted.Replacement.Id), (line.State, line.Reason, line.CanRetry, line.Id));
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        // The mark of the mover's stage stays: it says when the mover last looked, and the next stop sets it again.
        Assert.Equal(
            (ReplacementJobState.Verified, (string?)null, now, (DateTime?)checkedUtc),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc, replacement.QuarantineCheckedUtc));
        // MAM-27 — every row aimed at the replacement, not a list of actions: the mover's Retry writes its own action alone.
        Assert.Equal(
            [(AuditAction.ReplacementRetried, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, planted.Replacement.Id, ReplacementQuarantinePlanter.ReleaseTitle)],
            await _harness.ReplacementAuditsAsync(cancellationToken));
        // Retry writes nothing to the media and asks no one: the mover's next pass does the checking.
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Equal(sentBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(planted.Replacement.Id));
    }

    [Fact]
    public async Task Retry_of_a_replacement_stopped_part_way_keeps_its_names_where_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: "The app stopped while moving the old copies: 1 of 1 name is in the quarantine.");

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReplacementJobState.Verified, (await JobAsync(planted.Replacement.Id, cancellationToken)).State);
        await _factory.WithDbAsync(async db => Assert.Equal(
            ReplacementNameState.Quarantined,
            (await db.ReplacementJobNames.AsNoTracking().SingleAsync(cancellationToken)).State));
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Retry_of_a_replacement_whose_every_name_is_in_the_quarantine_settles_it_quarantined_on_the_next_pass_without_asking_a_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Stopped after its last rename — cut there by a restart, say —, every name already in the quarantine.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 98_304)),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: "The app stopped while moving the old copies: 2 of 2 names are in the quarantine.");
        await RetryAsync(planted.Replacement.Id);
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count);

        //Act
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        // Nothing left to rename: no torrent can hold it, so no client is asked — one that is down can't stop the job — and
        // the library heard of these names when they left.
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (replacement.State, replacement.Reason));
        Assert.Equal(sentBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count));
        Assert.Empty(_factory.Quarantine.Renames);
        await _factory.WithDbAsync(async db => Assert.All(
            await db.ReplacementJobNames.AsNoTracking().ToListAsync(cancellationToken),
            name => Assert.Equal(ReplacementNameState.Quarantined, name.State)));
        Assert.Equal(
            [AuditAction.ReplacementRetried, AuditAction.ReplacementQuarantineStarted, AuditAction.ReplacementQuarantined],
            (await _harness.QuarantineAuditsAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Retry_of_a_replacement_whose_every_video_was_deleted_for_good_ends_it_completed_on_the_next_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Every name deleted for good and the job still in review, with only Retry offered — what a deletion cut after its
        // last name looked like before its recovery ended such a cut Completed, planted so the settle after a Retry is
        // pinned for it too: nothing in the quarantine, nothing to move.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), ReplacementNameState.Deleted),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 98_304), ReplacementNameState.Deleted),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: "The app stopped while deleting the old copies for good: 2 of 2 names are deleted.");
        await RetryAsync(planted.Replacement.Id);
        var qbittorrentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await _factory.QuarantineReplacementsAsync(cancellationToken);

        //Assert
        // It ends where its Delete for good would have: Completed, the app its actor, nothing offered any more.
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Completed, (string?)null, (DateTime?)null), (replacement.State, replacement.Reason, replacement.LibraryNoticeDueUtc));
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;
        Assert.Equal((ReplacementJobState.Completed, 0, false), (line.State, line.QuarantinedCopyCount, line.CanRetry));
        Assert.Equal(qbittorrentBefore, _factory.QBittorrentHttp.Requests.Count);
        Assert.Equal(
            [
                (AuditAction.ReplacementRetried, (Guid?)_harness.Owner.User.Id),
                (AuditAction.ReplacementQuarantineStarted, null),
                (AuditAction.ReplacementCompleted, null),
            ],
            (await _harness.QuarantineAuditsAsync(cancellationToken)).Select(audit => (audit.Action, audit.ActorUserId)));
    }

    // ── the check's stop ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_of_a_replacement_the_check_of_its_new_files_stopped_sends_it_back_to_the_check_and_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The check started two hours ago and stopped when the library's hour ran out; the owner dropped a copy since.
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime.AddHours(-2);
        var askedUtc = startedUtc.AddMinutes(1);
        var droppedUtc = askedUtc.AddMinutes(70);
        // Two new files of Alien in the pack, so the theatrical cut couldn't be paired; the extended cut paired with the
        // first; the workprint, too short for the second, was dropped since.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: TwoNewFilesReason,
            copies:
            [
                new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1), Problem: TwoNewFilesReason),
                new PlantedCopy(ExtendedPath, TestMedia.File(inode: 2), PairedWithNewFile: 0),
                new PlantedCopy(WorkprintPath, TestMedia.File(inode: 3), Problem: TooShortProblem, OnlyDropSettles: true, DroppedUtc: droppedUtc, PairedWithNewFile: 1),
            ],
            newFiles: [IdentifiedNewFile(NewUhdName, NewUhdPath, inode: 7, askedUtc), IdentifiedNewFile(NewHdName, NewHdPath, inode: 8, askedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: askedUtc);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.Downloaded, (string?)null, planted.Replacement.Id), (line.State, line.Reason, line.Id));
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        // Downloaded, as a finished download's replacement waits for the check: the verifier's next pass claims it afresh.
        Assert.Equal(
            (ReplacementJobState.Downloaded, (string?)null, now, (DateTime?)null),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc, replacement.QuarantineCheckedUtc));
        // The last check's evidence is gone at once: no new file, no id, no pair or problem on a copy still in the job; the
        // dropped workprint keeps its own as its history.
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ReplacementJobNewFiles.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobNewFileIdentifiers.CountAsync(cancellationToken));
        });
        Assert.Equal(
            [
                (TheatricalPath, (Guid?)null, (string?)null, false, (DateTime?)null),
                (ExtendedPath, null, null, false, null),
                (WorkprintPath, planted.NewFiles[1].Id, TooShortProblem, true, droppedUtc),
            ],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.AppPath, copy.PairedNewFileId, copy.Problem, copy.OnlyDropSettles, copy.DroppedUtc)));
        // Its own action, not the mover's ReplacementRetried.
        Assert.Equal(
            [(AuditAction.ReplacementCheckRetried, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, planted.Replacement.Id, ReplacementQuarantinePlanter.ReleaseTitle)],
            await _harness.ReplacementAuditsAsync(cancellationToken));
        // The request itself writes nothing to the media and asks no one: the verifier's next pass reads the client and
        // the disk and asks the library to look again.
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Equal(sentBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(planted.Replacement.Id));
    }

    [Fact]
    public async Task Retry_of_a_check_stop_with_copies_only_a_drop_settles_is_409_naming_the_first_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin numbers both old episodes otherwise than when they were ticked: no fix there pairs them again.
        var planted = await PlantCheckStopAsync(
            RenumberedProblem,
            [
                new PlantedCopy(EpisodeFivePath, TestMedia.File(inode: 21), Problem: RenumberedProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 5),
                new PlantedCopy(EpisodeSixPath, TestMedia.File(inode: 22), Problem: EpisodeSixRenumberedProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 6),
            ]);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(EpisodeFiveDropOnlyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Retry_of_a_check_stop_once_its_copy_only_a_drop_settles_is_dropped_sends_it_back_to_the_check()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episode 5 is numbered elsewhere now; episode 6 paired with its new file.
        var planted = await PlantCheckStopAsync(
            RenumberedProblem,
            [
                new PlantedCopy(EpisodeFivePath, TestMedia.File(inode: 21), Problem: RenumberedProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 5),
                new PlantedCopy(EpisodeSixPath, TestMedia.File(inode: 22), PairedWithNewFile: 0, SeasonNumber: 2, EpisodeNumber: 6),
            ]);
        var refused = await RetryAsync(planted.Replacement.Id);
        var dropped = await _factory.PostHttpRequest(
            $"{BaseUrl}/{planted.Replacement.Id}/copies/{planted.Copies[0].Id}/drop", _harness.Owner.AccessToken);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(
            (HttpStatusCode.Conflict, (string?)EpisodeFiveDropOnlyError, HttpStatusCode.OK),
            (refused.StatusCode, await refused.ReadErrorAsync(cancellationToken), dropped.StatusCode));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReplacementJobState.Downloaded, (await JobAsync(planted.Replacement.Id, cancellationToken)).State);
        Assert.Equal(
            [
                (
                    AuditAction.ReplacementCopyDropped,
                    (Guid?)_harness.Owner.User.Id,
                    (AuditTargetKind?)AuditTargetKind.ReplacementJobFile,
                    (Guid?)planted.Copies[0].Id,
                    (string?)"Yellowstone S02E05.mkv"),
                (AuditAction.ReplacementCheckRetried, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, planted.Replacement.Id, ReplacementQuarantinePlanter.ReleaseTitle),
            ],
            await _harness.ReplacementAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Retry_of_a_replacement_whose_download_the_app_cant_follow_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The following stopped it before any check started: neither the check's nor the mover's mark is set.
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: NeedsLinkingReason);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DownloadNotFollowedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Retry_of_a_check_stop_whose_download_is_no_longer_done_and_checked_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantCheckStopAsync(CheckReason, [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        // A hand-edited row: the verifier reads no download that isn't done and through the strict check.
        await _factory.WithDbAsync(db => db.DownloadJobs
            .Where(download => download.Id == planted.Download.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(download => download.OverwriteCheck, DownloadOverwriteCheck.NotCheckedMagnet), cancellationToken));
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DownloadNotFollowedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    // ── refused ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotWaitingForReview))]
    public async Task Retry_of_a_replacement_not_waiting_for_review_is_409_and_writes_nothing(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Marked as both stages' — the check's and the mover's —, so only the state refuses it.
        var planted = await _planter.PlantAsync(
            state, [], quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime, inspectionStartedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NothingToRetryError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Retry_of_a_replacement_someone_is_working_on_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime, reason: HeldReason);
        var before = await SnapshotAsync(cancellationToken);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        var taken = inHand.TryTake(planted.Replacement.Id);

        //Act
        HttpResponseMessage response;
        try
        {
            response = await RetryAsync(planted.Replacement.Id);
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
    public async Task Retry_of_a_check_stop_that_ended_between_the_read_and_the_write_is_409_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantCheckStopAsync(CheckReason, [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1), PairedWithNewFile: 0)]);
        var id = planted.Replacement.Id;
        // The follower ends the review — its download failed — once the Retry passed its checks, just before it writes.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.ReplacementJobs
                .Where(job => job.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.State, ReplacementJobState.Failed).SetProperty(job => job.Reason, FailedReason),
                    CancellationToken.None)));

        //Act
        var response = await RetryAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChangedMeanwhileError, await response.ReadErrorAsync(cancellationToken));
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.Failed, (string?)FailedReason), (replacement.State, replacement.Reason));
        // The evidence of the check that stopped stays with the failed job; nothing is recorded for a Retry that wrote nothing.
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.ReplacementJobNewFiles.CountAsync(cancellationToken)));
        Assert.Empty(await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    [Fact]
    public async Task Retry_of_a_move_stop_that_ended_between_the_read_and_the_write_is_409_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), ReplacementNameState.Stayed, HeldReason),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: HeldReason);
        var id = planted.Replacement.Id;
        // The follower ends the review — its download failed — once the Retry passed its checks, just before it writes: never
        // back to the mover's queue.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.ReplacementJobs
                .Where(job => job.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.State, ReplacementJobState.Failed).SetProperty(job => job.Reason, FailedReason),
                    CancellationToken.None)));

        //Act
        var response = await RetryAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChangedMeanwhileError, await response.ReadErrorAsync(cancellationToken));
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.Failed, (string?)FailedReason), (replacement.State, replacement.Reason));
        Assert.Empty(await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    // ── whose ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Retry_of_an_unknown_or_another_users_replacement_is_a_bare_404_and_writes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        // A stop of the check, which the owner's own Retry would send back: only whose it is refuses it.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: CheckReason,
            requestedByUserId: member.User.Id,
            copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))],
            inspectionStartedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await RetryAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> RetryAsync(Guid replacementJobId) =>
        _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/retry", _harness.Owner.AccessToken);

    /// <summary>
    /// A replacement the check of its new files stopped for review with <paramref name="reason"/>: started and the library
    /// asked, never taken up by the mover, holding <paramref name="copies"/> and one new file Jellyfin identified as
    /// Alien.
    /// </summary>
    private Task<PlantedReplacement> PlantCheckStopAsync(string reason, IReadOnlyList<PlantedCopy> copies)
    {
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        return _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: reason,
            copies: copies,
            newFiles: [IdentifiedNewFile(NewUhdName, NewUhdPath, inode: 7, startedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
    }

    /// <summary>A new file at <paramref name="appPath"/>, downloaded, read on the disk and identified by Jellyfin as Alien by
    /// its IMDb id at <paramref name="readUtc"/>.</summary>
    private static ReplacementNewFileFacts IdentifiedNewFile(string relativePath, string appPath, long inode, DateTime readUtc) => new(
        relativePath,
        ClientSizeBytes: 3_891_719_630,
        ClientProgress: 1.0,
        AppPath: appPath,
        Disk: TestMedia.File(inode),
        DurationMilliseconds: 7_980_000,
        VideoCodec: "hevc",
        Width: 3840,
        Height: 2160,
        CheckedUtc: readUtc,
        LibraryItemId: $"item-{inode}",
        Kind: LibraryFileKind.Movie,
        Refreshed: true,
        ReadUtc: readUtc,
        MediaWorkId: Guid.NewGuid(),
        Identifiers: [(MediaIdentifierScheme.Imdb, AlienImdbId, false)]);

    private async Task<ReplacementJobEntity> JobAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity job = null!;
        await _factory.WithDbAsync(async db => job = await db.ReplacementJobs.AsNoTracking().SingleAsync(row => row.Id == replacementJobId, cancellationToken));
        return job;
    }

    private Task<ReplacementRowsSnapshot> SnapshotAsync(CancellationToken cancellationToken) => ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);

    /// <summary>
    /// That every replacement row, download job and audit row is as <paramref name="before"/> holds them, nothing was
    /// renamed, and no one holds the job: a refused or unaddressed Retry writes nothing.
    /// </summary>
    private async Task AssertNothingWrittenAsync(ReplacementRowsSnapshot before, CancellationToken cancellationToken)
    {
        Assert.Equal(before, await SnapshotAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
    }
}
