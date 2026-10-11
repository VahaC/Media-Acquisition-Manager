using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0031 — the owner's Retry (POST /api/replacements/{id}/retry) of a replacement the mover stopped —
// a torrent holding the old copy, a rename refused, a move, restore or deletion cut —: back to Verified with its reason
// gone and the owner's ReplacementRetried row, so the mover's next pass checks the copies again; nothing is written to the
// media and no client or library is asked. A stop of MAM-25's check of the new files isn't the mover's to retry (MAM-27's
// Retry), nor is any other state: 409, nothing written. One request at a time per job; a bare 404 for another user's. The
// pass after a Retry of a job with nothing left to rename asks no client and settles it where it rests: Quarantined while the
// quarantine holds something of it, Completed when its videos were deleted from it for good.
public sealed class ReplacementRetryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string NothingToRetryError = "This replacement isn't waiting for review after a move, so there's nothing to retry here.";
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string CheckReason = "“qBittorrent” no longer has this download, so the app can't confirm its files are complete.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementRetryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
        Assert.Equal(
            [(AuditAction.ReplacementRetried, _harness.Owner.User.Id, AuditTargetKind.ReplacementJob, planted.Replacement.Id, ReplacementQuarantinePlanter.ReleaseTitle)],
            await _harness.QuarantineAuditsAsync(cancellationToken));
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

    [Fact]
    public async Task Retry_of_a_replacement_the_check_of_its_new_files_stopped_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-25's review: the mover never looked at it, so its Retry is MAM-27's.
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: CheckReason);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NothingToRetryError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Theory]
    [InlineData(ReplacementJobState.Verified)]
    [InlineData(ReplacementJobState.Quarantining)]
    [InlineData(ReplacementJobState.Quarantined)]
    [InlineData(ReplacementJobState.Restoring)]
    [InlineData(ReplacementJobState.Restored)]
    [InlineData(ReplacementJobState.Completed)]
    [InlineData(ReplacementJobState.Failed)]
    public async Task Retry_of_a_replacement_not_waiting_for_review_is_409_and_writes_nothing(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Marked as the mover's stage, so only the state refuses it.
        var planted = await _planter.PlantAsync(state, [], quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime);

        //Act
        var response = await RetryAsync(planted.Replacement.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NothingToRetryError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Fact]
    public async Task Retry_of_a_replacement_someone_is_working_on_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime, reason: HeldReason);
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
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Retry_of_an_unknown_or_another_users_replacement_is_a_bare_404_and_writes_nothing(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview, [], quarantineCheckedUtc: _factory.Time.GetUtcNow().UtcDateTime, reason: HeldReason, requestedByUserId: member.User.Id);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;

        //Act
        var response = await RetryAsync(id);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(planted, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> RetryAsync(Guid replacementJobId) =>
        _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/retry", _harness.Owner.AccessToken);

    private async Task<ReplacementJobEntity> JobAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity job = null!;
        await _factory.WithDbAsync(async db => job = await db.ReplacementJobs.AsNoTracking().SingleAsync(row => row.Id == replacementJobId, cancellationToken));
        return job;
    }

    /// <summary>That the replacement is as <paramref name="planted"/> left it, with no audit row of the quarantine.</summary>
    private async Task AssertNothingWrittenAsync(PlantedReplacement planted, CancellationToken cancellationToken)
    {
        var replacement = await JobAsync(planted.Replacement.Id, cancellationToken);
        Assert.Equal(
            (planted.Replacement.State, planted.Replacement.Reason, planted.Replacement.StateChangedUtc),
            (replacement.State, replacement.Reason, replacement.StateChangedUtc));
        Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
    }
}
