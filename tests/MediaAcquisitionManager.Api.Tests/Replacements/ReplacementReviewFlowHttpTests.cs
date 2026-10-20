using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-27, ADR 0035, ADR 0032, ADR 0031 — the owner's way out of a replacement that stopped for review, end to end across
// the app's own passes and the owner's routes, with nothing in between planted by hand. A copy a torrent holds shows on
// the job's page as stopped by that torrent; once the owner removed it in qBittorrent, Retry lets the mover's next pass
// move the copy, and the page shows it in the quarantine — qBittorrent only ever read. A film Jellyfin gave no id within
// the hour: once the owner identified it there, Retry checks it again from its files, asks Jellyfin to look again and
// waits on it anew, and Jellyfin's ids verify it, so the mover moves the old copy; without a fix the check stops again an
// hour later with the same reason, and nothing moves. An episode Jellyfin now numbers elsewhere offers no Retry: the owner
// drops it, Retry checks the other again, and only the other moves — the dropped one stays where it is, as it was. A
// pack's copy a torrent holds is dropped, and Retry moves the others without the torrent stopping anything. Each step's
// own cases are ReplacementRetry, ReplacementDropCopy, ReplacementDetails, ReplacementDroppedCopy and the
// ReplacementVerification and ReplacementQuarantine tests'.
public sealed class ReplacementReviewFlowHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalName = "Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalInTorrent = "Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const string YellowstoneImdbId = "tt4236770";
    private const string YellowstoneTmdbTvId = "73586";
    private const string YellowstoneRelease = "Yellowstone (S2) (2019) WEB-DL 1080p";
    private const string PackFolder = "Yellowstone.S02.1080p.WEB-DL";
    // The TV Shows category saves to /downloads/tv-shows, which qBittorrent's mapping brings to /media/tv-shows: Серіали's
    // folder.
    private const string NewFolder = "/media/tv-shows/Yellowstone.S02.1080p.WEB-DL";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    // TestMedia's size of an old copy, which a torrent holding it lists.
    private const long OldSize = 3_891_719_630;
    private const string LookRequest = "POST /Library/Media/Updated";
    private const string SignInCall = "POST /api/v2/auth/login";
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";

    // The files as Jellyfin names them: its /data is the app's /media. Spelled out, never worked out.
    private const string JellyfinNewPath = "/data/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const string JellyfinTheatricalPath = "/data/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";

    // Sentences the job's page shows, spelled out rather than read from product code.
    private const string AlienHeldReason =
        "Still held by the torrent “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string EpisodeTwoHeldReason =
        "Still held by the torrent “Yellowstone.S02E02” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string NoIdReason =
        "“Living room” gave “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” no TMDb or IMDb id within an hour, so it can't be told to be Alien (1979).";
    private const string RenumberedReason =
        "“Living room” now numbers the old copy “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E01.mkv” season 2, episode 3, "
        + "not season 2, episode 1 as when it was scheduled.";
    private const string EpisodeOneDropOnlyError =
        "“Yellowstone S02E01.mkv” can only be dropped from this replacement, or both copies kept: checking again wouldn't change it.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private MediaWorkEntity _alien = null!;
    private MediaWorkEntity _yellowstone = null!;
    private string _seriesItemId = null!;

    public ReplacementReviewFlowHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut in the ticked Фільми, and Yellowstone's first three episodes of season 2 in the ticked
        // Серіали, Jellyfin's items of them read; all inspected.
        _alien = await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _yellowstone = await _harness.PlantSeriesAsync("Yellowstone", 2018, YellowstoneImdbId, YellowstoneTmdbTvId);
        _seriesItemId = _harness.SeriesItemIdOf(_yellowstone);
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 1, "A Thousand Acres", OldPathOf(1), inode: 21);
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 2, "New Beginnings", OldPathOf(2), inode: 22);
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 3, "Freight Trains and Monsters", OldPathOf(3), inode: 23);
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── held by a torrent ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flow_of_a_copy_a_torrent_holds_shows_it_stopped_by_the_torrent_and_once_the_owner_removed_it_retry_lets_the_next_pass_move_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await VerifiedFilmAsync(cancellationToken);
        var newFile = (await _harness.NewFilesAsync(cancellationToken)).Single();
        var torrent = TorrentHoldings.Holding("Alien.1979.Theatrical", TheatricalInTorrent, OldSize);
        _harness.QBittorrent.AddTorrent(torrent);
        var qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;
        var addRequests = _harness.QBittorrent.AddRequests.Count;

        //Act
        await QuarantineAsync(cancellationToken);
        var held = await DetailsAsync(replacement.Id, cancellationToken);
        var renamesWhileHeld = _factory.Quarantine.Renames.Count;
        // The owner removes the torrent in qBittorrent without its files, then presses Retry on the job's page.
        _harness.QBittorrent.RemoveTorrent(torrent.Hash);
        using var response = await RetryAsync(replacement.Id);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        var moved = await DetailsAsync(replacement.Id, cancellationToken);

        //Assert
        // Held: the job's page shows the mover's stop and the copy stopped by the torrent, saying what to do; Retry and Keep
        // both offered, Drop not — it is the replacement's only copy. Nothing renamed, no name written.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)AlienHeldReason, (ReplacementStop?)ReplacementStop.Move, true, true, 0),
            (held.State, held.Reason, held.Stop, held.CanRetry, held.CanCancel, held.DropOnlyCopyCount));
        var heldCopy = Assert.Single(held.Copies);
        Assert.Equal(
            (TheatricalPath, ReplacementCopyStatus.Stopped, (string?)AlienHeldReason, false, (Guid?)newFile.Id, (DateTime?)null, false),
            (heldCopy.AppPath, heldCopy.Status, heldCopy.Problem, heldCopy.OnlyDropSettles, heldCopy.PairedNewFileId, heldCopy.DroppedUtc, heldCopy.CanDrop));
        Assert.Empty(held.Names);
        Assert.Equal(0, renamesWhileHeld);

        // Retry: 200 with the job verified again and its reason gone.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retried = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.Verified, (string?)null, replacement.Id), (retried.State, retried.Reason, retried.Id));

        // The next pass: the copy in the quarantine, on the job's page too, renamed there with its identity, never copied.
        Assert.Equal(
            (ReplacementJobState.Quarantined, (string?)null, (ReplacementStop?)null, false, false, true),
            (moved.State, moved.Reason, moved.Stop, moved.CanRetry, moved.CanCancel, moved.CanRestore));
        var movedCopy = Assert.Single(moved.Copies);
        Assert.Equal(
            (ReplacementCopyStatus.InQuarantine, (string?)null, (Guid?)newFile.Id, false),
            (movedCopy.Status, movedCopy.Problem, movedCopy.PairedNewFileId, movedCopy.CanDrop));
        Assert.Equal(
            [(TheatricalPath, ReplacementNameState.Quarantined)],
            moved.Names.Select(name => (name.OriginalPath, name.State)));
        Assert.Equal([(TheatricalPath, QuarantinePathOf(replacement, TheatricalPath))], _factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 1L), await WhatIsAtAsync(QuarantinePathOf(replacement, TheatricalPath), cancellationToken));

        // ADR 0031 — qBittorrent only read, every torrent listed once a pass: the owner removed the torrent there, not the
        // app. Jellyfin told once that the old copy went.
        var calls = QBittorrentCallsSince(qbittorrentMark);
        Assert.All(calls, call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == SignInCall, call));
        Assert.Equal(2, calls.Count(call => call == TorrentsListing));
        Assert.Equal(addRequests, _harness.QBittorrent.AddRequests.Count);
        Assert.Equal([LookRequest], _harness.Writes());

        // The trail, save by save: the check's and the mover's rows have no actor, the owner's carry him.
        var name = Assert.Single(await NamesAsync(cancellationToken));
        var owner = _harness.Owner.User.Id;
        Assert.Equal(
            [
                AlienAudited(AuditAction.ReplacementScheduled, replacement.Id, owner),
                AlienAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementVerified, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementRetried, replacement.Id, owner),
                AlienAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, name.Id, TheatricalName),
                NameAudited(AuditAction.ReplacementNameMoved, name.Id, TheatricalName),
                AlienAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
            ],
            await _harness.ReplacementAuditsAsync(cancellationToken));
    }

    // ── no id from Jellyfin ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flow_of_a_film_jellyfin_gave_no_id_once_the_owner_identified_it_there_retry_checks_it_again_and_the_next_passes_verify_and_move_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await FilmStoppedWithoutAnIdAsync(cancellationToken);
        var firstAskedUtc = replacement.LibraryAskedUtc;

        //Act
        var stopped = await DetailsAsync(replacement.Id, cancellationToken);
        // The owner identifies the new item as Alien in Jellyfin — Identify sets its IMDb id, which Jellyfin's next look
        // at the file keeps —, then presses Retry on the job's page.
        _harness.JellyfinServer.OnScanRequested = null;
        _harness.JellyfinServer.PlantFilm(JellyfinNewPath, "Alien", 1979, imdbId: AlienImdbId);
        var retriedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        using var response = await RetryAsync(replacement.Id);
        var retried = await _harness.ReplacementAsync(cancellationToken);
        var newFilesAfterRetry = await _harness.NewFilesAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        var askedAgainUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _harness.VerifyAsync(cancellationToken);
        var checking = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        var verified = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        var moved = await DetailsAsync(replacement.Id, cancellationToken);

        //Assert
        // Stopped by the check: Retry and Keep both offered; the new file says why, and the copy waits unpaired.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)NoIdReason, (ReplacementStop?)ReplacementStop.Check, true, true, 0),
            (stopped.State, stopped.Reason, stopped.Stop, stopped.CanRetry, stopped.CanCancel, stopped.DropOnlyCopyCount));
        Assert.Equal(
            (ReplacementCopyStatus.NotPaired, (string?)null, (Guid?)null),
            (stopped.Copies.Single().Status, stopped.Copies.Single().Problem, stopped.Copies.Single().PairedNewFileId));
        Assert.Equal(
            (ReplacementNewFileProgress.Stopped, (string?)NoIdReason),
            (stopped.NewFiles.Single().Progress, stopped.NewFiles.Single().Problem));

        // Retry: 200, back to Downloaded with its reason gone, and the stopped check's evidence forgotten at once.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.Downloaded, (string?)null, replacement.Id), (line.State, line.Reason, line.Id));
        Assert.Equal(
            (ReplacementJobState.Downloaded, (string?)null, retriedUtc, (DateTime?)null),
            (retried.State, TestTexts.En(retried.Reason), retried.StateChangedUtc, retried.QuarantineCheckedUtc));
        Assert.Empty(newFilesAfterRetry);

        // The next pass checks again from the files and asks Jellyfin to look again, later than the first time: the hour
        // runs from that request.
        Assert.Equal(
            (ReplacementJobState.Inspecting, (DateTime?)askedAgainUtc, (DateTime?)askedAgainUtc),
            (checking.State, checking.InspectionStartedUtc, checking.LibraryAskedUtc));
        Assert.True(checking.LibraryAskedUtc > firstAskedUtc);

        // Jellyfin's ids decide: the new file is Alien, paired with the old copy, and the job verified.
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (verified.State, TestTexts.En(verified.Reason)));
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)_alien.Id, (string?)null), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal([(0, false, MediaIdentifierScheme.Imdb, AlienImdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));

        // The mover's next pass moves the old copy; the job's page shows it in the quarantine, paired with its new file.
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (moved.State, moved.Reason));
        Assert.Equal(
            (ReplacementCopyStatus.InQuarantine, (Guid?)newFile.Id),
            (moved.Copies.Single().Status, moved.Copies.Single().PairedNewFileId));
        Assert.Equal([(TheatricalPath, QuarantinePathOf(replacement, TheatricalPath))], _factory.Quarantine.Renames);

        // Jellyfin asked twice to look at the new file, by its own path, then told once that the old copy went; nothing
        // else written to it or to qBittorrent.
        Assert.Equal([[JellyfinNewPath], [JellyfinNewPath], [JellyfinTheatricalPath]], _harness.JellyfinServer.ScanRequests);
        Assert.Equal([["Created"], ["Created"], ["Deleted"]], _harness.JellyfinServer.ScanUpdateTypes);
        Assert.Equal([LookRequest, LookRequest, LookRequest], _harness.Writes());

        // The trail, save by save: the check's rows of the app, the owner's Retry of the check, the check again, the move.
        var name = Assert.Single(await NamesAsync(cancellationToken));
        var owner = _harness.Owner.User.Id;
        Assert.Equal(
            [
                AlienAudited(AuditAction.ReplacementScheduled, replacement.Id, owner),
                AlienAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementCheckRetried, replacement.Id, owner),
                AlienAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementVerified, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, name.Id, TheatricalName),
                NameAudited(AuditAction.ReplacementNameMoved, name.Id, TheatricalName),
                AlienAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
            ],
            await _harness.ReplacementAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Flow_of_a_film_jellyfin_still_gives_no_id_after_retry_stops_again_an_hour_after_it_was_asked_again_with_the_same_reason_and_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = await FilmStoppedWithoutAnIdAsync(cancellationToken);
        // Nothing fixed in Jellyfin: its item of the new file stays as it was, with no id.
        _harness.JellyfinServer.OnScanRequested = null;

        //Act
        using var response = await RetryAsync(replacement.Id);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var again = await DetailsAsync(replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Waited on anew for the whole hour from the second request, then stopped with the same reason — and Retry offered
        // again, as a fix in Jellyfin may still settle it.
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, TestTexts.En(beforeTheHour.Reason)));
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)NoIdReason, (ReplacementStop?)ReplacementStop.Check, true),
            (again.State, again.Reason, again.Stop, again.CanRetry));
        Assert.Equal(
            (ReplacementNewFileProgress.Stopped, (string?)NoIdReason),
            (again.NewFiles.Single().Progress, again.NewFiles.Single().Problem));
        Assert.Equal(ReplacementCopyStatus.NotPaired, again.Copies.Single().Status);

        // Nothing moved: the old copy where it was, as it was; Jellyfin asked twice to look, nothing else written.
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.Empty(await NamesAsync(cancellationToken));
        Assert.Equal([LookRequest, LookRequest], _harness.Writes());
        var owner = _harness.Owner.User.Id;
        Assert.Equal(
            [
                AlienAudited(AuditAction.ReplacementScheduled, replacement.Id, owner),
                AlienAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementCheckRetried, replacement.Id, owner),
                AlienAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                AlienAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
            ],
            await _harness.ReplacementAuditsAsync(cancellationToken));
    }

    // ── only a drop settles it ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flow_of_an_episode_jellyfin_now_numbers_elsewhere_offers_no_retry_until_the_owner_drops_it_then_retry_moves_the_other_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 and 2 ticked, both in the pack. Since the schedule, the owner renumbered the old episode 1's item as
        // episode 3 (ADR 0017: the library alone says what an item is): no new file can be its pair, whatever is fixed.
        await SeasonDownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), Episode(2));
        PlantNewEpisodes(1, 2);
        AnswerTheRequestToLookWith(1, 2);
        _harness.JellyfinServer.Episodes.RemoveAll(item => (string?)item["Path"] == OldLibraryPathOf(1));
        _harness.JellyfinServer.PlantEpisode(OldLibraryPathOf(1), "A Thousand Acres", _seriesItemId, 2, 3);
        await TwoChecksAsync(cancellationToken);
        _harness.JellyfinServer.OnScanRequested = null;
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        var copies = await _harness.CopiesAsync(cancellationToken);

        //Act
        var stopped = await DetailsAsync(replacement.Id, cancellationToken);
        using var refused = await RetryAsync(replacement.Id);
        var droppedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        using var dropResponse = await DropAsync(replacement.Id, copies[0].Id);
        var afterDrop = await DetailsAsync(replacement.Id, cancellationToken);
        using var retryResponse = await RetryAsync(replacement.Id);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await TwoChecksAsync(cancellationToken);
        var verified = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        var moved = await DetailsAsync(replacement.Id, cancellationToken);

        //Assert
        // Stopped by the renumbered episode, which only a drop settles: no Retry offered, Drop offered on each copy.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)RenumberedReason, (ReplacementStop?)ReplacementStop.Check, false, true, 1),
            (stopped.State, stopped.Reason, stopped.Stop, stopped.CanRetry, stopped.CanCancel, stopped.DropOnlyCopyCount));
        Assert.Equal(
            [
                (OldPathOf(1), ReplacementCopyStatus.Stopped, (string?)RenumberedReason, true, true),
                (OldPathOf(2), ReplacementCopyStatus.Paired, null, false, true),
            ],
            stopped.Copies.Select(copy => (copy.AppPath, copy.Status, copy.Problem, copy.OnlyDropSettles, copy.CanDrop)));

        // Retry refused, naming the copy; the drop goes through and leaves the job waiting for the owner's Retry.
        Assert.Equal((HttpStatusCode.Conflict, (string?)EpisodeOneDropOnlyError), (refused.StatusCode, await refused.ReadErrorAsync(cancellationToken)));
        Assert.Equal(HttpStatusCode.OK, dropResponse.StatusCode);
        var dropLine = (await dropResponse.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)RenumberedReason), (dropLine.State, dropLine.Reason));
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)RenumberedReason, true, 0),
            (afterDrop.State, afterDrop.Reason, afterDrop.CanRetry, afterDrop.DropOnlyCopyCount));
        Assert.Equal(
            [(ReplacementCopyStatus.Dropped, (DateTime?)droppedUtc, false), (ReplacementCopyStatus.Paired, null, false)],
            afterDrop.Copies.Select(copy => (copy.Status, copy.DroppedUtc, copy.CanDrop)));

        // Retry: the other episode checked again and verified; the new episode 1 is the show's, simply added.
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (verified.State, TestTexts.En(verified.Reason)));
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.Equal(
            [((string?)NewPathOf(1), (Guid?)_yellowstone.Id, (string?)null), (NewPathOf(2), _yellowstone.Id, null)],
            newFiles.Select(file => (file.AppPath, file.MediaWorkId, TestTexts.En(file.Problem))));

        // The move: episode 2 alone, into the quarantine; episode 1 stays where it is with its identity, its last problem
        // kept as history, never planned.
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (moved.State, moved.Reason));
        Assert.Equal(
            [
                (OldPathOf(1), ReplacementCopyStatus.Dropped, (string?)RenumberedReason, true, (DateTime?)droppedUtc),
                (OldPathOf(2), ReplacementCopyStatus.InQuarantine, null, false, null),
            ],
            moved.Copies.Select(copy => (copy.AppPath, copy.Status, copy.Problem, copy.OnlyDropSettles, copy.DroppedUtc)));
        Assert.Equal((Guid?)newFiles[1].Id, moved.Copies[1].PairedNewFileId);
        var names = await NamesAsync(cancellationToken);
        Assert.Equal([(copies[1].Id, OldPathOf(2), ReplacementNameState.Quarantined)], names.Select(name => (name.ReplacementJobFileId, name.AppPath, name.State)));
        Assert.Equal([(OldPathOf(2), QuarantinePathOf(replacement, OldPathOf(2)))], _factory.Quarantine.Renames);
        Assert.Equal([OldPathOf(2)], await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 21L), await WhatIsAtAsync(OldPathOf(1), cancellationToken));

        // Jellyfin asked twice to look at the new episodes, then told the old episode 2 went; nothing else written.
        Assert.Equal(
            [[NewLibraryPathOf(1), NewLibraryPathOf(2)], [NewLibraryPathOf(1), NewLibraryPathOf(2)], [OldLibraryPathOf(2)]],
            _harness.JellyfinServer.ScanRequests);
        Assert.Equal([LookRequest, LookRequest, LookRequest], _harness.Writes());

        // The trail: the refused Retry recorded nothing; the drop is aimed at the copy itself.
        var owner = _harness.Owner.User.Id;
        Assert.Equal(
            [
                SeasonAudited(AuditAction.ReplacementScheduled, replacement.Id, owner),
                SeasonAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                SeasonAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                CopyAudited(copies[0].Id, "Yellowstone S02E01.mkv"),
                SeasonAudited(AuditAction.ReplacementCheckRetried, replacement.Id, owner),
                SeasonAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                SeasonAudited(AuditAction.ReplacementVerified, replacement.Id, null),
                SeasonAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, names[0].Id, "Yellowstone S02E02.mkv"),
                NameAudited(AuditAction.ReplacementNameMoved, names[0].Id, "Yellowstone S02E02.mkv"),
                SeasonAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
            ],
            await _harness.ReplacementAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Flow_of_a_pack_whose_one_copy_a_torrent_holds_drops_that_copy_then_retry_moves_the_others_without_its_torrent_stopping_anything()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 to 3 ticked and verified; a torrent the owner keeps seeding holds the old episode 2.
        await SeasonDownloadedAsync([OldPathOf(1), OldPathOf(2), OldPathOf(3)], cancellationToken, Episode(1), Episode(2), Episode(3));
        PlantNewEpisodes(1, 2, 3);
        AnswerTheRequestToLookWith(1, 2, 3);
        await TwoChecksAsync(cancellationToken);
        _harness.JellyfinServer.OnScanRequested = null;
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        var copies = await _harness.CopiesAsync(cancellationToken);
        var torrent = TorrentHoldings.Holding("Yellowstone.S02E02", "Yellowstone/Season 2/Yellowstone S02E02.mkv", OldSize, savePath: "/downloads/tv-shows");
        _harness.QBittorrent.AddTorrent(torrent);
        _harness.Mark();
        var qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;
        var addRequests = _harness.QBittorrent.AddRequests.Count;

        //Act
        await QuarantineAsync(cancellationToken);
        var held = await DetailsAsync(replacement.Id, cancellationToken);
        var droppedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        using var dropResponse = await DropAsync(replacement.Id, copies[1].Id);
        var afterDrop = await _harness.ReplacementAsync(cancellationToken);
        using var retryResponse = await RetryAsync(replacement.Id);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);
        var moved = await DetailsAsync(replacement.Id, cancellationToken);

        //Assert
        // Held: the mover's stop names the torrent on episode 2; the others paired, each copy may be dropped.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)EpisodeTwoHeldReason, (ReplacementStop?)ReplacementStop.Move, true),
            (held.State, held.Reason, held.Stop, held.CanRetry));
        Assert.Equal(
            [
                (ReplacementCopyStatus.Paired, (string?)null, true),
                (ReplacementCopyStatus.Stopped, EpisodeTwoHeldReason, true),
                (ReplacementCopyStatus.Paired, null, true),
            ],
            held.Copies.Select(copy => (copy.Status, copy.Problem, copy.CanDrop)));

        // The drop leaves the stop as it was until the owner's Retry, which puts the job back in the mover's queue.
        Assert.Equal(HttpStatusCode.OK, dropResponse.StatusCode);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)EpisodeTwoHeldReason), (afterDrop.State, TestTexts.En(afterDrop.Reason)));
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = (await retryResponse.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement!;
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (retried.State, retried.Reason));

        // The next pass moves episodes 1 and 3; episode 2 stays where it is, as it was, the torrent's stop its history.
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (moved.State, moved.Reason));
        Assert.Equal(
            [
                (ReplacementCopyStatus.InQuarantine, (string?)null, (DateTime?)null),
                (ReplacementCopyStatus.Dropped, EpisodeTwoHeldReason, droppedUtc),
                (ReplacementCopyStatus.InQuarantine, null, null),
            ],
            moved.Copies.Select(copy => (copy.Status, copy.Problem, copy.DroppedUtc)));
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [(copies[0].Id, OldPathOf(1), ReplacementNameState.Quarantined), (copies[2].Id, OldPathOf(3), ReplacementNameState.Quarantined)],
            names.Select(name => (name.ReplacementJobFileId, name.AppPath, name.State)));
        Assert.Equal(
            [(OldPathOf(1), QuarantinePathOf(replacement, OldPathOf(1))), (OldPathOf(3), QuarantinePathOf(replacement, OldPathOf(3)))],
            _factory.Quarantine.Renames);
        Assert.Equal([OldPathOf(1), OldPathOf(3)], await _harness.ChangedOldCopiesAsync(cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, TestMedia.DeviceId, 22L), await WhatIsAtAsync(OldPathOf(2), cancellationToken));

        // ADR 0031 — qBittorrent only read, once a pass; the torrent the owner kept still there, still holding episode 2,
        // and never asked about by its hash. Jellyfin told once that episodes 1 and 3 went.
        var calls = QBittorrentCallsSince(qbittorrentMark);
        Assert.All(calls, call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == SignInCall, call));
        Assert.Equal(2, calls.Count(call => call == TorrentsListing));
        Assert.DoesNotContain(calls, call => call.Contains(torrent.Hash, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(addRequests, _harness.QBittorrent.AddRequests.Count);
        Assert.Single(_harness.QBittorrent.Torrents, kept => kept.Hash == torrent.Hash);
        Assert.Equal([LookRequest], _harness.Writes());
        Assert.Equal([OldLibraryPathOf(1), OldLibraryPathOf(3)], _harness.JellyfinServer.ScanRequests.Last());

        // The trail: the mover's stop, the owner's drop aimed at the copy and his Retry, then the mover's move.
        var owner = _harness.Owner.User.Id;
        Assert.Equal(
            [
                SeasonAudited(AuditAction.ReplacementScheduled, replacement.Id, owner),
                SeasonAudited(AuditAction.ReplacementCheckStarted, replacement.Id, null),
                SeasonAudited(AuditAction.ReplacementVerified, replacement.Id, null),
                SeasonAudited(AuditAction.ReplacementNeedsReview, replacement.Id, null),
                CopyAudited(copies[1].Id, "Yellowstone S02E02.mkv"),
                SeasonAudited(AuditAction.ReplacementRetried, replacement.Id, owner),
                SeasonAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, null),
                NameAudited(AuditAction.ReplacementNameMoving, names[0].Id, "Yellowstone S02E01.mkv"),
                NameAudited(AuditAction.ReplacementNameMoved, names[0].Id, "Yellowstone S02E01.mkv"),
                NameAudited(AuditAction.ReplacementNameMoving, names[1].Id, "Yellowstone S02E03.mkv"),
                NameAudited(AuditAction.ReplacementNameMoved, names[1].Id, "Yellowstone S02E03.mkv"),
                SeasonAudited(AuditAction.ReplacementQuarantined, replacement.Id, null),
            ],
            await _harness.ReplacementAuditsAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string OldPathOf(int number) => $"/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv";

    private static string OldLibraryPathOf(int number) => $"/data/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv";

    private static string NewPathOf(int number) => $"{NewFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv";

    private static string NewLibraryPathOf(int number) => $"/data/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv";

    /// <summary>Episode <paramref name="number"/> of the pack as qBittorrent lists it, done.</summary>
    private static FakeQBittorrentFile Episode(int number) => new($"{PackFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv", NewSize, 1.0);

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, verified: the new film downloaded
    /// to the Movies category's folder, on the disk, and identified by Jellyfin as Alien. Jellyfin answers a request to
    /// look with nothing again.
    /// </summary>
    private async Task<ReplacementJobEntity> VerifiedFilmAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        await _harness.VerifiedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
            [(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken())],
            paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId),
            cancellationToken);
        return await _harness.ReplacementAsync(cancellationToken);
    }

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, downloaded and on the disk, whose
    /// check stopped for review: Jellyfin, asked to look, read the new file and found no provider's id for it, and the hour
    /// from that request ran out. Jellyfin still makes the same item of it when asked to look again.
    /// </summary>
    private async Task<ReplacementJobEntity> FilmStoppedWithoutAnIdAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        await _harness.DownloadedAsync(release, _harness.Movies, [TheatricalPath], [new FakeQBittorrentFile(NewName, NewSize, 1.0)], cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien.1979.Directors.Cut.2160p", 1979);
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait);
        await _harness.VerifyAsync(cancellationToken);
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        if (replacement is not { State: ReplacementJobState.NeedsReview } || TestTexts.En(replacement.Reason) != NoIdReason)
            throw new InvalidOperationException($"The replacement is {replacement.State}, not stopped for the missing id: {TestTexts.En(replacement.Reason)}");
        return replacement;
    }

    /// <summary>
    /// The owner schedules the replacement of the copies at <paramref name="tickedPaths"/> by the season 2 release through
    /// the real route, and qBittorrent finishes it holding <paramref name="files"/> in the TV Shows category's folder: the
    /// replacement is Downloaded.
    /// </summary>
    private async Task SeasonDownloadedAsync(IReadOnlyList<string> tickedPaths, CancellationToken cancellationToken, params FakeQBittorrentFile[] files)
    {
        var release = await _harness.FindAsync("Yellowstone", 2018, YellowstoneRelease, ReleaseKind.Tv, YellowstoneImdbId);
        await _harness.DownloadedAsync(release, _harness.TvShows, tickedPaths, files, cancellationToken);
    }

    /// <summary>The pack's episodes <paramref name="numbers"/> on the disk, each a file of its own.</summary>
    private void PlantNewEpisodes(params int[] numbers)
    {
        foreach (var number in numbers)
            _factory.Files.AddFile(NewPathOf(number), TestMedia.File(inode: 30 + number, sizeBytes: NewSize), TestMedia.Continental());
    }

    /// <summary>
    /// When asked to look at its own path of the pack's episodes <paramref name="numbers"/>, Jellyfin makes each an episode
    /// of Yellowstone's series item in season 2, at its number, read and refreshed.
    /// </summary>
    private void AnswerTheRequestToLookWith(params int[] numbers)
    {
        var byLibraryPath = numbers.ToDictionary(NewLibraryPathOf, StringComparer.Ordinal);
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            foreach (var path in paths)
            {
                if (byLibraryPath.TryGetValue(path, out var number))
                    _harness.JellyfinServer.PlantEpisode(path, $"Episode {number}", _seriesItemId, 2, number);
            }
        };
    }

    /// <summary>The verifier's two passes a <see cref="ReplacementVerificationTracker.PassInterval"/> apart: the files' checks
    /// and the request to look, then Jellyfin's read and the pairing.</summary>
    private async Task TwoChecksAsync(CancellationToken cancellationToken)
    {
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
    }

    /// <summary>One pass of the mover, which must not have logged an error.</summary>
    private async Task QuarantineAsync(CancellationToken cancellationToken)
    {
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } error)
            throw new InvalidOperationException($"A pass logged an error: {error.Category}: {error.Message}");
    }

    /// <summary>
    /// The replacement <paramref name="replacementJobId"/> as the job's page reads it, as the owner signed in afresh: a
    /// flow that ran the clock past an hour outlived the session it started with. Throws unless the answer is 200.
    /// </summary>
    private async Task<ReplacementDetailsDto> DetailsAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest($"{BaseUrl}/{replacementJobId}", await OwnerTokenAsync());
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The replacement's read answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The owner's Retry on the job's page, signed in afresh.</summary>
    private async Task<HttpResponseMessage> RetryAsync(Guid replacementJobId) =>
        await _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/retry", await OwnerTokenAsync());

    /// <summary>The owner's Drop of the ticked copy <paramref name="copyId"/> on the job's page, signed in afresh.</summary>
    private async Task<HttpResponseMessage> DropAsync(Guid replacementJobId, Guid copyId) =>
        await _factory.PostHttpRequest($"{BaseUrl}/{replacementJobId}/copies/{copyId}/drop", await OwnerTokenAsync());

    private async Task<string> OwnerTokenAsync() => (await new UserSeeder(_factory.Scope).CreateSession(_harness.Owner.User)).AccessToken;

    /// <summary>Every request to qBittorrent since <paramref name="mark"/>, as <c>METHOD /path?query</c>.</summary>
    private List<string> QBittorrentCallsSince(int mark) =>
        [.. _factory.QBittorrentHttp.Requests.Skip(mark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The replacement's names, in their order.</summary>
    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobNames.AsNoTracking().OrderBy(name => name.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>What the disk shows at <paramref name="appPath"/> inside /media: whether a file is there, and its device and
    /// inode (0 for none).</summary>
    private async Task<(FileInventoryStatus Status, long DeviceId, long Inode)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.DeviceId ?? 0, described.Value?.Inode ?? 0);
    }

    private static string QuarantinePathOf(ReplacementJobEntity replacement, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, appPath);

    /// <summary>An audit row of Alien's replacement, by <paramref name="actor"/> (none for the app's own), naming it by its
    /// release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) AlienAudited(AuditAction action, Guid replacementJobId, Guid? actor) =>
        (action, actor, AuditTargetKind.ReplacementJob, replacementJobId, AlienRelease);

    /// <summary>An audit row of the season's replacement, by <paramref name="actor"/> (none for the app's own), naming it by
    /// its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) SeasonAudited(AuditAction action, Guid replacementJobId, Guid? actor) =>
        (action, actor, AuditTargetKind.ReplacementJob, replacementJobId, YellowstoneRelease);

    /// <summary>An audit row of one name of the mover's, the app's own, naming it by the file's own name.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, Guid nameId, string fileName) =>
        (action, null, AuditTargetKind.ReplacementJobName, nameId, fileName);

    /// <summary>The owner's drop of one ticked copy, aimed at the copy itself and naming it by its file's own name.</summary>
    private (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) CopyAudited(Guid copyId, string fileName) =>
        (AuditAction.ReplacementCopyDropped, _harness.Owner.User.Id, AuditTargetKind.ReplacementJobFile, copyId, fileName);
}
