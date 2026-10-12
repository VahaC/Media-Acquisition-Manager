using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0031 — check 6, right before the move: one read of every download client's torrents with their files
// (qBittorrent's GET torrents/info?includeFiles=true, nothing else sent to it, nothing written), for every name the move
// would rename. A video any torrent lists — seeding, queued or stopped — stops the job for review naming the torrent, each
// of several once, until the owner removes it in qBittorrent without its files and presses Retry; a torrent's file the
// app can't place (a save path no pair maps), a client that doesn't answer, one switched off and a qBittorrent too old to
// list files are uncertain and stop it the same way. A companion a torrent lists stays where it is, named on the job, and
// its video moves; a second name of a torrent's file moves, its row saying another name keeps the data. Once the torrent
// is gone and the job is retried, the next pass moves it. MAM-28 — a client the app sent a replacement to, this one's or an
// earlier one's, is asked whatever its path mappings say: once its mapping is deleted or pointed outside the Media folders,
// a torrent listing there a file of a name the move would rename (in any letter case), of its size or of no size stops the
// job the same way, counted once per torrent, the sentence naming those mappings after any torrent it couldn't place; one
// listing nothing like it doesn't, nor one listing a file only of the size of a video with several names, the torrent's
// own name of it — its hardlink twin outside the Media folders —, the link count read again even of subtitles an earlier
// run left waiting. A seedbox the app never sent a replacement to — sent
// nothing, a download that replaced nothing, a replacement it never took, or one through a client deleted since —, saving
// outside the Media folders, is still never asked. The moves themselves are ReplacementQuarantineHttpTests'.
public sealed class ReplacementQuarantineHoldsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const long TheatricalSize = 3_891_719_630;
    private const long SrtSize = 51_234;
    // qBittorrent's Movies category saves to /downloads/movies, the app's /media/movies.
    private const string TheatricalInTorrent = "Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SrtInTorrent = "Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const string SecondNamePath = "/media/torrents/Alien.1979.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";
    private const string SeedboxUrl = "http://seedbox.test:8080";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string HeldReason =
        "Still held by the torrent “Alien.1979.Theatrical” in “qBittorrent”: remove that torrent there without its files, then press Retry.";
    private const string HeldTwiceReason =
        "Still held by the torrents “Alien.1979.Theatrical” in “qBittorrent” and “Alien.1979.Theatrical.Cross” in “seedbox”: "
        + "remove those torrents there without their files, then press Retry.";
    private const string CompanionHeldProblem = "Held by the torrent “Alien.1979.Subs” in “qBittorrent”, so it stays where it is.";
    private const string UnplacedReason =
        "The app couldn't place 1 of “qBittorrent”'s torrents in its folders, so it can't tell whether one of them holds the old copy.";
    private const string UnreachableReason =
        "The app couldn't tell whether a torrent in “qBittorrent” holds the old copy: Couldn't reach qBittorrent at http://qbittorrent.test:8080: "
        + "nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
    private const string SwitchedOffReason = "The app couldn't tell whether a torrent in “qBittorrent” holds the old copy: “qBittorrent” is switched off.";
    private const string TooOldReason =
        "The app couldn't tell whether a torrent in “qBittorrent” holds the old copy: This is qBittorrent v5.1.4; telling which torrents hold a library file "
        + "needs qBittorrent 5.2 or later.";
    private const string OutsideReason =
        "“qBittorrent”'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether that torrent holds the old copy: "
        + "check those mappings under Integrations, then press Retry.";
    private const string OutsideTwiceReason =
        "“qBittorrent”'s path mappings put 2 of its torrents outside the Media folders, so the app can't tell whether one of them holds the old copy: "
        + "check those mappings under Integrations, then press Retry.";
    private const string SeedboxOutsideReason =
        "“seedbox”'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether that torrent holds the old copy: "
        + "check those mappings under Integrations, then press Retry.";
    private const string ElsewhereRoot = "/srv/elsewhere";
    private const string EarlierHash = "4444444444444444444444444444444444444444";

    // What a seedbox was sent before this replacement (MAM-28): nothing, a download that replaced nothing, a replacement it
    // never took, or a replacement through a client deleted since.
    private const string NothingSent = "nothing";
    private const string PlainDownload = "a download that replaced nothing";
    private const string ReplacementNeverTaken = "a replacement it never took";
    private const string ReplacementOfADeletedClient = "a replacement through a client deleted since";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private int _qbittorrentMark;
    private int _addRequestsMark;

    public ReplacementQuarantineHoldsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми, with its subtitles beside it.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize));
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── held ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("stalledUP")]
    [InlineData("queuedUP")]
    [InlineData("stoppedUP")]
    public async Task Quarantine_of_a_video_a_torrent_lists_in_any_state_stops_for_review_naming_it_and_moves_nothing(string state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Seeding, queued to seed, or stopped: listed means held (ADR 0031).
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize, state));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, replacement, HeldReason, HeldReason, cancellationToken);
    }

    [Fact]
    public async Task Quarantine_of_a_video_two_torrents_list_names_each_of_them_by_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The same content seeded in qBittorrent and cross-seeded in a seedbox whose /downloads is the app's /media too.
        var (seedbox, _) = await AddSeedboxAsync();
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize));
        seedbox.AddTorrent(TorrentHolding("Alien.1979.Theatrical.Cross", TheatricalInTorrent, TheatricalSize));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // Each client listed once, and neither written to.
        await AssertStoppedAsync(job, replacement, HeldTwiceReason, HeldTwiceReason, cancellationToken, listings: 2);
        Assert.Empty(seedbox.AddRequests);
    }

    [Fact]
    public async Task Quarantine_of_a_companion_a_torrent_lists_leaves_it_named_on_the_job_and_moves_the_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Subs", SrtInTorrent, SrtSize));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameKind.Video, ReplacementNameState.Quarantined, (string?)null),
                (TheatricalSrtPath, ReplacementNameKind.Companion, ReplacementNameState.Stayed, CompanionHeldProblem),
            ],
            names.Select(name => (name.AppPath, name.Kind, name.State, name.Problem)));
        // The subtitles stay where the torrent has them; only the video was renamed, and the stayed name has no write's rows.
        Assert.Equal([(TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath))], _factory.Quarantine.Renames);
        Assert.Equal(FileInventoryStatus.Found, (await _factory.Files.DescribeAsync(TheatricalSrtPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken)).Status);
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameMoved, names[0]),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
        AssertQBittorrentOnlyRead();
    }

    [Fact]
    public async Task Quarantine_of_a_video_that_is_a_second_name_of_a_torrents_file_moves_it_recording_its_other_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, replacement) = await VerifiedAsync(cancellationToken);
        // A hardlink: the torrent's own name lies in /media/torrents, the library's is the theatrical cut; one file, two names.
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1, linkCount: 2), TestMedia.Taken());
        _factory.Files.AddFile(SecondNamePath, TestMedia.File(inode: 1, linkCount: 2));
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979", "Alien.1979.mkv", TheatricalSize, savePath: "/downloads/torrents"));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // The torrent keeps its own name, so the library's may move; the row says another name keeps the data.
        Assert.Equal(ReplacementJobState.Quarantined, (await _harness.ReplacementAsync(cancellationToken)).State);
        var video = (await NamesAsync(cancellationToken))[0];
        Assert.Equal((ReplacementNameState.Quarantined, (int?)2), (video.State, video.LinkCount));
        Assert.Equal(
            FileInventoryStatus.Found,
            (await _factory.Files.DescribeAsync(SecondNamePath, [ReplacementVerificationHarness.MediaRoot], cancellationToken)).Status);
        Assert.Contains((TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath)), _factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Quarantine_retried_once_the_owner_removed_the_torrent_moves_the_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        var torrent = TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize);
        _harness.QBittorrent.AddTorrent(torrent);
        await QuarantineAsync(cancellationToken);
        var held = await _harness.ReplacementAsync(cancellationToken);
        // The owner removes the torrent in qBittorrent without its files, then presses Retry: the job is verified again
        // (the Retry route's own write, as a conditional update).
        _harness.QBittorrent.RemoveTorrent(torrent.Hash);
        await _factory.WithDbAsync(db => db.ReplacementJobs
            .Where(row => row.Id == replacement.Id && row.State == ReplacementJobState.NeedsReview && row.QuarantineCheckedUtc != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, ReplacementJobState.Verified).SetProperty(row => row.Reason, (string?)null), cancellationToken));
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)HeldReason), (held.State, held.Reason));
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        // The stop's problem no longer holds; both names moved.
        Assert.Null(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem);
        Assert.Equal(
            [ReplacementNameState.Quarantined, ReplacementNameState.Quarantined],
            (await NamesAsync(cancellationToken)).Select(name => name.State));
        Assert.Equal(2, _factory.Quarantine.Renames.Count);
        var audits = await _harness.QuarantineAuditsAsync(cancellationToken);
        Assert.Equal(
            [AuditAction.ReplacementNeedsReview, AuditAction.ReplacementQuarantineStarted],
            audits.Take(2).Select(audit => audit.Action));
        Assert.Equal(AuditAction.ReplacementQuarantined, audits[^1].Action);
        AssertQBittorrentOnlyRead(listings: 2);
    }

    // ── uncertain ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_while_a_torrent_saves_where_no_pair_maps_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // qBittorrent's /elsewhere is no folder of the app's: whatever it holds can't be placed.
        _harness.QBittorrent.AddTorrent(TorrentHolding("Elsewhere", "Alien.mkv", TheatricalSize, savePath: "/elsewhere"));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, replacement, UnplacedReason, null, cancellationToken);
    }

    [Fact]
    public async Task Quarantine_while_qbittorrent_does_not_answer_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _harness.QBittorrent.Unreachable = true;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, replacement, UnreachableReason, null, cancellationToken, listings: 0);
    }

    [Fact]
    public async Task Quarantine_while_qbittorrent_is_switched_off_stops_for_review_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // A switched-off client may still run and seed: its silence is no all-clear.
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(client => client.Id == _harness.Client.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(client => client.IsEnabled, false), cancellationToken));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, replacement, SwitchedOffReason, null, cancellationToken, listings: 0);
        Assert.Empty(_factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark));
    }

    [Fact]
    public async Task Quarantine_while_qbittorrent_is_too_old_to_list_the_files_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        _harness.QBittorrent.Version = "v5.1.4";

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, replacement, TooOldReason, null, cancellationToken, listings: 0);
    }

    // ── a path mapping changed since the replacement was sent (MAM-28) ──────────────────────────

    [Fact]
    public async Task Quarantine_after_qbittorrents_path_mapping_was_deleted_stops_for_review_while_a_torrent_lists_the_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The owner deletes qBittorrent's only mapping: its /downloads is taken as the app's own /downloads now, outside the
        // Media folders, where the app never looks — while the release's torrent still lists the old copy and its subtitles
        // there.
        await RemapClientAsync(null, cancellationToken);
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize) with
        {
            Files = [new FakeQBittorrentFile(TheatricalInTorrent, TheatricalSize, 1), new FakeQBittorrentFile(SrtInTorrent, SrtSize, 1)],
        });

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // The torrent can't be ruled out, so nothing moves; the stop is the job's, not the copy's. One torrent, however many
        // of its files could be a name.
        await AssertStoppedAsync(job, replacement, OutsideReason, null, cancellationToken);
        AssertNothingDescribedOutsideTheMediaFolders();
    }

    [Fact]
    public async Task Quarantine_after_qbittorrents_path_mapping_was_pointed_outside_the_media_folders_stops_for_review_while_a_torrent_lists_the_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // qBittorrent's /downloads is the app's /srv/elsewhere now: a mapping that reaches no Media folder, which alone would
        // leave the client out unasked.
        await RemapClientAsync(ElsewhereRoot, cancellationToken);
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // Its one listing says it was asked all the same: the replacement went through it.
        await AssertStoppedAsync(job, replacement, OutsideReason, null, cancellationToken, listings: 1);
        AssertNothingDescribedOutsideTheMediaFolders();
    }

    [Fact]
    public async Task Quarantine_after_qbittorrents_path_mapping_was_deleted_counts_each_torrent_listing_a_name_the_move_would_rename_in_any_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        await RemapClientAsync(null, cancellationToken);
        // The video's torrent, and a subtitles torrent listing the .srt's own name in capitals at another size.
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize));
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Subs", "Subs/ALIEN (1979) THEATRICAL CUT.SRT", 12_345));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, replacement, OutsideTwiceReason, null, cancellationToken);
        AssertNothingDescribedOutsideTheMediaFolders();
    }

    [Fact]
    public async Task Quarantine_after_qbittorrents_path_mapping_was_pointed_outside_the_media_folders_says_what_it_couldnt_place_then_what_lies_outside()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        await RemapClientAsync(ElsewhereRoot, cancellationToken);
        // The video's torrent, outside the Media folders now, and one saving to /elsewhere, which no mapping covers.
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize));
        _harness.QBittorrent.AddTorrent(TorrentHolding("Elsewhere", "Alien.mkv", TheatricalSize, savePath: "/elsewhere"));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // One client, both of its sentences: the torrent it couldn't place, then the one its mappings put outside.
        await AssertStoppedAsync(job, replacement, UnplacedReason + " " + OutsideReason, null, cancellationToken);
        AssertNothingDescribedOutsideTheMediaFolders();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ElsewhereRoot)]
    public async Task Quarantine_after_qbittorrents_path_mapping_was_deleted_or_pointed_outside_the_media_folders_moves_a_copy_no_torrent_lists_like_it(
        string? appPrefix)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        await RemapClientAsync(appPrefix, cancellationToken);
        // Another film's torrent, outside the Media folders now: no name the move would rename, and none of their sizes.
        _harness.QBittorrent.AddTorrent(TorrentHolding("Blade.Runner.1982", "Blade Runner (1982)/Blade Runner (1982).mkv", 4_120_000_000));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // Only when it matters: nothing listed there could be the old copy, so it moves — qBittorrent asked all the same.
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath)),
                (TheatricalSrtPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalSrtPath)),
            ],
            _factory.Quarantine.Renames);
        AssertNothingDescribedOutsideTheMediaFolders();
        AssertQBittorrentOnlyRead();
    }

    [Fact]
    public async Task Quarantine_while_qbittorrent_seeds_a_second_name_of_the_video_outside_the_media_folders_moves_the_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // /data/torrents hardlinked into /data/media: qBittorrent's /data is the app's /srv/data, outside the Media folders,
        // where the torrent seeds its own name of the theatrical cut; the library's is the other. One file, two names.
        await AddClientMappingAsync("/data", "/srv/data", cancellationToken);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1, linkCount: 2), TestMedia.Taken());
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979", "Alien.1979.mkv", TheatricalSize, savePath: "/data/torrents"));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // A file outside of only the size of a video with several names is the torrent's own name, which moving the
        // library's leaves seeding: no stop, and the video moves — qBittorrent asked all the same.
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        var video = (await NamesAsync(cancellationToken))[0];
        Assert.Equal((ReplacementNameState.Quarantined, (int?)2), (video.State, video.LinkCount));
        Assert.Contains((TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath)), _factory.Quarantine.Renames);
        AssertNothingDescribedOutsideTheMediaFolders();
        AssertQBittorrentOnlyRead();
    }

    [Fact]
    public async Task Quarantine_retried_with_subtitles_left_waiting_reads_their_link_count_again_and_stops_while_a_torrent_outside_the_media_folders_lists_their_size()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // The subtitles have a second name when the move plans them, and the owner switches replacement off right after the
        // video's rename, which leaves them waiting with that link count on their row.
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize, linkCount: 2));
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, false)));
        };
        await QuarantineAsync(cancellationToken);
        var waiting = (await NamesAsync(cancellationToken))[1];
        // While they wait, their other name goes; qBittorrent's /data, the app's /srv/data outside the Media folders, gets a
        // torrent listing a file of their size under another name. Then the owner switches replacement on and presses Retry.
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize));
        await AddClientMappingAsync("/data", "/srv/data", cancellationToken);
        _harness.QBittorrent.AddTorrent(TorrentHolding("Alien.1979.Subs", "Alien.1979.Theatrical.srt", SrtSize, savePath: "/data/subs"));
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, true), cancellationToken));
        var retried = await _factory.PostHttpRequest($"/api/replacements/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // The row's link count is no proof now: read again, the subtitles have one name, so the torrent's file may be them
        // and can't be ruled out. They stay waiting where they are.
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal((TheatricalSrtPath, ReplacementNameState.Planned, (int?)2), (waiting.AppPath, waiting.State, waiting.LinkCount));
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)OutsideReason), (line!.State, line.Reason));
        Assert.Equal(
            [ReplacementNameState.Quarantined, ReplacementNameState.Planned],
            (await NamesAsync(cancellationToken)).Select(name => name.State));
        Assert.Equal([TheatricalPath], _factory.Quarantine.Renames.Select(rename => rename.From));
        AssertNothingDescribedOutsideTheMediaFolders();
    }

    // ── which clients the app sent a replacement to (MAM-28) ────────────────────────────────────

    [Fact]
    public async Task Quarantine_while_a_seedbox_an_earlier_replacement_went_through_lists_the_copy_outside_the_media_folders_stops_for_review_naming_its_mappings()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // An earlier replacement, long completed, went through a seedbox whose /downloads is the app's /srv/seedbox now; this
        // one went through qBittorrent. The seedbox still lists the theatrical cut there.
        var (seedbox, integration) = await AddSeedboxAsync("/srv/seedbox");
        await AddEarlierDownloadAsync(integration.Id, EarlierHash, replacement: true, cancellationToken);
        seedbox.AddTorrent(TorrentHolding("Alien.1979.Theatrical.Cross", TheatricalInTorrent, TheatricalSize));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // Every client the app ever sent a replacement to is asked whatever its mappings, not only this job's: both listed,
        // and the seedbox's torrent can't be ruled out.
        await AssertStoppedAsync(job, replacement, SeedboxOutsideReason, null, cancellationToken, listings: 2);
        Assert.Empty(seedbox.AddRequests);
        AssertNothingDescribedOutsideTheMediaFolders();
    }

    [Theory]
    [InlineData(NothingSent)]
    [InlineData(PlainDownload)]
    [InlineData(ReplacementNeverTaken)]
    [InlineData(ReplacementOfADeletedClient)]
    public async Task Quarantine_while_a_seedbox_the_app_never_sent_a_replacement_to_saves_outside_the_media_folders_moves_the_video_and_asks_it_nothing(
        string earlier)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // A seedbox whose /downloads is the app's /srv/seedbox, listing a file of the video's own name and size there. Sent
        // nothing before; or a download that replaced nothing; or a replacement it never took (no id of its own there); or a
        // replacement through a client deleted since — this seedbox added again, say, under a new id.
        var (seedbox, integration) = await AddSeedboxAsync("/srv/seedbox");
        seedbox.AddTorrent(TorrentHolding("Alien.1979.Theatrical", TheatricalInTorrent, TheatricalSize));
        await (earlier switch
        {
            NothingSent => Task.CompletedTask,
            PlainDownload => AddEarlierDownloadAsync(integration.Id, EarlierHash, replacement: false, cancellationToken),
            ReplacementNeverTaken => AddEarlierDownloadAsync(integration.Id, null, replacement: true, cancellationToken),
            ReplacementOfADeletedClient => AddEarlierDownloadAsync(null, EarlierHash, replacement: true, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(earlier), earlier, null),
        });

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // ADR 0031's "each download client whose paths reach the allowed folders" still holds for it: left out, unasked —
        // and the pass logged no error over a download whose client is gone.
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        Assert.Contains((TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath)), _factory.Quarantine.Renames);
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark), request => request.Uri.Host == "seedbox.test");
        AssertQBittorrentOnlyRead();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, verified: the new film downloaded
    /// to the Movies category's folder, on the disk, and identified by Jellyfin as Alien. The mark of what follows is taken
    /// then.
    /// </summary>
    private async Task<(DownloadJobDto Job, ReplacementJobEntity Replacement)> VerifiedAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        var job = await _harness.VerifiedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
            [(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken())],
            paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId),
            cancellationToken);
        _qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;
        _addRequestsMark = _harness.QBittorrent.AddRequests.Count;
        return (job, await _harness.ReplacementAsync(cancellationToken));
    }

    /// <summary>A second qBittorrent, "seedbox", whose <c>/downloads</c> is the app's <paramref name="appPrefix"/> —
    /// <c>/media</c> unless given —, armed beside the harness's one, with its integration. The app sends it nothing unless
    /// the test says so (<see cref="AddEarlierDownloadAsync"/>).</summary>
    private async Task<(FakeQBittorrentServer Server, IntegrationConnectionEntity Integration)> AddSeedboxAsync(
        string appPrefix = ReplacementVerificationHarness.MediaRoot)
    {
        var seedbox = new FakeQBittorrentServer(SeedboxUrl);
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _harness.QBittorrent, seedbox);
        var integration = await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "qbittorrent",
            "seedbox",
            seedbox.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", appPrefix)]);
        return (seedbox, integration);
    }

    /// <summary>
    /// MAM-28 — a download the owner sent before this replacement's, of the same release: to the client
    /// <paramref name="clientId"/> (<c>null</c>: deleted since), which took it as <paramref name="clientJobId"/>
    /// (<c>null</c>: it never did); with <paramref name="replacement"/>, the replacement it brought, long completed.
    /// </summary>
    private async Task AddEarlierDownloadAsync(Guid? clientId, string? clientJobId, bool replacement, CancellationToken cancellationToken)
    {
        // The one download so far is this replacement's.
        Guid releaseId = default;
        await _factory.WithDbAsync(async db => releaseId = await db.DownloadJobs.AsNoTracking().Select(download => download.ReleaseId).SingleAsync(cancellationToken));
        var context = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var download = await new DownloadJobDataFactory(context, _factory.Time).Create(
            releaseId, clientId, "seedbox", status: DownloadJobStatus.Downloaded, requestedByUserId: _harness.Owner.User.Id, clientJobId: clientJobId);
        if (replacement)
            await new ReplacementJobDataFactory(context, _factory.Time).Create(download.Id, _harness.Owner.User.Id, ReplacementJobState.Completed);
    }

    /// <summary>MAM-28 — qBittorrent's path mappings deleted (<paramref name="appPrefix"/> <c>null</c>), as the owner's Save
    /// of none leaves them — its paths are taken as the app's own then (ADR 0003) —, or its one mapping's app side pointed
    /// at <paramref name="appPrefix"/>.</summary>
    private Task RemapClientAsync(string? appPrefix, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db =>
        {
            var mappings = db.PathMappings.Where(mapping => mapping.IntegrationConnectionId == _harness.Client.Id);
            return appPrefix is null
                ? mappings.ExecuteDeleteAsync(cancellationToken)
                : mappings.ExecuteUpdateAsync(setters => setters.SetProperty(mapping => mapping.AppPrefix, appPrefix), cancellationToken);
        });

    /// <summary>MAM-28 — a second mapping of qBittorrent's, after its <c>/downloads</c> → <c>/media</c>: its
    /// <paramref name="servicePrefix"/> is the app's <paramref name="appPrefix"/>.</summary>
    private Task AddClientMappingAsync(string servicePrefix, string appPrefix, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db =>
        {
            db.PathMappings.Add(new PathMappingEntity
            {
                IntegrationConnectionId = _harness.Client.Id,
                Position = 1,
                ServicePrefix = servicePrefix,
                AppPrefix = appPrefix,
            });
            return db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>MAM-28 — nothing outside the Media folders was ever looked at on the disk (ADR 0009), whatever a torrent
    /// listed there.</summary>
    private void AssertNothingDescribedOutsideTheMediaFolders() =>
        Assert.All(
            _factory.Files.DescribedPaths,
            path => Assert.StartsWith(ReplacementVerificationHarness.MediaRoot + "/", path, StringComparison.Ordinal));

    /// <summary>A torrent of the Movies category in <paramref name="state"/>, done, listing <paramref name="file"/> below
    /// <paramref name="savePath"/> at <paramref name="size"/>; its hash made of its name.</summary>
    private static FakeQBittorrentTorrent TorrentHolding(
        string name, string file, long size, string state = "stalledUP", string savePath = "/downloads/movies") =>
        FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes(Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(name))), null),
                name,
                "Movies",
                state,
                savePath: savePath)
            with { Files = [new FakeQBittorrentFile(file, size, 1)] };

    /// <summary>One pass of the mover, which must not have logged an error.</summary>
    private async Task QuarantineAsync(CancellationToken cancellationToken)
    {
        await _factory.QuarantineReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } error)
            throw new InvalidOperationException($"A pass logged an error: {error.Category}: {error.Message}");
    }

    /// <summary>
    /// The job stopped for review with <paramref name="reason"/> before the move was claimed — on the Jobs page too —, the
    /// copy's problem <paramref name="problem"/>, the mark that lets the owner retry, one audit row of the app's own, no
    /// name planned, nothing renamed and the old copy where it was; the download clients only read — their listings of every
    /// torrent <paramref name="listings"/> times in all.
    /// </summary>
    private async Task AssertStoppedAsync(
        DownloadJobDto job, ReplacementJobEntity replacement, string reason, string? problem, CancellationToken cancellationToken, int listings = 1)
    {
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)reason), (line!.State, line.Reason));
        var stopped = await StoredAsync(replacement.Id, cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)reason, (DateTime?)now, (DateTime?)null),
            (stopped.State, stopped.Reason, stopped.QuarantineCheckedUtc, stopped.LibraryNoticeDueUtc));
        Assert.Equal(problem, Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem);
        Assert.Empty(await NamesAsync(cancellationToken));
        Assert.Equal([JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id)], await _harness.QuarantineAuditsAsync(cancellationToken));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
        AssertQBittorrentOnlyRead(listings);
    }

    /// <summary>
    /// Every request to a download client since the replacement was verified was a read or the sign-in — the listing of
    /// every torrent <paramref name="listings"/> times among them —, and nothing was added to qBittorrent: the app never
    /// writes to a download client (ADR 0031).
    /// </summary>
    private void AssertQBittorrentOnlyRead(int listings = 1)
    {
        var calls = _factory.QBittorrentHttp.Requests
            .Skip(_qbittorrentMark)
            .Select(request => $"{request.Method} {request.Uri.PathAndQuery}")
            .ToList();
        Assert.All(calls, call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Equal(listings, calls.Count(call => call == TorrentsListing));
        Assert.Equal(_addRequestsMark, _harness.QBittorrent.AddRequests.Count);
    }

    /// <summary>The replacement <paramref name="replacementJobId"/> as stored now: a test may plant an earlier one beside it
    /// (MAM-28).</summary>
    private async Task<ReplacementJobEntity> StoredAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        ReplacementJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.ReplacementJobs.AsNoTracking().SingleAsync(job => job.Id == replacementJobId, cancellationToken));
        return row!;
    }

    /// <summary>The replacement's names, in their order.</summary>
    private async Task<IReadOnlyList<ReplacementJobNameEntity>> NamesAsync(CancellationToken cancellationToken)
    {
        List<ReplacementJobNameEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobNames.AsNoTracking().OrderBy(name => name.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>An audit row of the job, the app's own (no actor), naming it by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobAudited(AuditAction action, Guid replacementJobId) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, AlienRelease);

    /// <summary>An audit row of one name, the app's own (no actor), naming it by the file's own name.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, ReplacementJobNameEntity name) =>
        (action, null, AuditTargetKind.ReplacementJobName, name.Id, name.AppPath[(name.AppPath.LastIndexOf('/') + 1)..]);
}
