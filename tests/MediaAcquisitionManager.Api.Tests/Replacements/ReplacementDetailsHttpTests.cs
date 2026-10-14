using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0031 — what the owner reads of a replacement's quarantine. GET /api/replacements/{id} lists every
// name of its move in its plan's order, where each was, where it went, its size, whether another name keeps its data — as
// the disk says it of each quarantined file now —, where it is now and why, with the room its names in the quarantine take,
// the room Delete for good would free (nothing for a file with a second name) and which of Retry, Restore and Delete for
// good are offered; a bare 404 for another user's.
// MAM-27, ADR 0032, ADR 0035 — and who stopped it (its download, the check of its new files, or the mover), where a running
// check is and until when it waits, every ticked copy with its pair, its problem, whether only a drop settles it, its drop
// and where it stands now, every new file with what each check found of it and the ids the library gave it, and the works
// they are expected to be with their ids. Retry is offered for a check's stop unless a copy still in it only a drop
// settles, Keep both before any name left its folder, Drop on a copy of a stopped replacement none of whose names left and
// that isn't the last one. Each reason the ticket names is read as the trackers write it: a torrent that holds the old copy,
// no id from Jellyfin while it waits and after the hour, an unclear pair, replacement switched off. A fixed number of reads,
// however many copies and new files a season pack brings.
// Writes nothing. The Jobs page's line (GET /api/downloads and /api/downloads/{id}) carries the replacement's id, how many
// copies are in the quarantine, how many companions stayed in their folder and whether Retry is offered, read for a whole
// page in one query of the names and one of the copies.
public sealed class ReplacementDetailsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string DownloadsUrl = "/api/downloads";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string NfoPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.nfo";
    private const string AliensPath = "/media/movies/Aliens (1986)/Aliens (1986).mkv";
    private const string Alien3Path = "/media/movies/Alien 3 (1992)/Alien 3 (1992).mkv";
    private const long TheatricalSize = 3_891_719_630;
    private const long SubtitleSize = 98_304;
    private const long NfoSize = 4_096;
    private const long AliensSize = 5_000_000_000;
    private const string AlienImdbId = "tt0078748";
    private const string AlienTmdbId = "348";
    private const string AliensImdbId = "tt0090605";
    // The Director's Cut release, downloaded into the Movies category's folder: Фільми's.
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const long TakenMilliseconds = 5_604_672;
    private const string PackFolder = "Alien.1979.Directors.Cut";
    private const string PackUhdPath = "/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv";
    private const string PackHdPath = "/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv";
    // qBittorrent's Movies category saves to /downloads/movies, the app's /media/movies.
    private const string TheatricalInTorrent = "Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    // Yellowstone's second season in Серіали, and the season pack that brings it again.
    private const string SeasonFolder = "/media/tv-shows/Yellowstone (2018)/Season 02";
    private const string PackSeasonFolder = "/media/tv-shows/Yellowstone.S02.1080p";
    private const long EpisodeSize = 2_147_483_648;
    private const long EpisodeMilliseconds = 3_130_000;
    private const string YellowstoneTmdbTvId = "73586";
    private const string YellowstoneImdbId = "tt4236770";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string HeldProblem = "Held by the torrent “Alien” in “qBittorrent”, so it stays where it is.";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string TorrentHeldReason =
        "Still held by the torrent “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string NoIdReason =
        "“Living room” gave “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” no TMDb or IMDb id within an hour, so it can't be told to be Alien (1979).";
    private const string TwoNewFilesReason =
        "Two new files are Alien (1979): “/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv” and "
        + "“/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv”; the app can't tell which one replaces the old copy.";
    private const string NotAlienReason = "The new file isn't Alien (1979).";
    private const string NeedsLinkingReason =
        "“qBittorrent”'s downloads couldn't be told apart for this release, so the app can't follow it to verify the new file.";
    private const string CancelledReason = "You cancelled the replacement, so nothing will be replaced.";
    private const string RenumberedProblem =
        "“Living room” now numbers the old copy “/media/tv-shows/Yellowstone (2018)/Season 02/Yellowstone S02E05.mkv” season 2, episode 6, "
        + "not season 2, episode 5 as when it was scheduled.";
    private const string TooShortProblem =
        "“/media/tv-shows/Yellowstone.S02.1080p/Yellowstone.S02E07.1080p.mkv” runs 0:30:00, less than three quarters of the old copy's 0:52:10.";
    private const string CoversSeveralProblem =
        "“/media/tv-shows/Yellowstone (2018)/Season 02/Yellowstone S02E09.mkv” covers episodes 9–10 of season 2, so one new episode can't replace it.";
    private const string ChangedProblem = "The new file “/media/tv-shows/Yellowstone.S02.1080p/Yellowstone.S02E01.1080p.mkv” changed after it was checked.";
    private const string OtherSeriesProblem =
        "“Living room” placed “/media/tv-shows/Yellowstone.S02.1080p/Yellowstone.S02E08.1080p.mkv” in another series (IMDb tt0903747), not Yellowstone (2018).";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementDetailsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the replacement's page ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_of_a_quarantined_replacement_lists_every_name_in_its_plans_order_with_the_room_it_takes_and_frees()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two copies: Alien with its subtitle moved and its .nfo held by a torrent; Aliens, which a seeding torrent also
        // names elsewhere (a second name), moved.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1, sizeBytes: TheatricalSize)),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: SubtitleSize)),
                new PlantedName(ReplacementNameKind.Companion, NfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 2)),
            ]);
        var id = planted.Replacement.Id;
        var describedBefore = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{id}", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = (await response.Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (id, planted.Download.Id, ReplacementJobState.Quarantined, (string?)null, false, false, true, true),
            (details.Id, details.DownloadJobId, details.State, details.Reason, details.SwitchedOff, details.CanRetry, details.CanRestore, details.CanDeleteForGood));
        // ADR 0031 — Aliens' data stays with its other name: in the quarantine's room, but nothing Delete for good frees.
        Assert.Equal(
            (TheatricalSize + SubtitleSize + AliensSize, TheatricalSize + SubtitleSize),
            (details.QuarantinedBytes, details.FreesBytes));
        Assert.Equal(
            [
                new ReplacementNameDto(planted.Names[0].Id, ReplacementNameKind.Video, TheatricalPath, QuarantinePathOf(id, TheatricalPath), TheatricalSize, false, ReplacementNameState.Quarantined, null),
                new ReplacementNameDto(planted.Names[1].Id, ReplacementNameKind.Companion, SubtitlePath, QuarantinePathOf(id, SubtitlePath), SubtitleSize, false, ReplacementNameState.Quarantined, null),
                new ReplacementNameDto(planted.Names[2].Id, ReplacementNameKind.Companion, NfoPath, QuarantinePathOf(id, NfoPath), NfoSize, false, ReplacementNameState.Stayed, HeldProblem),
                new ReplacementNameDto(planted.Names[3].Id, ReplacementNameKind.Video, AliensPath, QuarantinePathOf(id, AliensPath), AliensSize, true, ReplacementNameState.Quarantined, null),
            ],
            details.Names);
        // A read of the rows and of each quarantined name's link count where it is now — never another file —; nothing is
        // written.
        Assert.Equal(
            [QuarantinePathOf(id, TheatricalPath), QuarantinePathOf(id, SubtitlePath), QuarantinePathOf(id, AliensPath)],
            _factory.Files.DescribedPaths.Skip(describedBefore));
        Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Get_of_a_quarantined_name_whose_other_name_went_since_the_move_says_deleting_it_frees_its_room()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Aliens moved as a second name of a seeding torrent's file; the torrent was removed with its files since, so the
        // quarantined name is the file's last one now.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 2))]);
        var id = planted.Replacement.Id;
        _factory.Files.AddFile(QuarantinePathOf(id, AliensPath), TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 1));

        //Act
        var details = await GetAsync(id, cancellationToken);

        //Assert
        // ADR 0031 — the count the move read is stale: what the disk says now decides the room Delete for good frees.
        Assert.Equal((AliensSize, AliensSize), (details.QuarantinedBytes, details.FreesBytes));
        Assert.False(Assert.Single(details.Names).OtherNames);
    }

    [Fact]
    public async Task Get_of_a_quarantined_name_another_file_took_the_place_of_keeps_the_count_the_move_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize, linkCount: 2))]);
        var id = planted.Replacement.Id;
        // Not the file the move put there: its count says nothing of the quarantined one.
        _factory.Files.AddFile(QuarantinePathOf(id, AliensPath), TestMedia.File(inode: 99, sizeBytes: AliensSize, linkCount: 1));

        //Act
        var details = await GetAsync(id, cancellationToken);

        //Assert
        Assert.Equal((AliensSize, 0L, true), (details.QuarantinedBytes, details.FreesBytes, Assert.Single(details.Names).OtherNames));
    }

    [Fact]
    public async Task Get_of_a_replacement_the_mover_stopped_after_a_name_left_offers_retry_and_restore_but_neither_keep_both_nor_delete_for_good()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))],
            quarantineCheckedUtc: Now(),
            reason: HeldReason);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // MAM-27 — what is in the quarantine comes back through Restore, never by cancelling the replacement.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)HeldReason, (ReplacementStop?)ReplacementStop.Move, true, true, false, false),
            (details.State, details.Reason, details.Stop, details.CanRetry, details.CanRestore, details.CanCancel, details.CanDeleteForGood));
    }

    [Fact]
    public async Task Get_of_a_replacement_the_check_of_its_new_files_stopped_offers_retry_and_keep_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-25's review: no name moved, and the mover never looked at it.
        var startedUtc = Now().AddHours(-2);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview, [], reason: NotAlienReason, inspectionStartedUtc: startedUtc, libraryAskedUtc: startedUtc);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // MAM-27 — Retry runs the check again once the owner fixed the item in Jellyfin; Keep both cancels only the
        // replacement. Nothing is in the quarantine to restore or delete.
        Assert.Equal(
            ((ReplacementStop?)ReplacementStop.Check, true, true, false, false, 0L, 0L),
            (details.Stop, details.CanRetry, details.CanCancel, details.CanRestore, details.CanDeleteForGood, details.QuarantinedBytes, details.FreesBytes));
        // Stopped, it waits for nothing: when the check started and asked Jellyfin to look stay on the page.
        Assert.Equal(
            ((ReplacementCheckStage?)null, (DateTime?)null, (DateTime?)startedUtc, (DateTime?)startedUtc),
            (details.CheckStage, details.CheckWaitsUntilUtc, details.CheckStartedUtc, details.LibraryAskedUtc));
        Assert.Empty(details.Names);
    }

    [Fact]
    public async Task Get_of_a_replacement_whose_download_the_app_couldnt_follow_offers_keep_both_but_neither_retry_nor_drop()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The follower's review: the check of the new files never started, so there is nothing to check again.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: NeedsLinkingReason,
            copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1)), new PlantedCopy(AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize))]);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(
            ((ReplacementStop?)ReplacementStop.Download, (string?)NeedsLinkingReason, false, true),
            (details.Stop, details.Reason, details.CanRetry, details.CanCancel));
        Assert.Equal(
            [(ReplacementCopyStatus.NotPaired, false), (ReplacementCopyStatus.NotPaired, false)],
            details.Copies.Select(copy => (copy.Status, copy.CanDrop)));
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled, true)]
    [InlineData(ReplacementJobState.Submitted, true)]
    [InlineData(ReplacementJobState.Downloading, true)]
    [InlineData(ReplacementJobState.Paused, true)]
    [InlineData(ReplacementJobState.Downloaded, true)]
    [InlineData(ReplacementJobState.Inspecting, true)]
    [InlineData(ReplacementJobState.Verified, true)]
    [InlineData(ReplacementJobState.Quarantining, false)]
    [InlineData(ReplacementJobState.Quarantined, false)]
    [InlineData(ReplacementJobState.Restoring, false)]
    [InlineData(ReplacementJobState.Deleting, false)]
    [InlineData(ReplacementJobState.Restored, false)]
    [InlineData(ReplacementJobState.Completed, false)]
    [InlineData(ReplacementJobState.Failed, false)]
    [InlineData(ReplacementJobState.Cancelled, false)]
    public async Task Get_of_a_replacement_offers_keep_both_only_before_its_old_copies_move_and_never_retry_or_drop_outside_a_review(
        ReplacementJobState state, bool canCancel)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // No name of either copy left its folder: the state alone decides.
        var planted = await _planter.PlantAsync(
            state, [], copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1)), new PlantedCopy(AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize))]);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal((canCancel, false, (ReplacementStop?)null), (details.CanCancel, details.CanRetry, details.Stop));
        Assert.Equal([false, false], details.Copies.Select(copy => copy.CanDrop));
    }

    [Theory]
    [InlineData(ReplacementNameState.Planned, ReplacementNameState.Planned, ReplacementCopyStatus.NotPaired, false)]
    [InlineData(ReplacementNameState.Stayed, ReplacementNameState.Stayed, ReplacementCopyStatus.NotPaired, false)]
    // The video stayed, its subtitle moved: part of the copy left its folder all the same.
    [InlineData(ReplacementNameState.Stayed, ReplacementNameState.Quarantined, ReplacementCopyStatus.NotPaired, true)]
    [InlineData(ReplacementNameState.Moving, ReplacementNameState.Planned, ReplacementCopyStatus.Moving, true)]
    [InlineData(ReplacementNameState.Quarantined, ReplacementNameState.Quarantined, ReplacementCopyStatus.InQuarantine, true)]
    [InlineData(ReplacementNameState.Restoring, ReplacementNameState.Quarantined, ReplacementCopyStatus.Moving, true)]
    [InlineData(ReplacementNameState.Deleting, ReplacementNameState.Quarantined, ReplacementCopyStatus.Moving, true)]
    [InlineData(ReplacementNameState.Deleted, ReplacementNameState.Deleted, ReplacementCopyStatus.Deleted, true)]
    // Back where it was, or found nowhere: nothing of it is the quarantine's to give back.
    [InlineData(ReplacementNameState.Restored, ReplacementNameState.Restored, ReplacementCopyStatus.Restored, false)]
    [InlineData(ReplacementNameState.Lost, ReplacementNameState.Restored, ReplacementCopyStatus.Lost, false)]
    public async Task Get_of_a_move_stop_says_where_each_copys_video_is_and_offers_drop_and_keep_both_only_while_none_of_its_names_left(
        ReplacementNameState videoState, ReplacementNameState subtitleState, ReplacementCopyStatus status, bool leftItsFolder)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Alien's names as the theory gives them; Aliens' video held where it is by a torrent.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), videoState),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: SubtitleSize), subtitleState),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize), ReplacementNameState.Stayed, HeldProblem),
            ],
            quarantineCheckedUtc: Now(),
            reason: HeldReason);
        await StopCopyAsync(planted.Copies[1].Id, HeldReason, cancellationToken);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(((ReplacementStop?)ReplacementStop.Move, true, !leftItsFolder), (details.Stop, details.CanRetry, details.CanCancel));
        Assert.Equal(
            [(planted.Copies[0].Id, status, (string?)null, !leftItsFolder), (planted.Copies[1].Id, ReplacementCopyStatus.Stopped, HeldReason, true)],
            details.Copies.Select(copy => (copy.Id, copy.Status, copy.Problem, copy.CanDrop)));
    }

    [Fact]
    public async Task Get_of_a_check_stop_lists_every_copy_with_its_pair_problem_and_drop_and_withholds_retry_while_one_only_a_drop_settles()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddHours(-2);
        var droppedUtc = Now().AddMinutes(-5);
        // A season pack: episode 1 paired; episode 5 renumbered by Jellyfin and episode 9 covering two episodes, which no
        // fix of the library settles; episode 7 paired with a new file too short, and dropped since.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: RenumberedProblem,
            copies:
            [
                new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), PairedWithNewFile: 0, SeasonNumber: 2, EpisodeNumber: 1),
                new PlantedCopy(EpisodePath(5), TestMedia.File(inode: 15, sizeBytes: EpisodeSize), RenumberedProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 5),
                new PlantedCopy(
                    EpisodePath(7), TestMedia.File(inode: 17, sizeBytes: EpisodeSize), TooShortProblem, OnlyDropSettles: true, DroppedUtc: droppedUtc, PairedWithNewFile: 1,
                    SeasonNumber: 2, EpisodeNumber: 7),
                new PlantedCopy(EpisodePath(9), TestMedia.File(inode: 19, sizeBytes: EpisodeSize), CoversSeveralProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 9),
            ],
            newFiles: [IdentifiedEpisode(1, Guid.NewGuid(), startedUtc), IdentifiedEpisode(7, Guid.NewGuid(), startedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
        await _factory.WithDbAsync(db => db.ReplacementJobFiles
            .Where(copy => copy.Id == planted.Copies[3].Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.EpisodeNumberEnd, 10), cancellationToken));
        var before = await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // The dropped copy's problem is history: two copies still in it can only be dropped, so Retry would only stop again.
        Assert.Equal(
            ((ReplacementStop?)ReplacementStop.Check, 2, false, true),
            (details.Stop, details.DropOnlyCopyCount, details.CanRetry, details.CanCancel));
        Assert.Equal(
            [
                CopyOf(planted, 0, 2, 1, null, planted.NewFiles[0].Id, null, false, null, ReplacementCopyStatus.Paired, true),
                CopyOf(planted, 1, 2, 5, null, null, RenumberedProblem, true, null, ReplacementCopyStatus.Stopped, true),
                CopyOf(planted, 2, 2, 7, null, planted.NewFiles[1].Id, TooShortProblem, true, droppedUtc, ReplacementCopyStatus.Dropped, false),
                CopyOf(planted, 3, 2, 9, 10, null, CoversSeveralProblem, true, null, ReplacementCopyStatus.Stopped, true),
            ],
            details.Copies);
        Assert.Equal(before, await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken));
    }

    [Fact]
    public async Task Get_of_a_check_stop_whose_copy_only_a_drop_settles_was_dropped_offers_retry_and_never_the_drop_of_the_last_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddHours(-2);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: RenumberedProblem,
            copies:
            [
                new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), PairedWithNewFile: 0, SeasonNumber: 2, EpisodeNumber: 1),
                new PlantedCopy(
                    EpisodePath(5), TestMedia.File(inode: 15, sizeBytes: EpisodeSize), RenumberedProblem, OnlyDropSettles: true, DroppedUtc: Now(), SeasonNumber: 2, EpisodeNumber: 5),
            ],
            newFiles: [IdentifiedEpisode(1, Guid.NewGuid(), startedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // A new check can settle what is left; the copy left is the replacement's last, so it is Keep both, not Drop.
        Assert.Equal((0, true, true), (details.DropOnlyCopyCount, details.CanRetry, details.CanCancel));
        Assert.Equal(
            [(ReplacementCopyStatus.Paired, false), (ReplacementCopyStatus.Dropped, false)],
            details.Copies.Select(copy => (copy.Status, copy.CanDrop)));
    }

    [Fact]
    public async Task Get_of_a_dropped_copy_whose_new_file_a_later_check_deleted_shows_it_paired_with_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddHours(-2);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: TooShortProblem,
            copies:
            [
                new PlantedCopy(
                    EpisodePath(7), TestMedia.File(inode: 17, sizeBytes: EpisodeSize), TooShortProblem, OnlyDropSettles: true, DroppedUtc: Now(), SeasonNumber: 2, EpisodeNumber: 7),
                new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), PairedWithNewFile: 0, SeasonNumber: 2, EpisodeNumber: 1),
            ],
            newFiles: [IdentifiedEpisode(1, Guid.NewGuid(), startedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
        // The dropped copy keeps the pair of the check it was dropped after, whose new file rows the next check replaced.
        await _factory.WithDbAsync(db => db.ReplacementJobFiles
            .Where(copy => copy.Id == planted.Copies[0].Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.PairedNewFileId, Guid.NewGuid()), cancellationToken));

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(
            [((Guid?)null, ReplacementCopyStatus.Dropped), (planted.NewFiles[0].Id, ReplacementCopyStatus.Paired)],
            details.Copies.Select(copy => (copy.PairedNewFileId, copy.Status)));
    }

    [Fact]
    public async Task Get_of_a_replacement_the_owner_cancelled_shows_each_copy_staying_where_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Cancelled,
            [],
            reason: CancelledReason,
            copies: [new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1)), new PlantedCopy(AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize))]);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(((string?)CancelledReason, false, false), (details.Reason, details.CanCancel, details.CanRetry));
        Assert.Equal([ReplacementCopyStatus.Stays, ReplacementCopyStatus.Stays], details.Copies.Select(copy => copy.Status));
    }

    [Fact]
    public async Task Get_of_a_replacement_restored_after_its_move_stopped_part_way_shows_the_copies_the_move_never_reached_staying_where_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The mover moved Alien and its subtitle, couldn't move Aliens — a torrent holds it, which stopped the move — and
        // never reached Alien 3, still paired with its new file; the owner then restored what the quarantine held.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Restored,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1, sizeBytes: TheatricalSize), ReplacementNameState.Restored),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: SubtitleSize), ReplacementNameState.Restored),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Video, Alien3Path, TestMedia.File(inode: 5), ReplacementNameState.Planned),
            ],
            quarantineCheckedUtc: Now(),
            newFiles: [new ReplacementNewFileFacts(NewName, NewSize, 1.0, NewPath, CheckedUtc: Now())]);
        await _factory.WithDbAsync(async db =>
        {
            await db.ReplacementJobFiles
                .Where(copy => copy.Id == planted.Copies[1].Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.Problem, HeldReason), cancellationToken);
            await db.ReplacementJobFiles
                .Where(copy => copy.Id == planted.Copies[2].Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.PairedNewFileId, planted.NewFiles[0].Id), cancellationToken);
        });

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // The job has ended: nothing will move the copies it never moved, whatever the check and the move found of them.
        Assert.Equal((false, false, false, false), (details.CanRetry, details.CanCancel, details.CanRestore, details.CanDeleteForGood));
        Assert.Equal(
            [
                (ReplacementCopyStatus.Restored, (string?)null, (Guid?)null),
                (ReplacementCopyStatus.Stays, HeldReason, null),
                (ReplacementCopyStatus.Stays, null, planted.NewFiles[0].Id),
            ],
            details.Copies.Select(copy => (copy.Status, copy.Problem, copy.PairedNewFileId)));
    }

    // ── each reason the ticket names, as the trackers write it ──────────────────────────────────

    [Fact]
    public async Task Get_of_a_replacement_a_torrent_holds_the_old_copy_of_shows_the_copy_stopped_naming_the_torrent_with_its_pair_and_offers_retry_and_keep_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        var release = await _harness.FindAsync("Alien", 1979, ReplacementQuarantinePlanter.ReleaseTitle, imdbId: AlienImdbId);
        await _harness.VerifiedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
            [(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken())],
            paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId),
            cancellationToken);
        // The theatrical cut still seeds: the mover stops before anything moves (ADR 0031).
        _harness.QBittorrent.AddTorrent(TorrentHoldings.Holding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize));
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        var copies = await _harness.CopiesAsync(cancellationToken);
        var before = await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);
        var requestsBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count);

        //Act
        var details = await GetAsync(replacement.Id, cancellationToken);

        //Assert
        var newFile = Assert.Single(newFiles);
        var copy = Assert.Single(copies);
        // Retry once the torrent is removed in qBittorrent without its files; or Keep both, nothing having moved.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)TorrentHeldReason, (ReplacementStop?)ReplacementStop.Move, true, true, false, false),
            (details.State, details.Reason, details.Stop, details.CanRetry, details.CanCancel, details.CanRestore, details.CanDeleteForGood));
        Assert.Empty(details.Names);
        // The copy says which torrent in which client holds it, beside the new file it is paired with; the last copy is
        // never dropped.
        Assert.Equal(
            [
                new ReplacementJobCopyDto(
                    copy.Id, 0, TheatricalPath, TheatricalSize, alien.Id, null, null, null, "Theatrical Cut", TakenMilliseconds, newFile.Id,
                    TorrentHeldReason, false, null, ReplacementCopyStatus.Stopped, false),
            ],
            details.Copies);
        // What each check found of the new file: the client's word, the disk's and the media inspector's, Jellyfin's ids.
        var newFileRow = Assert.Single(details.NewFiles);
        Assert.Equal(
            new ReplacementJobNewFileDto(
                newFile.Id, 0, NewName, NewPath, NewSize, 1.0, NewSize, TakenMilliseconds, "h264", 1920, 1080, newFile.CheckedUtc, LibraryFileKind.Movie, null,
                null, null, newFile.ReadUtc, alien.Id, null, ReplacementNewFileProgress.Identified, newFileRow.Identifiers),
            newFileRow);
        Assert.Equal([new ReplacementJobIdentifierDto(MediaIdentifierScheme.Imdb, AlienImdbId, false, false)], newFileRow.Identifiers);
        // The work it was expected to be, matched by its IMDb id: no confirmation needed.
        var work = Assert.Single(details.Works);
        Assert.Equal((alien.Id, MediaWorkKind.Movie, (string?)"Alien", (int?)1979, false), (work.WorkId, work.Kind, work.Title, work.Year, work.ConfirmedByOwner));
        Assert.Equal([new ReplacementJobIdentifierDto(MediaIdentifierScheme.Imdb, AlienImdbId, false, false)], work.Identifiers);
        // A read of the rows: nothing written, neither client asked.
        Assert.Equal(before, await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken));
        Assert.Equal(requestsBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count));
    }

    [Fact]
    public async Task Get_while_jellyfin_gives_the_new_file_no_id_shows_it_waiting_until_the_hour_runs_out_then_the_check_stopped_with_retry()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        var release = await _harness.FindAsync("Alien", 1979, ReplacementQuarantinePlanter.ReleaseTitle, imdbId: AlienImdbId);
        await _harness.DownloadedAsync(release, _harness.Movies, [TheatricalPath], [new FakeQBittorrentFile(NewName, NewSize, 1.0)], cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Read and refreshed, but no provider found the film: no TMDb or IMDb id to tell it by.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien.1979.Directors.Cut.2160p", 1979);
        var askedUtc = Now();
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        var id = (await _harness.ReplacementAsync(cancellationToken)).Id;

        //Act
        var waiting = await GetAsync(id, cancellationToken);
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait);
        await _harness.VerifyAsync(cancellationToken);
        var stopped = await GetAsync(id, cancellationToken, await FreshSessionAsync());

        //Assert
        // While the hour runs: Jellyfin's stage, the time it waits until, the file listed and read with no id yet, and the
        // copy kept until it is verified. Nothing to retry or drop yet; Keep both is offered.
        Assert.Equal(
            (ReplacementJobState.Inspecting, (ReplacementStop?)null, (ReplacementCheckStage?)ReplacementCheckStage.Library, (DateTime?)askedUtc,
                (DateTime?)askedUtc, (DateTime?)(askedUtc + ReplacementVerificationRules.LibraryWait), false, true),
            (waiting.State, waiting.Stop, waiting.CheckStage, waiting.CheckStartedUtc, waiting.LibraryAskedUtc, waiting.CheckWaitsUntilUtc, waiting.CanRetry,
                waiting.CanCancel));
        var waitingFile = Assert.Single(waiting.NewFiles);
        Assert.Equal(
            ((LibraryFileKind?)LibraryFileKind.Movie, (Guid?)null, (string?)null, ReplacementNewFileProgress.NoId),
            (waitingFile.Kind, waitingFile.WorkId, waitingFile.Problem, waitingFile.Progress));
        Assert.Empty(waitingFile.Identifiers);
        Assert.Equal([(ReplacementCopyStatus.Waiting, (string?)null, false)], waiting.Copies.Select(copy => (copy.Status, copy.Problem, copy.CanDrop)));

        // Once it ran out: the check's stop with the reason on the file, which Retry checks again once the owner fixed the
        // item in Jellyfin; the copy found no pair and stopped nothing itself.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)NoIdReason, (ReplacementStop?)ReplacementStop.Check, (ReplacementCheckStage?)null, (DateTime?)null, true, true, 0),
            (stopped.State, stopped.Reason, stopped.Stop, stopped.CheckStage, stopped.CheckWaitsUntilUtc, stopped.CanRetry, stopped.CanCancel, stopped.DropOnlyCopyCount));
        Assert.Equal(
            [((string?)NoIdReason, ReplacementNewFileProgress.Stopped)],
            stopped.NewFiles.Select(file => (file.Problem, file.Progress)));
        Assert.Equal(
            [(ReplacementCopyStatus.NotPaired, (Guid?)null, (string?)null, false)],
            stopped.Copies.Select(copy => (copy.Status, copy.PairedNewFileId, copy.Problem, copy.CanDrop)));
    }

    [Fact]
    public async Task Get_of_a_replacement_whose_two_new_files_are_the_same_film_shows_the_unclear_pair_on_the_copy_and_offers_retry()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        var release = await _harness.FindAsync("Alien", 1979, ReplacementQuarantinePlanter.ReleaseTitle, imdbId: AlienImdbId);
        // A multi-file torrent: both of its films are Alien to Jellyfin.
        await _harness.DownloadedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [
                new FakeQBittorrentFile($"{PackFolder}/Alien.1979.Directors.Cut.2160p.mkv", NewSize, 1.0),
                new FakeQBittorrentFile($"{PackFolder}/Alien.1979.Directors.Cut.1080p.mkv", NewSize, 1.0),
            ],
            cancellationToken);
        _factory.Files.AddFile(PackUhdPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _factory.Files.AddFile(PackHdPath, TestMedia.File(inode: 8, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            foreach (var path in paths)
                _harness.JellyfinServer.PlantFilm(path, "Alien", 1979, imdbId: AlienImdbId);
        };
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        var details = await GetAsync((await _harness.ReplacementAsync(cancellationToken)).Id, cancellationToken);

        //Assert
        // Settled through the library — one of the two items corrected, then Retry — or by Keep both; never by a hand
        // confirmation. Not one only a drop settles.
        Assert.Equal(
            ((string?)TwoNewFilesReason, (ReplacementStop?)ReplacementStop.Check, true, true, 0),
            (details.Reason, details.Stop, details.CanRetry, details.CanCancel, details.DropOnlyCopyCount));
        Assert.Equal(
            [(ReplacementCopyStatus.Stopped, (string?)TwoNewFilesReason, (Guid?)null, false, false)],
            details.Copies.Select(copy => (copy.Status, copy.Problem, copy.PairedNewFileId, copy.OnlyDropSettles, copy.CanDrop)));
        Assert.Equal(
            [((string?)PackUhdPath, (Guid?)alien.Id, ReplacementNewFileProgress.Identified), (PackHdPath, alien.Id, ReplacementNewFileProgress.Identified)],
            details.NewFiles.Select(file => (file.AppPath, file.WorkId, file.Progress)));
    }

    [Fact]
    public async Task Get_of_a_verified_replacement_while_replacement_is_switched_off_offers_keep_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddMinutes(-10);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Verified,
            [],
            copies: [new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), PairedWithNewFile: 0, SeasonNumber: 2, EpisodeNumber: 1)],
            newFiles: [IdentifiedEpisode(1, Guid.NewGuid(), startedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
        await SwitchOffAsync(cancellationToken);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // Nothing moves while it is off; Keep both writes nothing to the media, so it is offered all the same.
        Assert.Equal((true, true, false, (ReplacementStop?)null), (details.SwitchedOff, details.CanCancel, details.CanRetry, details.Stop));
        Assert.Equal([(ReplacementCopyStatus.Paired, false)], details.Copies.Select(copy => (copy.Status, copy.CanDrop)));
    }

    [Fact]
    public async Task Get_of_a_check_stop_while_replacement_is_switched_off_still_offers_retry_and_keep_both()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddHours(-2);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview, [], reason: NotAlienReason, inspectionStartedUtc: startedUtc, libraryAskedUtc: startedUtc);
        await SwitchOffAsync(cancellationToken);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // A check's Retry writes nothing to the media either.
        Assert.Equal((true, true, true), (details.SwitchedOff, details.CanRetry, details.CanCancel));
    }

    [Fact]
    public async Task Get_while_replacement_is_switched_off_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined, [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))]);
        await SwitchOffAsync(cancellationToken);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // Still offered — the page disables them, saying why — as the actions refuse while it is off.
        Assert.Equal((true, true, true), (details.SwitchedOff, details.CanRestore, details.CanDeleteForGood));
    }

    // ── the new files and the works ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_while_the_check_waits_for_the_library_says_how_far_it_got_with_each_new_file_and_until_when_it_waits()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var askedUtc = Now().AddMinutes(-20);
        var readUtc = Now().AddSeconds(-15);
        var seriesId = Guid.NewGuid();
        // Each new file of the pack a step further than the one before, as a check's evidence leaves it; the first with a
        // problem a pass found, which stops nothing while the check still waits.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Inspecting,
            [],
            copies: [new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), SeasonNumber: 2, EpisodeNumber: 1)],
            newFiles:
            [
                new ReplacementNewFileFacts(PackName(1), EpisodeSize, 1.0, PackPath(1), CheckedUtc: askedUtc, Problem: ChangedProblem),
                new ReplacementNewFileFacts(PackName(2), EpisodeSize, 0.5),
                new ReplacementNewFileFacts(PackName(3), EpisodeSize, 1.0),
                new ReplacementNewFileFacts(PackName(4), EpisodeSize, 1.0, PackPath(4), CheckedUtc: askedUtc),
                new ReplacementNewFileFacts(PackName(5), EpisodeSize, 1.0, PackPath(5), CheckedUtc: askedUtc, ReadUtc: readUtc),
                new ReplacementNewFileFacts(
                    PackName(6), EpisodeSize, 1.0, PackPath(6), CheckedUtc: askedUtc, LibraryItemId: "item-6", Kind: LibraryFileKind.Episode, ReadUtc: readUtc),
                new ReplacementNewFileFacts(
                    PackName(7), EpisodeSize, 1.0, PackPath(7), CheckedUtc: askedUtc, LibraryItemId: "item-7", Kind: LibraryFileKind.Other, Refreshed: true, ReadUtc: readUtc),
                new ReplacementNewFileFacts(
                    PackName(8), EpisodeSize, 1.0, PackPath(8), CheckedUtc: askedUtc, LibraryItemId: "item-8", Kind: LibraryFileKind.Movie, Refreshed: true, ReadUtc: readUtc),
                new ReplacementNewFileFacts(
                    PackName(9), EpisodeSize, 1.0, PackPath(9), CheckedUtc: askedUtc, LibraryItemId: "item-9", Kind: LibraryFileKind.Episode, Refreshed: true, SeasonNumber: 2,
                    ReadUtc: readUtc, MediaWorkId: seriesId),
                new ReplacementNewFileFacts(
                    PackName(10),
                    EpisodeSize,
                    1.0,
                    PackPath(10),
                    TestMedia.File(inode: 30, sizeBytes: EpisodeSize),
                    EpisodeMilliseconds,
                    "hevc",
                    1920,
                    1080,
                    askedUtc,
                    "item-10",
                    LibraryFileKind.Episode,
                    Refreshed: true,
                    SeasonNumber: 2,
                    EpisodeNumber: 10,
                    EpisodeNumberEnd: 11,
                    ReadUtc: readUtc,
                    MediaWorkId: seriesId,
                    // Out of order: the page gets the item's own ids first, then its series'.
                    Identifiers:
                    [
                        (MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId, true),
                        (MediaIdentifierScheme.Tvdb, "7654321", false),
                        (MediaIdentifierScheme.Imdb, YellowstoneImdbId, true),
                    ]),
            ],
            inspectionStartedUtc: askedUtc,
            libraryAskedUtc: askedUtc);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(
            ((ReplacementCheckStage?)ReplacementCheckStage.Library, (DateTime?)askedUtc, (DateTime?)askedUtc, (DateTime?)(askedUtc + ReplacementVerificationRules.LibraryWait)),
            (details.CheckStage, details.CheckStartedUtc, details.LibraryAskedUtc, details.CheckWaitsUntilUtc));
        Assert.Equal(
            [
                // Its problem is sent beside how far the check got with it: the replacement didn't stop.
                ReplacementNewFileProgress.AskedToLook,
                ReplacementNewFileProgress.Downloading,
                ReplacementNewFileProgress.NotChecked,
                ReplacementNewFileProgress.AskedToLook,
                ReplacementNewFileProgress.NotListed,
                ReplacementNewFileProgress.NotRead,
                ReplacementNewFileProgress.Extra,
                ReplacementNewFileProgress.NoId,
                ReplacementNewFileProgress.NotNumbered,
                ReplacementNewFileProgress.Identified,
            ],
            details.NewFiles.Select(file => file.Progress));
        Assert.Equal(((string?)ChangedProblem, (string?)PackPath(1)), (details.NewFiles[0].Problem, details.NewFiles[0].AppPath));
        var identified = details.NewFiles[9];
        Assert.Equal(
            new ReplacementJobNewFileDto(
                planted.NewFiles[9].Id, 9, PackName(10), PackPath(10), EpisodeSize, 1.0, EpisodeSize, EpisodeMilliseconds, "hevc", 1920, 1080, askedUtc,
                LibraryFileKind.Episode, 2, 10, 11, readUtc, seriesId, null, ReplacementNewFileProgress.Identified, identified.Identifiers),
            identified);
        Assert.Equal(
            [
                new ReplacementJobIdentifierDto(MediaIdentifierScheme.Tvdb, "7654321", false, false),
                new ReplacementJobIdentifierDto(MediaIdentifierScheme.Imdb, YellowstoneImdbId, true, false),
                new ReplacementJobIdentifierDto(MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId, true, false),
            ],
            identified.Identifiers);
        Assert.Equal([ReplacementCopyStatus.Waiting], details.Copies.Select(copy => copy.Status));
    }

    [Fact]
    public async Task Get_while_the_check_waits_on_another_file_sends_what_a_pass_found_without_saying_it_stopped_the_replacement()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var askedUtc = Now().AddMinutes(-20);
        var seriesId = Guid.NewGuid();
        // As a pass leaves a season pack while Jellyfin hasn't listed episode 1's new file yet: episode 7's new file runs
        // too short — a problem only a drop settles —, and Jellyfin placed episode 8's in another series; no copy is paired
        // until the job is decided.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Inspecting,
            [],
            copies:
            [
                new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), SeasonNumber: 2, EpisodeNumber: 1),
                new PlantedCopy(EpisodePath(7), TestMedia.File(inode: 17, sizeBytes: EpisodeSize), TooShortProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 7),
            ],
            newFiles:
            [
                new ReplacementNewFileFacts(PackName(1), EpisodeSize, 1.0, PackPath(1), CheckedUtc: askedUtc, ReadUtc: askedUtc.AddMinutes(1)),
                IdentifiedEpisode(7, seriesId, askedUtc),
                IdentifiedEpisode(8, seriesId, askedUtc) with { MediaWorkId = null, Problem = OtherSeriesProblem },
            ],
            inspectionStartedUtc: askedUtc,
            libraryAskedUtc: askedUtc);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        // The check still waits for Jellyfin: nothing stopped the replacement, so of the owner's actions only Keep both is
        // offered.
        Assert.Equal(
            ((ReplacementStop?)null, (ReplacementCheckStage?)ReplacementCheckStage.Library, (DateTime?)(askedUtc + ReplacementVerificationRules.LibraryWait), false, true),
            (details.Stop, details.CheckStage, details.CheckWaitsUntilUtc, details.CanRetry, details.CanCancel));
        // Each copy waits, with the problem found sent beside it; what settles it is said once the job stops for review.
        Assert.Equal(
            [
                CopyOf(planted, 0, 2, 1, null, null, null, false, null, ReplacementCopyStatus.Waiting, false),
                CopyOf(planted, 1, 2, 7, null, null, TooShortProblem, false, null, ReplacementCopyStatus.Waiting, false),
            ],
            details.Copies);
        // Each new file says how far the check got with it, with its problem sent beside it.
        Assert.Equal(
            [
                (ReplacementNewFileProgress.NotListed, (string?)null),
                (ReplacementNewFileProgress.Identified, null),
                (ReplacementNewFileProgress.NoId, OtherSeriesProblem),
            ],
            details.NewFiles.Select(file => (file.Progress, file.Problem)));
    }

    [Fact]
    public async Task Get_while_the_check_reads_the_new_files_on_the_disk_says_it_waits_an_hour_from_its_start()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddMinutes(-10);
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Inspecting,
            [],
            copies: [new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), SeasonNumber: 2, EpisodeNumber: 1)],
            newFiles:
            [
                new ReplacementNewFileFacts(PackName(1), EpisodeSize, 1.0, PackPath(1), CheckedUtc: startedUtc),
                new ReplacementNewFileFacts(PackName(2), EpisodeSize, 0.25),
            ],
            inspectionStartedUtc: startedUtc);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal(
            ((ReplacementCheckStage?)ReplacementCheckStage.Files, (DateTime?)startedUtc, (DateTime?)null, (DateTime?)(startedUtc + ReplacementVerificationRules.FilesWait)),
            (details.CheckStage, details.CheckStartedUtc, details.LibraryAskedUtc, details.CheckWaitsUntilUtc));
        Assert.Equal([ReplacementNewFileProgress.Checked, ReplacementNewFileProgress.Downloading], details.NewFiles.Select(file => file.Progress));
    }

    [Fact]
    public async Task Get_lists_the_works_the_new_files_are_expected_to_be_with_their_ids_each_in_the_place_of_its_first_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A collection: Aliens ticked first, then Alien, which matched by title and year only and was confirmed by the owner,
        // whose own TMDb link counts as its id.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Downloading,
            [],
            copies: [new PlantedCopy(AliensPath, TestMedia.File(inode: 4, sizeBytes: AliensSize)), new PlantedCopy(TheatricalPath, TestMedia.File(inode: 1))]);
        var aliensId = planted.Copies[0].MatchedWorkId;
        var alienId = planted.Copies[1].MatchedWorkId;
        await Replacements().AddWork(
            planted.Replacement.Id,
            alienId,
            MediaWorkKind.Movie,
            "Alien",
            1979,
            MatchEvidence.TitleAndYear,
            confirmedByOwner: true,
            "This release is Alien (1979)",
            [(MediaIdentifierScheme.Tmdb, AlienTmdbId, true), (MediaIdentifierScheme.Imdb, AlienImdbId, false)]);
        await Replacements().AddWork(
            planted.Replacement.Id,
            aliensId,
            MediaWorkKind.Movie,
            "Aliens",
            1986,
            MatchEvidence.Identifier,
            confirmedByOwner: false,
            "This release is Aliens (1986)",
            [(MediaIdentifierScheme.Imdb, AliensImdbId, false)]);

        //Act
        var details = await GetAsync(planted.Replacement.Id, cancellationToken);

        //Assert
        Assert.Equal([aliensId, alienId], details.Copies.Select(copy => copy.WorkId));
        Assert.Equal(
            [(aliensId, MediaWorkKind.Movie, (string?)"Aliens", (int?)1986, false), (alienId, MediaWorkKind.Movie, "Alien", 1979, true)],
            details.Works.Select(work => (work.WorkId, work.Kind, work.Title, work.Year, work.ConfirmedByOwner)));
        Assert.Equal([new ReplacementJobIdentifierDto(MediaIdentifierScheme.Imdb, AliensImdbId, false, false)], details.Works[0].Identifiers);
        Assert.Equal(
            [
                new ReplacementJobIdentifierDto(MediaIdentifierScheme.Imdb, AlienImdbId, false, false),
                new ReplacementJobIdentifierDto(MediaIdentifierScheme.Tmdb, AlienTmdbId, false, true),
            ],
            details.Works[1].Identifiers);
    }

    [Fact]
    public async Task Get_sends_the_copies_new_files_works_and_ids_by_the_names_the_page_reads_every_enum_by_name_and_every_time_in_utc()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var startedUtc = Now().AddHours(-2);
        var seriesId = Guid.NewGuid();
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [],
            reason: RenumberedProblem,
            copies:
            [
                new PlantedCopy(EpisodePath(1), TestMedia.File(inode: 11, sizeBytes: EpisodeSize), PairedWithNewFile: 0, SeasonNumber: 2, EpisodeNumber: 1),
                new PlantedCopy(EpisodePath(5), TestMedia.File(inode: 15, sizeBytes: EpisodeSize), RenumberedProblem, OnlyDropSettles: true, SeasonNumber: 2, EpisodeNumber: 5),
            ],
            newFiles: [IdentifiedEpisode(1, seriesId, startedUtc)],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
        await Replacements().AddWork(
            planted.Replacement.Id,
            seriesId,
            MediaWorkKind.Series,
            "Yellowstone",
            2018,
            MatchEvidence.Identifier,
            confirmedByOwner: false,
            "This release is Yellowstone (2018)",
            [(MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId, false)]);

        //Act
        using var response = await _factory.GetHttpRequest($"{BaseUrl}/{planted.Replacement.Id}", _harness.Owner.AccessToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        //Assert
        // Exactly the names the job's page reads (frontend ReplacementDetails, ReplacementJobCopy, ReplacementJobNewFile,
        // ReplacementJobWork and ReplacementJobIdentifier).
        var root = document.RootElement;
        var copy = root.GetProperty("copies")[0];
        var newFile = root.GetProperty("newFiles")[0];
        var work = root.GetProperty("works")[0];
        Assert.Equal(
            [
                "id", "downloadJobId", "state", "reason", "switchedOff", "canRetry", "canRestore", "canDeleteForGood", "quarantinedBytes", "freesBytes", "names",
                "canCancel", "stop", "checkStage", "checkStartedUtc", "libraryAskedUtc", "checkWaitsUntilUtc", "dropOnlyCopyCount", "copies", "newFiles", "works",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "id", "position", "appPath", "sizeBytes", "workId", "seasonNumber", "episodeNumber", "episodeNumberEnd", "editionLabel", "durationMilliseconds",
                "pairedNewFileId", "problem", "onlyDropSettles", "droppedUtc", "status", "canDrop",
            ],
            copy.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "id", "position", "relativePath", "appPath", "clientSizeBytes", "clientProgress", "sizeBytes", "durationMilliseconds", "videoCodec", "width",
                "height", "checkedUtc", "kind", "seasonNumber", "episodeNumber", "episodeNumberEnd", "readUtc", "workId", "problem", "progress", "identifiers",
            ],
            newFile.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["workId", "kind", "title", "year", "confirmedByOwner", "identifiers"], work.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["scheme", "value", "ofSeries", "setByOwner"], newFile.GetProperty("identifiers")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(["scheme", "value", "ofSeries", "setByOwner"], work.GetProperty("identifiers")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ("Check", "Paired", "Stopped", "Episode", "Identified", "Tvdb", "Series", "TmdbTv"),
            (root.GetProperty("stop").GetString(), copy.GetProperty("status").GetString(), root.GetProperty("copies")[1].GetProperty("status").GetString(),
                newFile.GetProperty("kind").GetString(), newFile.GetProperty("progress").GetString(),
                newFile.GetProperty("identifiers")[0].GetProperty("scheme").GetString(), work.GetProperty("kind").GetString(),
                work.GetProperty("identifiers")[0].GetProperty("scheme").GetString()));
        // Instants carry their zone, so the browser never reads them as its own local time.
        Assert.All(
            new[] { root.GetProperty("checkStartedUtc"), root.GetProperty("libraryAskedUtc"), newFile.GetProperty("checkedUtc"), newFile.GetProperty("readUtc") },
            time => Assert.EndsWith("Z", time.GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_reads_each_table_once_however_many_copies_new_files_and_works_the_replacement_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var small = await PlantHeldSeasonAsync("Small", episodes: 1, works: 1, firstInode: 100);
        var large = await PlantHeldSeasonAsync("Large", episodes: 4, works: 2, firstInode: 200);
        var commands = new ConcurrentQueue<string>();
        // Records every command and lets each through: the hook matches nothing, so it never disarms.
        _factory.DbCommandHook.BeforeNextCommand(
            command =>
            {
                commands.Enqueue(command.CommandText);
                return false;
            },
            _ => Task.CompletedTask);
        // Each read once first, so nothing a first request fills can pass for a difference between the two.
        await GetAsync(small.Replacement.Id, cancellationToken);
        await GetAsync(large.Replacement.Id, cancellationToken);

        //Act
        var start = commands.Count;
        await GetAsync(small.Replacement.Id, cancellationToken);
        var middle = commands.Count;
        await GetAsync(large.Replacement.Id, cancellationToken);
        var readsOfLarge = commands.Skip(middle).ToList();

        //Assert
        // As many reads for four copies, four new files and two works as for one of each: one per table.
        Assert.Equal(middle - start, readsOfLarge.Count);
        Assert.Equal(
            [1, 1, 1, 1, 1, 1],
            new[]
                {
                    "ReplacementJobNames", "ReplacementJobFiles", "ReplacementJobNewFiles", "ReplacementJobNewFileIdentifiers", "ReplacementJobWorks",
                    "ReplacementJobWorkIdentifiers",
                }
                .Select(table => readsOfLarge.Count(command => command.Contains($"FROM \"{table}\"", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("an unknown replacement")]
    [InlineData("another user's replacement")]
    public async Task Get_of_an_unknown_or_another_users_replacement_is_a_bare_404(string which)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1))],
            requestedByUserId: member.User.Id);
        var id = which == "an unknown replacement" ? Guid.NewGuid() : planted.Replacement.Id;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{id}", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── the Jobs page's line ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Jobs_list_carries_each_replacements_id_quarantined_copies_stayed_files_and_retry_from_one_read_of_the_names_and_one_of_the_copies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two copies in the quarantine, one companion held where it is.
        var quarantined = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, NfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4)),
            ]);
        // The mover stopped over the video before anything moved: the companion it left is no news of a move.
        var stopped = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, "/media/movies/Heat (1995)/Heat (1995).mkv", TestMedia.File(inode: 5), ReplacementNameState.Stayed, HeldProblem),
                new PlantedName(ReplacementNameKind.Companion, "/media/movies/Heat (1995)/Heat (1995).srt", TestMedia.File(inode: 6), ReplacementNameState.Stayed, HeldProblem),
            ],
            quarantineCheckedUtc: Now(),
            reason: HeldReason);
        // Not moved yet: no name at all.
        var verified = await _planter.PlantAsync(ReplacementJobState.Verified, []);
        var namesRead = 0;
        var copiesRead = 0;
        // Counts every read of the names and of the copies and lets each through: the hook matches nothing, so it never
        // disarms.
        _factory.DbCommandHook.BeforeNextCommand(
            command =>
            {
                if (command.CommandText.Contains("FROM \"ReplacementJobNames\"", StringComparison.Ordinal))
                    Interlocked.Increment(ref namesRead);
                if (command.CommandText.Contains("FROM \"ReplacementJobFiles\"", StringComparison.Ordinal))
                    Interlocked.Increment(ref copiesRead);
                return false;
            },
            _ => Task.CompletedTask);

        //Act
        var response = await _factory.GetHttpRequest(DownloadsUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var lines = list.Items.ToDictionary(job => job.Id, job => job.Replacement!);
        Assert.Equal(
            [
                (quarantined.Replacement.Id, ReplacementJobState.Quarantined, 2, 1, false),
                (stopped.Replacement.Id, ReplacementJobState.NeedsReview, 0, 0, true),
                (verified.Replacement.Id, ReplacementJobState.Verified, 0, 0, false),
            ],
            new[] { quarantined, stopped, verified }.Select(planted => lines[planted.Download.Id])
                .Select(line => (line.Id, line.State, line.QuarantinedCopyCount, line.StayedFileCount, line.CanRetry)));
        // MAM-24, R10 — batched for the page, never one read per job; MAM-27 — the copies too, which the line now counts by
        // their drops.
        Assert.Equal((1, 1), (namesRead, copiesRead));
    }

    [Fact]
    public async Task Jobs_read_of_one_job_carries_the_same_line()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, NfoPath, TestMedia.File(inode: 3, sizeBytes: NfoSize), ReplacementNameState.Stayed, HeldProblem),
            ]);

        //Act
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Quarantined, 1, null, false, 0)
            {
                Id = planted.Replacement.Id,
                QuarantinedCopyCount = 1,
                StayedFileCount = 1,
                CanRetry = false,
                // MAM-41 — as the replacement's own page offers them.
                CanRestore = true,
                CanDeleteForGood = true,
            },
            line);
    }

    [Fact]
    public async Task Jobs_line_counts_only_the_copies_still_to_move_among_those_no_longer_in_a_ticked_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Retried after a stop over Aliens, then both folders unticked: Alien's video is in the quarantine already, which
        // check 5 passes over, so only Aliens' folder stops the move now.
        var planted = await _planter.PlantAsync(
            ReplacementJobState.Verified,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Video, AliensPath, TestMedia.File(inode: 4), ReplacementNameState.Stayed, "The app may not write there."),
            ],
            quarantineCheckedUtc: Now());
        await _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken));

        //Act
        var line = (await _harness.LineAsync(planted.Download.Id, cancellationToken))!;

        //Assert
        Assert.Equal((2, 1, 1), (line.CopyCount, line.CopiesOutsideTickedFolders, line.QuarantinedCopyCount));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private DateTime Now() => _factory.Time.GetUtcNow().UtcDateTime;

    private static string QuarantinePathOf(Guid replacementJobId, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacementJobId, appPath);

    /// <summary>Yellowstone's episode <paramref name="episode"/> of season 2 in the library, the app's path.</summary>
    private static string EpisodePath(int episode) => $"{SeasonFolder}/Yellowstone S02E{episode:00}.mkv";

    /// <summary>The season pack's episode <paramref name="episode"/> as qBittorrent names it.</summary>
    private static string PackName(int episode) => $"Yellowstone.S02.1080p/Yellowstone.S02E{episode:00}.1080p.mkv";

    /// <summary>The season pack's episode <paramref name="episode"/>, the app's path.</summary>
    private static string PackPath(int episode) => $"{PackSeasonFolder}/Yellowstone.S02E{episode:00}.1080p.mkv";

    /// <summary>
    /// The pack's episode <paramref name="episode"/> as a check that identified it left it: downloaded, read on the disk
    /// when it started (<paramref name="checkedUtc"/>), listed, read and numbered by the library a minute later, and an
    /// episode of <paramref name="seriesId"/> by its series' TMDb id.
    /// </summary>
    private static ReplacementNewFileFacts IdentifiedEpisode(int episode, Guid seriesId, DateTime checkedUtc) =>
        new(
            PackName(episode),
            EpisodeSize,
            1.0,
            PackPath(episode),
            CheckedUtc: checkedUtc,
            LibraryItemId: $"item-{episode}",
            Kind: LibraryFileKind.Episode,
            Refreshed: true,
            SeasonNumber: 2,
            EpisodeNumber: episode,
            ReadUtc: checkedUtc.AddMinutes(1),
            MediaWorkId: seriesId,
            Identifiers: [(MediaIdentifierScheme.Tvdb, $"76543{episode:00}", false), (MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId, true)]);

    /// <summary>
    /// The copy at <paramref name="position"/> of <paramref name="planted"/> as the page should list it: its row's own
    /// facts, an episode of season <paramref name="season"/> from <paramref name="episode"/> to
    /// <paramref name="episodeEnd"/>, and the rest as given.
    /// </summary>
    private static ReplacementJobCopyDto CopyOf(
        PlantedReplacement planted,
        int position,
        int season,
        int episode,
        int? episodeEnd,
        Guid? pairedNewFileId,
        string? problem,
        bool onlyDropSettles,
        DateTime? droppedUtc,
        ReplacementCopyStatus status,
        bool canDrop)
    {
        var copy = planted.Copies[position];
        return new ReplacementJobCopyDto(
            copy.Id, position, copy.AppPath, copy.SizeBytes, copy.MatchedWorkId, season, episode, episodeEnd, null, null, pairedNewFileId, problem, onlyDropSettles,
            droppedUtc, status, canDrop);
    }

    /// <summary>Alien's theatrical cut, inspected, in the ticked Фільми: what the owner replaces through the real route.</summary>
    private async Task<MediaWorkEntity> PlantAlienAsync(CancellationToken cancellationToken)
    {
        var alien = await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        await _harness.InspectAsync(cancellationToken);
        return alien;
    }

    /// <summary>
    /// A replacement of <paramref name="episodes"/> episodes of the series <paramref name="title"/> the mover stopped, each
    /// copy's video held where it is (its file's inode from <paramref name="firstInode"/> on), each paired by the check with a
    /// new file it identified, and <paramref name="works"/> works with two ids each.
    /// </summary>
    private async Task<PlantedReplacement> PlantHeldSeasonAsync(string title, int episodes, int works, long firstInode)
    {
        var startedUtc = Now().AddHours(-2);
        var seriesId = Guid.NewGuid();
        var planted = await _planter.PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                .. Enumerable.Range(1, episodes).Select(episode => new PlantedName(
                    ReplacementNameKind.Video,
                    $"/media/tv-shows/{title}/Season 02/{title} S02E{episode:00}.mkv",
                    TestMedia.File(inode: firstInode + episode),
                    ReplacementNameState.Stayed,
                    HeldProblem)),
            ],
            quarantineCheckedUtc: Now(),
            reason: HeldReason,
            newFiles: [.. Enumerable.Range(1, episodes).Select(episode => IdentifiedEpisode(episode, seriesId, startedUtc))],
            inspectionStartedUtc: startedUtc,
            libraryAskedUtc: startedUtc);
        foreach (var work in Enumerable.Range(1, works))
        {
            await Replacements().AddWork(
                planted.Replacement.Id,
                work == 1 ? seriesId : Guid.NewGuid(),
                MediaWorkKind.Series,
                $"{title} {work}",
                2018,
                MatchEvidence.Identifier,
                confirmedByOwner: false,
                $"This release is {title} {work} (2018)",
                [(MediaIdentifierScheme.TmdbTv, $"{work}", false), (MediaIdentifierScheme.Imdb, $"tt{work:0000000}", false)]);
        }

        return planted;
    }

    private IReplacementJobDataFactory Replacements() =>
        new ReplacementJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    /// <summary>The copy <paramref name="copyId"/> stopped its job with <paramref name="problem"/>, as the mover's check writes
    /// it beside the job's reason.</summary>
    private Task StopCopyAsync(Guid copyId, string problem, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.ReplacementJobFiles
            .Where(copy => copy.Id == copyId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.Problem, problem), cancellationToken));

    private Task SwitchOffAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

    /// <summary>A session of the owner signed in now: a test that ran the clock past an hour outlived the one it started
    /// with.</summary>
    private async Task<string> FreshSessionAsync() => (await new UserSeeder(_factory.Scope).CreateSession(_harness.Owner.User)).AccessToken;

    private async Task<ReplacementDetailsDto> GetAsync(Guid replacementJobId, CancellationToken cancellationToken, string? accessToken = null)
    {
        using var response = await _factory.GetHttpRequest($"{BaseUrl}/{replacementJobId}", accessToken ?? _harness.Owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReplacementDetailsDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
