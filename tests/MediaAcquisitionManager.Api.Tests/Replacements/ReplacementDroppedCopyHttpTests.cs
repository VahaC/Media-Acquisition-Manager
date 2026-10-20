using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-27, ADR 0032, ADR 0035, ADR 0031 — what the app's own passes do with a ticked copy the owner dropped from its
// replacement: it stays where it is, and the job goes on as if it had never been ticked. The check never reads it in
// Jellyfin, never pairs it and never waits on it — the job is verified once every copy left in it is paired, and a new file
// Jellyfin identified at its place is simply added —, while a new file that is the dropped copy itself still stops the
// check (it is still in the library). The check marks each copy's problem that only a drop settles — an episode with no new
// file at its place — apart from one a Retry may settle — two new files of it —, and a check of the files run again
// forgets the last pairs, problems and marks of the copies left in the job alone: a dropped copy keeps its last ones as its
// history. A pass that stops the job before the pairing — a new file changed, Jellyfin unreadable once the hour ran out —
// forgets what a waiting pass before it found, so no mark of a pass that decided nothing withholds the owner's Retry. The mover
// never plans, checks or renames a dropped copy, so a torrent still holding it stops nothing, and the Download dialog lets
// another download tick it again. The owner's Drop itself is ReplacementDropCopyHttpTests'; here a copy is dropped as that
// route writes it, one column of its row.
public sealed class ReplacementDroppedCopyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string OptionsUrl = "/api/replacements/options";
    private const string YellowstoneImdbId = "tt4236770";
    private const string YellowstoneTmdbTvId = "73586";
    private const string Release = "Yellowstone (S2) (2019) WEB-DL 1080p";
    private const string PackFolder = "Yellowstone.S02.1080p.WEB-DL";
    // The TV Shows category saves to /downloads/tv-shows, which qBittorrent's mapping brings to /media/tv-shows: Серіали's
    // folder.
    private const string NewFolder = "/media/tv-shows/Yellowstone.S02.1080p.WEB-DL";
    private const string HdName = "Yellowstone.S02E01.720p.WEB-DL.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    // TestMedia's size of an old copy, which a hard link of it shares.
    private const long OldSize = 3_891_719_630;
    private const long ContinentalMilliseconds = 5_179_168;
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string NoSecondEpisodeReason =
        "No new file is Yellowstone (2018), season 2, episode 2, so “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv” has nothing to replace it.";
    private const string NoThirdEpisodeReason =
        "No new file is Yellowstone (2018), season 2, episode 3, so “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E03.mkv” has nothing to replace it.";
    private const string TwoNewFilesReason =
        "Two new files are Yellowstone (2018), season 2, episode 1: “/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.1080p.WEB-DL.mkv” and "
        + "“/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.720p.WEB-DL.mkv”; the app can't tell which one replaces the old copy.";
    private const string OldCopyItselfReason =
        "The new file “/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E02.1080p.WEB-DL.mkv” is the old copy itself.";
    private const string HeldReason =
        "Still held by the torrent “Yellowstone.S02E02” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string FirstEpisodeTooShortReason =
        "“/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.1080p.WEB-DL.mkv” runs 0:50:00, less than three quarters of the old copy's 1:26:19.";
    private const string FirstEpisodeChangedReason =
        "The new file “/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.1080p.WEB-DL.mkv” changed after it was checked.";
    private const string JellyfinUnavailableReason =
        "The app couldn't read “Living room” within an hour of asking it to look at the new files: Jellyfin answered 503 Service Unavailable. Its own log says why.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private MediaWorkEntity _yellowstone = null!;
    private string _seriesItemId = null!;

    public ReplacementDroppedCopyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Yellowstone's first two episodes of season 2, inspected, in the ticked Серіали; Jellyfin's items of them read.
        _yellowstone = await _harness.PlantSeriesAsync("Yellowstone", 2018, YellowstoneImdbId, YellowstoneTmdbTvId);
        _seriesItemId = _harness.SeriesItemIdOf(_yellowstone);
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 1, "A Thousand Acres", OldPathOf(1), inode: 21);
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 2, "New Beginnings", OldPathOf(2), inode: 22);
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the check ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_never_reads_pairs_or_waits_on_a_dropped_episode_and_verifies_the_episodes_left()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 and 2 ticked; the pack holds 1 and 3. Episode 2 alone would stop the job, and Jellyfin no longer listing
        // its old file would hold it up for an hour — but the owner dropped it, and Retry sent the job back to its check.
        await DownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), Episode(3));
        PlantNewFiles(1, 3);
        AnswerTheRequestToLookWith((NewPathOf(1), 1), (NewPathOf(3), 3));
        _harness.JellyfinServer.Episodes.RemoveAll(item => (string?)item["Path"] == OldLibraryPathOf(2));
        var droppedUtc = await DropAsync(1, cancellationToken);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (replacement.State, TestTexts.En(replacement.Reason)));
        // The episode left paired with its new file; the extra one, of the job's work, simply added.
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.Equal(
            [((string?)NewPathOf(1), (Guid?)_yellowstone.Id, (string?)null), (NewPathOf(3), _yellowstone.Id, null)],
            newFiles.Select(file => (file.AppPath, file.MediaWorkId, TestTexts.En(file.Problem))));
        // The dropped copy's row is as the drop left it: no pair, no problem, no mark, no running time read for it.
        Assert.Equal(
            [
                ((Guid?)newFiles[0].Id, (string?)null, false, (long?)ContinentalMilliseconds, (DateTime?)null),
                (null, null, false, null, droppedUtc),
            ],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.PairedNewFileId, TestTexts.En(copy.Problem), copy.OnlyDropSettles, copy.DurationMilliseconds, copy.DroppedUtc)));
        // Jellyfin was read for the episode left — and never for the dropped one, under any id its old file could have.
        var readPaths = _factory.JellyfinHttp.Requests.Select(request => request.Uri.AbsolutePath).ToList();
        Assert.Contains($"/Items/{JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, OldLibraryPathOf(1))}", readPaths);
        string[] droppedIds =
        [
            .. new[] { JellyfinItemIds.MovieType, JellyfinItemIds.EpisodeType, JellyfinItemIds.VideoType, JellyfinItemIds.TrailerType }
                .Select(type => JellyfinItemIds.Of(type, OldLibraryPathOf(2))),
        ];
        Assert.DoesNotContain(readPaths, path => droppedIds.Any(id => path.Contains(id, StringComparison.Ordinal)));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementVerified, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_simply_adds_the_new_file_of_a_dropped_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Both episodes ticked and both in the pack; episode 2 dropped: its new file is the show's, paired with nothing.
        await DownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), Episode(2));
        PlantNewFiles(1, 2);
        AnswerTheRequestToLookWith((NewPathOf(1), 1), (NewPathOf(2), 2));
        await DropAsync(1, cancellationToken);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(ReplacementJobState.Verified, (await _harness.ReplacementAsync(cancellationToken)).State);
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.All(newFiles, file => Assert.Equal(((Guid?)_yellowstone.Id, (string?)null), (file.MediaWorkId, TestTexts.En(file.Problem))));
        Assert.Equal(
            [(Guid?)newFiles[0].Id, null],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => copy.PairedNewFileId));
    }

    [Fact]
    public async Task Verify_of_a_new_file_that_is_a_dropped_copy_itself_still_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The pack's episode 2 is a second name of the old episode 2 — a hard link: dropped or not, that copy is still in
        // the library, and a new file that is it is no new copy.
        await DownloadedAsync(
            [OldPathOf(1), OldPathOf(2)],
            cancellationToken,
            Episode(1),
            new FakeQBittorrentFile($"{PackFolder}/Yellowstone.S02E02.1080p.WEB-DL.mkv", OldSize, 1.0));
        PlantNewFiles(1);
        _factory.Files.AddFile(NewPathOf(2), TestMedia.File(inode: 22, linkCount: 2), TestMedia.Continental());
        var droppedUtc = await DropAsync(1, cancellationToken);

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)OldCopyItselfReason), (replacement.State, TestTexts.En(replacement.Reason)));
        Assert.Equal(
            [((string?)NewPathOf(1), (string?)null), (NewPathOf(2), OldCopyItselfReason)],
            (await _harness.NewFilesAsync(cancellationToken)).Select(file => (file.AppPath, TestTexts.En(file.Problem))));
        Assert.Equal(droppedUtc, (await _harness.CopiesAsync(cancellationToken))[1].DroppedUtc);
        // Stopped by the files' checks: Jellyfin was never asked to look.
        Assert.Empty(_harness.JellyfinServer.ScanRequests);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_marks_a_ticked_episode_no_new_file_is_as_only_a_drop_settling_it_and_two_new_files_of_one_as_not()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 and 2 ticked; the pack holds episode 1 twice and episode 3: episode 1 is an unclear pair Jellyfin may
        // still settle, episode 2 has no new file — the download holds none, which no fix changes.
        await DownloadedAsync(
            [OldPathOf(1), OldPathOf(2)],
            cancellationToken,
            Episode(1),
            new FakeQBittorrentFile($"{PackFolder}/{HdName}", NewSize, 1.0),
            Episode(3));
        PlantNewFiles(1, 3);
        PlantNewFile($"{NewFolder}/{HdName}", inode: 39);
        AnswerTheRequestToLookWith((NewPathOf(1), 1), ($"{NewFolder}/{HdName}", 1), (NewPathOf(3), 3));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)TwoNewFilesReason), (replacement.State, TestTexts.En(replacement.Reason)));
        Assert.Equal(
            [((Guid?)null, (string?)TwoNewFilesReason, false), (null, NoSecondEpisodeReason, true)],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.PairedNewFileId, TestTexts.En(copy.Problem), copy.OnlyDropSettles)));
    }

    [Fact]
    public async Task Verify_checking_the_files_again_forgets_the_last_pairs_problems_and_marks_of_the_copies_left_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 3, "Freight Trains and Monsters", OldPathOf(3), inode: 23);
        await _harness.InspectAsync(cancellationToken);
        // Episodes 1 to 3 ticked; the pack holds 1 and 4: episode 1 pairs, 2 and 3 have no new file — the check stops.
        await DownloadedAsync([OldPathOf(1), OldPathOf(2), OldPathOf(3)], cancellationToken, Episode(1), Episode(4));
        PlantNewFiles(1, 4);
        AnswerTheRequestToLookWith((NewPathOf(1), 1), (NewPathOf(4), 4));
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        var stopped = await _harness.CopiesAsync(cancellationToken);
        var stoppedNewFiles = await _harness.NewFilesAsync(cancellationToken);
        var droppedUtc = await DropAsync(2, cancellationToken);
        // Back to its check, as a Retry sends it — its own forgetting left out, so the files' stage alone is seen here.
        await BackToTheCheckAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // As the stop left them: episode 1 paired, 2 and 3 marked as only a drop settling them.
        Assert.Equal(
            [
                ((Guid?)stoppedNewFiles[0].Id, (string?)null, false),
                (null, NoSecondEpisodeReason, true),
                (null, NoThirdEpisodeReason, true),
            ],
            stopped.Select(copy => (copy.PairedNewFileId, TestTexts.En(copy.Problem), copy.OnlyDropSettles)));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Inspecting, true), (replacement.State, replacement.LibraryAskedUtc is not null));
        // The new files read afresh, nothing of Jellyfin's last word kept on them.
        Assert.Equal(
            [((string?)NewPathOf(1), (Guid?)null, (string?)null), (NewPathOf(4), null, null)],
            (await _harness.NewFilesAsync(cancellationToken)).Select(file => (file.AppPath, file.MediaWorkId, TestTexts.En(file.Problem))));
        // The copies left in the job start over; the dropped one keeps its last problem and mark.
        Assert.Equal(
            [
                ((Guid?)null, (string?)null, false, (DateTime?)null),
                (null, null, false, null),
                (null, NoThirdEpisodeReason, true, droppedUtc),
            ],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.PairedNewFileId, TestTexts.En(copy.Problem), copy.OnlyDropSettles, copy.DroppedUtc)));
    }

    [Theory]
    // Written again since the media inspector read it — downloaded anew, say —: what was found of it no longer holds.
    [InlineData(true, FirstEpisodeChangedReason)]
    // Jellyfin answers 503 to everything until the hour runs out.
    [InlineData(false, JellyfinUnavailableReason)]
    public async Task Verify_that_stops_before_the_pairing_forgets_what_a_waiting_pass_found_so_retry_stays_offered(bool changed, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 and 2 ticked; episode 1's new file runs 0:50:00 against the old copy's 1:26:19 — a problem only a drop
        // settles —, while Jellyfin doesn't list episode 2's new file yet: the pass waits, keeping what it found.
        var job = await DownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), Episode(2));
        _factory.Files.AddFile(
            NewPathOf(1), TestMedia.File(31, sizeBytes: NewSize), TestMedia.Continental() with { Duration = TimeSpan.FromMilliseconds(3_000_000) });
        PlantNewFile(NewPathOf(2), inode: 32);
        AnswerTheRequestToLookWith((NewPathOf(1), 1));
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        var waited = await _harness.CopiesAsync(cancellationToken);
        var waitedState = (await _harness.ReplacementAsync(cancellationToken)).State;
        if (changed)
        {
            _factory.Files.AddFile(
                NewPathOf(1), TestMedia.File(31, sizeBytes: NewSize, modifiedUtc: TestMedia.ModifiedUtc.AddMinutes(5)), TestMedia.Continental());
            _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        }
        else
        {
            _factory.JellyfinHttp.Handler = null;
            _factory.Time.Advance(ReplacementVerificationRules.LibraryWait);
        }

        //Act
        await _harness.VerifyAsync(cancellationToken);
        var stopped = await _harness.CopiesAsync(cancellationToken);
        var stoppedNewFiles = await _harness.NewFilesAsync(cancellationToken);
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        // A session of now: the hour waited may have outlived the one the owner signed in with.
        var session = await new UserSeeder(_factory.Scope).CreateSession(_harness.Owner.User);
        var retried = await _factory.PostHttpRequest($"/api/replacements/{line!.Id}/retry", session.AccessToken);

        //Assert
        // The waiting pass found episode 1's copy too short, which only a drop settles, and kept no pair.
        Assert.Equal(ReplacementJobState.Inspecting, waitedState);
        Assert.Equal(
            [((Guid?)null, (string?)FirstEpisodeTooShortReason, true), (null, null, false)],
            waited.Select(copy => (copy.PairedNewFileId, TestTexts.En(copy.Problem), copy.OnlyDropSettles)));
        // This pass stopped on the changed file, or the unread library, before any pairing: it keeps nothing the pass
        // before it found, so the reason is the one thing that stopped the job.
        Assert.Equal(
            [((Guid?)null, (string?)null, false), (null, null, false)],
            stopped.Select(copy => (copy.PairedNewFileId, TestTexts.En(copy.Problem), copy.OnlyDropSettles)));
        Assert.Equal([changed ? reason : null, null], stoppedNewFiles.Select(file => TestTexts.En(file.Problem)));
        // Retry is offered — a new check may well settle it — and goes through.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)reason, (ReplacementStop?)ReplacementStop.Check, true, 0),
            (line.State, line.Reason, line.Stop, line.CanRetry, line.DropOnlyCopyCount));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(ReplacementJobState.Downloaded, (await _harness.ReplacementAsync(cancellationToken)).State);
    }

    // ── the move ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_moves_the_episodes_left_and_never_the_dropped_one_a_torrent_still_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await VerifiedAsync(cancellationToken);
        // A torrent seeds the old episode 2: the move stops for review, naming it.
        _harness.QBittorrent.AddTorrent(TorrentHoldings.Holding("Yellowstone.S02E02", "Yellowstone/Season 2/Yellowstone S02E02.mkv", OldSize, savePath: "/downloads/tv-shows"));
        var qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;
        var addRequests = _harness.QBittorrent.AddRequests.Count;
        await QuarantineAsync(cancellationToken);
        var held = await _harness.ReplacementAsync(cancellationToken);
        // The owner drops episode 2 — the torrent stays — and presses Retry: the job is verified again (the Retry route's
        // own write, as a conditional update).
        var droppedUtc = await DropAsync(1, cancellationToken);
        await _factory.WithDbAsync(db => db.ReplacementJobs
            .Where(row => row.Id == replacement.Id && row.State == ReplacementJobState.NeedsReview && row.QuarantineCheckedUtc != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, ReplacementJobState.Verified).SetProperty(row => row.Reason, (Message?)null), cancellationToken));
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)HeldReason), (held.State, TestTexts.En(held.Reason)));
        var moved = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (moved.State, TestTexts.En(moved.Reason)));
        // Episode 1 alone planned and moved; episode 2 where it was, with the torrent's stop kept as its history.
        var names = await NamesAsync(cancellationToken);
        var copies = await _harness.CopiesAsync(cancellationToken);
        Assert.Equal(
            [(copies[0].Id, ReplacementNameKind.Video, OldPathOf(1), ReplacementNameState.Quarantined)],
            names.Select(name => (name.ReplacementJobFileId, name.Kind, name.AppPath, name.State)));
        Assert.Equal([(OldPathOf(1), ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, OldPathOf(1)))], _factory.Quarantine.Renames);
        Assert.Equal([OldPathOf(1)], await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.Equal(
            [((string?)null, (DateTime?)null), (HeldReason, droppedUtc)],
            copies.Select(copy => (TestTexts.En(copy.Problem), copy.DroppedUtc)));
        Assert.Equal(
            [
                AuditAction.ReplacementScheduled,
                AuditAction.ReplacementCheckStarted,
                AuditAction.ReplacementVerified,
                AuditAction.ReplacementNeedsReview,
                AuditAction.ReplacementQuarantineStarted,
                AuditAction.ReplacementNameMoving,
                AuditAction.ReplacementNameMoved,
                AuditAction.ReplacementQuarantined,
            ],
            (await _harness.ReplacementAuditsAsync(cancellationToken)).Select(audit => audit.Action));
        // ADR 0031 — qBittorrent only read, every torrent listed once a pass, the stop's and the move's; the torrent the
        // owner kept untouched.
        var calls = _factory.QBittorrentHttp.Requests.Skip(qbittorrentMark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}").ToList();
        Assert.All(calls, call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Equal(2, calls.Count(call => call == TorrentsListing));
        Assert.Equal(addRequests, _harness.QBittorrent.AddRequests.Count);
        Assert.Single(_harness.QBittorrent.Torrents, torrent => torrent.Name == "Yellowstone.S02E02");
    }

    // ── the Download dialog ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Options_lets_another_download_tick_a_copy_dropped_from_a_replacement_not_yet_ended(bool dropped)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Another download's replacement of both episodes stopped for review; episode 2 dropped from it, or not.
        await new ReplacementQuarantinePlanter(_factory, _harness).PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: PlantedMessage.Of(NoSecondEpisodeReason),
            copies:
            [
                new PlantedCopy(OldPathOf(1), TestMedia.File(inode: 21), SeasonNumber: 2, EpisodeNumber: 1),
                new PlantedCopy(OldPathOf(2), TestMedia.File(inode: 22), PlantedMessage.Of(NoSecondEpisodeReason), OnlyDropSettles: true, DroppedUtc: dropped ? now : null, SeasonNumber: 2, EpisodeNumber: 2),
            ],
            inspectionStartedUtc: now);
        var release = await _harness.FindAsync("Yellowstone", 2018, Release, ReleaseKind.Tv, YellowstoneImdbId);

        //Act
        using var response = await _factory.GetHttpRequest($"{OptionsUrl}?releaseId={release.Id}&destinationId={_harness.TvShows.Id}", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            [
                (OldPathOf(1), (ReplacementCopyUnavailableReason?)ReplacementCopyUnavailableReason.AlreadyScheduled),
                (OldPathOf(2), dropped ? null : ReplacementCopyUnavailableReason.AlreadyScheduled),
            ],
            Assert.Single(options.Works).Episodes.SelectMany(episode => episode.Copies).Select(copy => (copy.File.AppPath!, copy.Unavailable)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string OldPathOf(int number) => $"/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv";

    private static string OldLibraryPathOf(int number) => $"/data/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv";

    private static string NewPathOf(int number) => $"{NewFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv";

    /// <summary>Episode <paramref name="number"/> of the pack as qBittorrent lists it, done.</summary>
    private static FakeQBittorrentFile Episode(int number) => new($"{PackFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv", NewSize, 1.0);

    /// <summary>
    /// The owner schedules the replacement of the copies at <paramref name="tickedPaths"/> by the season 2 release through
    /// the real route, and qBittorrent finishes it holding <paramref name="files"/> in the TV Shows category's folder: the
    /// replacement is Downloaded.
    /// </summary>
    private async Task<DownloadJobDto> DownloadedAsync(IReadOnlyList<string> tickedPaths, CancellationToken cancellationToken, params FakeQBittorrentFile[] files)
    {
        var release = await _harness.FindAsync("Yellowstone", 2018, Release, ReleaseKind.Tv, YellowstoneImdbId);
        return await _harness.DownloadedAsync(release, _harness.TvShows, tickedPaths, files, cancellationToken);
    }

    /// <summary>
    /// The owner's replacement of episodes 1 and 2 by the season 2 release, verified as the check leaves it: both in the
    /// pack, each Jellyfin's at its place, the check's two passes run — then Jellyfin answers a request to look with
    /// nothing again.
    /// </summary>
    private async Task<ReplacementJobEntity> VerifiedAsync(CancellationToken cancellationToken)
    {
        await DownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), Episode(2));
        PlantNewFiles(1, 2);
        AnswerTheRequestToLookWith((NewPathOf(1), 1), (NewPathOf(2), 2));
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        _harness.JellyfinServer.OnScanRequested = null;
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        if (replacement.State != ReplacementJobState.Verified)
            throw new InvalidOperationException($"The replacement is {replacement.State}, not Verified: {replacement.Reason}");
        return replacement;
    }

    /// <summary>The pack's episodes <paramref name="numbers"/> on the disk, each a file of its own.</summary>
    private void PlantNewFiles(params int[] numbers)
    {
        foreach (var number in numbers)
            PlantNewFile(NewPathOf(number), inode: 30 + number);
    }

    private void PlantNewFile(string path, long inode) =>
        _factory.Files.AddFile(path, TestMedia.File(inode, sizeBytes: NewSize), TestMedia.Continental());

    /// <summary>
    /// When asked to look at its own path of each of <paramref name="items"/>, given by its app path, Jellyfin makes it an
    /// episode of Yellowstone's series item in season 2, at its number, read and refreshed.
    /// </summary>
    private void AnswerTheRequestToLookWith(params (string Path, int Number)[] items)
    {
        var byLibraryPath = items.ToDictionary(item => ReplacementVerificationHarness.InJellyfin(item.Path), StringComparer.Ordinal);
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            foreach (var path in paths)
            {
                if (byLibraryPath.TryGetValue(path, out var item))
                    _harness.JellyfinServer.PlantEpisode(path, $"Episode {item.Number}", _seriesItemId, 2, item.Number);
            }
        };
    }

    /// <summary>
    /// The owner drops the replacement's ticked copy at <paramref name="position"/> as the Drop route writes it — when, and
    /// nothing else of its row —, at the factory's now, which it returns.
    /// </summary>
    private async Task<DateTime> DropAsync(int position, CancellationToken cancellationToken)
    {
        var droppedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.WithDbAsync(db => db.ReplacementJobFiles
            .Where(copy => copy.Position == position)
            .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.DroppedUtc, droppedUtc), cancellationToken));
        return droppedUtc;
    }

    /// <summary>The stopped check's job back to Downloaded, its reason gone: the state the owner's Retry of a check sends
    /// it to.</summary>
    private Task BackToTheCheckAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.ReplacementJobs
            .Where(row => row.State == ReplacementJobState.NeedsReview && row.QuarantineCheckedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, ReplacementJobState.Downloaded).SetProperty(row => row.Reason, (Message?)null), cancellationToken));

    /// <summary>One pass of the mover, which must not have logged an error.</summary>
    private async Task QuarantineAsync(CancellationToken cancellationToken)
    {
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } error)
            throw new InvalidOperationException($"A pass logged an error: {error.Category}: {error.Message}");
    }

    /// <summary>The replacement's names, in their order.</summary>
    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobNames.AsNoTracking().OrderBy(name => name.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>An audit row of the check as it should be: the app's own (no actor), naming the replacement by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) Audited(AuditAction action, Guid replacementJobId) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, Release);
}
