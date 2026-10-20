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
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-27, ADR 0032 — the owner's Drop of one ticked old copy (POST /api/replacements/{id}/copies/{copyId}/drop) from a
// replacement that stopped for review at the check of its new files or by the mover (Vasyl, 2026-10-11): the copy is
// marked dropped and stays where it is, its problem and drop-only mark kept as its history, its names that never left their
// folders forgotten and every other name — its own put back or lost, and the other copies' — kept; the job's state, reason
// and evidence stay as they were — nothing more happens until the owner presses Retry —, with the owner's
// ReplacementCopyDropped row aimed at the copy itself; nothing is sent to qBittorrent or Jellyfin and nothing is renamed.
// Refused with 409 and nothing written: a replacement that didn't stop for review (every other state), one whose download
// the app can't follow, a copy dropped already, one with a name that left its folder (Restore puts it back), the last copy
// left (keep both instead), a job someone is working on — a second drop at once included —, a drop whose other copy was
// dropped meanwhile, and one whose replacement the follower ended meanwhile. A bare 404 for a copy of another job, a
// made-up copy, and an unknown or another user's replacement. The request that scheduled the job still replays after a
// drop.
public sealed class ReplacementDropCopyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string YellowstoneImdbId = "tt4236770";
    private const string YellowstoneTmdbTvId = "73586";
    private const string Release = "Yellowstone (S2) (2019) WEB-DL 1080p";
    private const string PackFolder = "Yellowstone.S02.1080p.WEB-DL";
    private const string EpisodeFivePath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05.mkv";
    private const string EpisodeFiveSubtitlePath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05.uk.srt";
    private const string EpisodeFiveNfoPath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05.nfo";
    private const string EpisodeSixPath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E06.mkv";
    private const string EpisodeSixSubtitlePath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E06.uk.srt";
    private const string EpisodeSevenPath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E07.mkv";
    private const string EpisodeSevenSubtitlePath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E07.uk.srt";
    private const string NewEpisodeSixName = "Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E06.1080p.WEB-DL.mkv";
    private const string NewEpisodeSixPath = "/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E06.1080p.WEB-DL.mkv";
    private const string EpisodeFiveName = "Yellowstone S02E05.mkv";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string NotStoppedForReviewError = "A copy can be dropped only from a replacement that stopped for review.";
    private const string DownloadNotFollowedError =
        "The app can't follow this replacement's download, so dropping a copy wouldn't change anything: keep both copies, or cancel the job.";
    private const string AlreadyDroppedError = "“Yellowstone S02E05.mkv” is dropped from this replacement already.";
    private const string LeftItsFolderError = "“Yellowstone S02E05.mkv” has left its folder already, so it can't be dropped: Restore puts it back.";
    private const string LastCopyError =
        "“Yellowstone S02E05.mkv” is the last copy this replacement would replace, so it can't be dropped: keep both copies instead.";
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";
    private const string ChangedMeanwhileError =
        "This replacement changed while the app was working on it, so it stopped. Reload the page to see where it stands.";
    private const string FailedReason = "The download failed, so nothing will be replaced.";
    private const string RenumberedProblem =
        "“Living room” now numbers the old copy “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05.mkv” season 2, episode 7, "
        + "not season 2, episode 5 as when it was scheduled.";
    private const string HeldReason =
        "Still held by the torrent “Yellowstone S02” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string NeedsLinkingReason =
        "“qBittorrent”'s downloads couldn't be told apart for this release, so the app can't follow it to verify the new file.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementDropCopyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every state but a review — a state added later included —: Vasyl, 2026-10-11, a copy is dropped only from a
    /// replacement that stopped for review.</summary>
    public static TheoryData<ReplacementJobState> NotStoppedForReview =>
        [.. Enum.GetValues<ReplacementJobState>().Where(state => state != ReplacementJobState.NeedsReview)];

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── dropped ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Drop_of_a_copy_from_a_replacement_the_check_stopped_takes_it_out_and_leaves_the_job_waiting_and_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantSeasonStopAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var before = await SnapshotAsync(cancellationToken);
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)RenumberedProblem, planted.Replacement.Id), (line.State, line.Reason, line.Id));
        // Nothing more happens until the owner's Retry: the job, its new files and the other copy are as they were.
        var after = await SnapshotAsync(cancellationToken);
        Assert.Equal((before.Jobs, before.NewFiles, before.Downloads), (after.Jobs, after.NewFiles, after.Downloads));
        // The episode stays where it is, out of the job; what stopped it stays beside it as its history.
        Assert.Equal(
            [
                (EpisodeFivePath, (DateTime?)now, (string?)RenumberedProblem, true, (Guid?)null),
                (EpisodeSixPath, null, null, false, planted.NewFiles[0].Id),
            ],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.AppPath, copy.DroppedUtc, TestTexts.En(copy.Problem), copy.OnlyDropSettles, copy.PairedNewFileId)));
        // MAM-27 — aimed at the copy itself, named by its file's own name.
        Assert.Equal(
            [(AuditAction.ReplacementCopyDropped, _harness.Owner.User.Id, AuditTargetKind.ReplacementJobFile, planted.Copies[0].Id, EpisodeFiveName)],
            await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.Equal(sentBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(planted.Replacement.Id));
    }

    [Fact]
    public async Task Drop_of_a_copy_from_a_replacement_the_mover_stopped_forgets_its_names_that_never_moved_and_keeps_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // An earlier run moved episode 6, whose subtitle a torrent holds; this run stopped on episode 5, whose subtitle a
        // torrent holds too and whose .nfo was found nowhere, and never reached episode 7 and its subtitle.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, EpisodeFivePath, TestMedia.File(inode: 25), ReplacementNameState.Planned),
                new PlantedName(ReplacementNameKind.Companion, EpisodeFiveSubtitlePath, TestMedia.File(inode: 26, sizeBytes: 98_304), ReplacementNameState.Stayed, PlantedMessage.Of(HeldReason)),
                new PlantedName(ReplacementNameKind.Companion, EpisodeFiveNfoPath, TestMedia.File(inode: 27, sizeBytes: 4_096), ReplacementNameState.Lost),
                new PlantedName(ReplacementNameKind.Video, EpisodeSixPath, TestMedia.File(inode: 28)),
                new PlantedName(ReplacementNameKind.Companion, EpisodeSixSubtitlePath, TestMedia.File(inode: 29, sizeBytes: 98_304), ReplacementNameState.Stayed, PlantedMessage.Of(HeldReason)),
                new PlantedName(ReplacementNameKind.Video, EpisodeSevenPath, TestMedia.File(inode: 30), ReplacementNameState.Planned),
                new PlantedName(ReplacementNameKind.Companion, EpisodeSevenSubtitlePath, TestMedia.File(inode: 31, sizeBytes: 98_304), ReplacementNameState.Planned),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: PlantedMessage.Of(HeldReason),
            inspectionStartedUtc: _factory.Time.GetUtcNow().UtcDateTime);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)HeldReason), (replacement.State, TestTexts.En(replacement.Reason)));
        // Episode 5's planned and stayed names would read "waits to move"; its lost .nfo stays, and so does every name of
        // the other copies — the moved, the stayed and the planned alike.
        Assert.Equal(
            [
                (EpisodeFiveNfoPath, ReplacementNameState.Lost),
                (EpisodeSixPath, ReplacementNameState.Quarantined),
                (EpisodeSixSubtitlePath, ReplacementNameState.Stayed),
                (EpisodeSevenPath, ReplacementNameState.Planned),
                (EpisodeSevenSubtitlePath, ReplacementNameState.Planned),
            ],
            (await NamesAsync(planted.Replacement.Id, cancellationToken)).Select(name => (name.AppPath, name.State)));
        Assert.Equal(
            [(AuditAction.ReplacementCopyDropped, _harness.Owner.User.Id, AuditTargetKind.ReplacementJobFile, planted.Copies[0].Id, EpisodeFiveName)],
            await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Drop_leaves_the_request_that_scheduled_the_job_replaying_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var series = await _harness.PlantSeriesAsync("Yellowstone", 2018, YellowstoneImdbId, YellowstoneTmdbTvId);
        await _harness.PlantEpisodeAsync(series, 2, 5, "Touching Your Enemy", EpisodeFivePath, inode: 25);
        await _harness.PlantEpisodeAsync(series, 2, 6, "Blood the Boy", EpisodeSixPath, inode: 26);
        await _harness.InspectAsync(cancellationToken);
        var release = await _harness.FindAsync("Yellowstone", 2018, Release, ReleaseKind.Tv, YellowstoneImdbId);
        _harness.Feed.TorrentFile = TestTorrent.V1(
            PackFolder,
            [
                (["Yellowstone.S02E05.1080p.WEB-DL.mkv"], TestTorrent.Bytes(TestTorrent.PieceLength + 5, seed: 1)),
                (["Yellowstone.S02E06.1080p.WEB-DL.mkv"], TestTorrent.Bytes(TestTorrent.PieceLength + 7, seed: 2)),
            ]);
        var request = new ScheduleReplacementRequest(
            Guid.NewGuid(),
            release.Id,
            _harness.Client.Id,
            _harness.TvShows.Id,
            [await _harness.FileIdAsync(EpisodeFivePath, cancellationToken), await _harness.FileIdAsync(EpisodeSixPath, cancellationToken)]);
        using var scheduled = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);
        var job = (await scheduled.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        // As the check of its new files leaves it when Jellyfin numbers episode 5 elsewhere now.
        var replacementId = job.Replacement!.Id;
        await _factory.WithDbAsync(async db =>
        {
            await db.ReplacementJobs.Where(row => row.Id == replacementId).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.State, ReplacementJobState.NeedsReview)
                    .SetProperty(row => row.Reason, PlantedMessage.Of(RenumberedProblem))
                    .SetProperty(row => row.InspectionStartedUtc, _factory.Time.GetUtcNow().UtcDateTime),
                cancellationToken);
            await db.ReplacementJobFiles.Where(copy => copy.ReplacementJobId == replacementId && copy.Position == 0).ExecuteUpdateAsync(
                setters => setters.SetProperty(copy => copy.Problem, PlantedMessage.Of(RenumberedProblem)).SetProperty(copy => copy.OnlyDropSettles, true),
                cancellationToken);
        });
        var episodeFive = (await _harness.CopiesAsync(cancellationToken))[0];
        var dropped = await DropAsync(replacementId, episodeFive.Id);

        //Act
        var replayed = await _factory.PostHttpRequest(BaseUrl, _harness.Owner.AccessToken, request);

        //Assert
        // The ticks as they were sent are still the job's: a dropped copy keeps its row.
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK), (scheduled.StatusCode, dropped.StatusCode, replayed.StatusCode));
        var again = (await replayed.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((job.Id, replacementId), (again.Id, again.Replacement!.Id));
        await _factory.WithDbAsync(async db => Assert.Equal(
            (1, 1, 2),
            (
                await db.DownloadJobs.CountAsync(cancellationToken),
                await db.ReplacementJobs.CountAsync(cancellationToken),
                await db.ReplacementJobFiles.CountAsync(cancellationToken))));
        Assert.NotNull((await _harness.CopiesAsync(cancellationToken))[0].DroppedUtc);
    }

    // ── refused ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotStoppedForReview))]
    public async Task Drop_of_a_copy_from_a_replacement_not_stopped_for_review_is_409_and_writes_nothing(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Marked as both stages', with two copies still in it: only the state refuses it.
        var planted = await _planter.PlantAsync(
            state,
            [],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            copies: [Episode(5), Episode(6)],
            inspectionStartedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NotStoppedForReviewError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Drop_of_a_copy_from_a_replacement_whose_download_the_app_cant_follow_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The following stopped it before any check started.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview, [], reason: PlantedMessage.Of(NeedsLinkingReason), copies: [Episode(5), Episode(6)]);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DownloadNotFollowedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Drop_of_a_copy_dropped_already_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantCheckStopAsync(
        [
            Episode(5) with { Problem = PlantedMessage.Of(RenumberedProblem), OnlyDropSettles = true, DroppedUtc = _factory.Time.GetUtcNow().UtcDateTime },
            Episode(6),
            Episode(7),
        ]);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadyDroppedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Theory]
    [InlineData(ReplacementNameState.Moving)]
    [InlineData(ReplacementNameState.Quarantined)]
    [InlineData(ReplacementNameState.Restoring)]
    [InlineData(ReplacementNameState.Deleting)]
    [InlineData(ReplacementNameState.Deleted)]
    public async Task Drop_of_a_copy_with_a_name_that_left_its_folder_is_409_and_writes_nothing(ReplacementNameState nameState)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, EpisodeFivePath, TestMedia.File(inode: 25), nameState),
                new PlantedName(ReplacementNameKind.Video, EpisodeSixPath, TestMedia.File(inode: 26), ReplacementNameState.Planned),
            ],
            quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime,
            reason: PlantedMessage.Of(HeldReason),
            inspectionStartedUtc: _factory.Time.GetUtcNow().UtcDateTime);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LeftItsFolderError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Theory]
    [InlineData("the only copy")]
    [InlineData("the copy left after the other was dropped")]
    public async Task Drop_of_the_last_copy_left_is_409_and_writes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var episodeFive = Episode(5) with { Problem = PlantedMessage.Of(RenumberedProblem), OnlyDropSettles = true };
        var planted = which == "the only copy"
            ? await PlantCheckStopAsync([episodeFive])
            : await PlantCheckStopAsync([episodeFive, Episode(6) with { DroppedUtc = _factory.Time.GetUtcNow().UtcDateTime }]);
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LastCopyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    [Fact]
    public async Task Drop_while_someone_is_working_on_the_replacement_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantSeasonStopAsync();
        var before = await SnapshotAsync(cancellationToken);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        var taken = inHand.TryTake(planted.Replacement.Id);

        //Act
        HttpResponseMessage response;
        try
        {
            response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);
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
    public async Task Drops_of_the_last_two_copies_at_once_drop_one_and_answer_the_other_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantSeasonStopAsync();
        var id = planted.Replacement.Id;
        HttpResponseMessage? second = null;
        // The drop of episode 6 arrives once the drop of episode 5 passed its checks and is about to claim the job.
        _factory.TransactionHook.BeforeNextTransaction(_ => true, async _ => second = await DropAsync(id, planted.Copies[1].Id));

        //Act
        var first = await DropAsync(id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second?.StatusCode);
        Assert.Equal(BeingWorkedOnError, await second!.ReadErrorAsync(cancellationToken));
        // The job keeps a copy to replace: never both dropped.
        Assert.Equal([true, false], (await _harness.CopiesAsync(cancellationToken)).Select(copy => copy.DroppedUtc is not null));
        Assert.Equal([AuditAction.ReplacementCopyDropped], (await _harness.ReplacementAuditsAsync(cancellationToken)).Select(audit => audit.Action));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    [Fact]
    public async Task Drop_whose_other_copy_was_dropped_meanwhile_is_409_as_the_last_copy_and_drops_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantSeasonStopAsync();
        var otherId = planted.Copies[1].Id;
        var droppedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        // Episode 6 is dropped once the drop of episode 5 passed its checks, just before it claims the job: what the claim
        // reads again under its lock is what decides.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.ReplacementJobFiles
                .Where(copy => copy.Id == otherId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.DroppedUtc, droppedUtc), CancellationToken.None)));

        //Act
        var response = await DropAsync(planted.Replacement.Id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LastCopyError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal([false, true], (await _harness.CopiesAsync(cancellationToken)).Select(copy => copy.DroppedUtc is not null));
        Assert.Empty(await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(planted.Replacement.Id));
    }

    [Fact]
    public async Task Drop_from_a_replacement_the_follower_ended_between_the_read_and_the_write_is_409_and_drops_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantSeasonStopAsync();
        var id = planted.Replacement.Id;
        // Its download failed: the follower ends the review once the drop passed its checks, just before it claims the job
        // — the follower never takes a job in hand, so only the claim's own condition sees it.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.ReplacementJobs
                .Where(job => job.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.State, ReplacementJobState.Failed).SetProperty(job => job.Reason, PlantedMessage.Of(FailedReason)),
                    CancellationToken.None)));

        //Act
        var response = await DropAsync(id, planted.Copies[0].Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChangedMeanwhileError, await response.ReadErrorAsync(cancellationToken));
        var replacement = await JobAsync(id, cancellationToken);
        Assert.Equal((ReplacementJobState.Failed, (string?)FailedReason), (replacement.State, TestTexts.En(replacement.Reason)));
        Assert.Equal([false, false], (await _harness.CopiesAsync(cancellationToken)).Select(copy => copy.DroppedUtc is not null));
        Assert.Empty(await _harness.ReplacementAuditsAsync(cancellationToken));
        Assert.False(_factory.Resolve<ReplacementJobsInHand>().Holds(id));
    }

    // ── whose ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a copy of another job")]
    [InlineData("a made-up copy")]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Drop_of_a_copy_that_isnt_this_replacements_or_of_an_unknown_or_another_users_replacement_is_a_bare_404_and_writes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        // Each a stop of the check its owner could drop a copy from: only what the route names refuses it.
        var owners = await PlantSeasonStopAsync();
        var others = await PlantSeasonStopAsync();
        var members = await PlantSeasonStopAsync(member.User.Id);
        var (replacementId, copyId) = which switch
        {
            "a copy of another job" => (owners.Replacement.Id, others.Copies[0].Id),
            "a made-up copy" => (owners.Replacement.Id, Guid.NewGuid()),
            "an unknown replacement" => (Guid.NewGuid(), owners.Copies[0].Id),
            _ => (members.Replacement.Id, members.Copies[0].Id),
        };
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var response = await DropAsync(replacementId, copyId);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(before, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> DropAsync(Guid replacementJobId, Guid copyId) =>
        _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/copies/{copyId}/drop", _harness.Owner.AccessToken);

    /// <summary>Yellowstone's episode <paramref name="number"/> of season 2 as it was ticked, still at its old path.</summary>
    private static PlantedCopy Episode(int number) =>
        new($"/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv", TestMedia.File(inode: 20 + number), SeasonNumber: 2, EpisodeNumber: number);

    /// <summary>
    /// A season replacement the check of its new files stopped for review because Jellyfin numbers episode 5 elsewhere now
    /// — a problem only a drop settles —, while episode 6 paired with its new file: started and the library asked, never
    /// taken up by the mover.
    /// </summary>
    private Task<PlantedReplacement> PlantSeasonStopAsync(Guid? requestedByUserId = null) =>
        PlantCheckStopAsync(
            [Episode(5) with { Problem = PlantedMessage.Of(RenumberedProblem), OnlyDropSettles = true }, Episode(6) with { PairedWithNewFile = 0 }],
            requestedByUserId);

    /// <summary>A replacement the check of its new files stopped for review, holding <paramref name="copies"/> and episode 6's
    /// new file, scheduled by the owner — or by <paramref name="requestedByUserId"/>.</summary>
    private Task<PlantedReplacement> PlantCheckStopAsync(IReadOnlyList<PlantedCopy> copies, Guid? requestedByUserId = null)
    {
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        return _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: PlantedMessage.Of(RenumberedProblem),
            requestedByUserId: requestedByUserId,
            copies: copies,
            newFiles:
            [
                new ReplacementNewFileFacts(
                    NewEpisodeSixName,
                    ClientSizeBytes: 1_073_741_824,
                    ClientProgress: 1.0,
                    AppPath: NewEpisodeSixPath,
                    Disk: TestMedia.File(inode: 36, sizeBytes: 1_073_741_824),
                    CheckedUtc: startedUtc,
                    LibraryItemId: "item-36",
                    Kind: LibraryFileKind.Episode,
                    Refreshed: true,
                    SeasonNumber: 2,
                    EpisodeNumber: 6,
                    ReadUtc: startedUtc,
                    MediaWorkId: Guid.NewGuid(),
                    Identifiers: [(MediaIdentifierScheme.Imdb, YellowstoneImdbId, true)]),
            ],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
    }

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
    /// was renamed or sent to qBittorrent or Jellyfin.</summary>
    private async Task AssertNothingWrittenAsync(ReplacementRowsSnapshot before, CancellationToken cancellationToken)
    {
        Assert.Equal(before, await SnapshotAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_harness.Writes());
    }
}
