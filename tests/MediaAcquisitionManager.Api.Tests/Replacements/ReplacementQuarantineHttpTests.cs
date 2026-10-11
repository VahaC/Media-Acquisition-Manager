using System.Globalization;
using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030, ADR 0033 — once a replacement is verified, the mover's own pass moves the old copy the owner ticked
// into the quarantine of the allowed folder holding it, at /media/.mam-quarantine/<job id>/<its path inside /media>: the
// video, then each of its own companions (its name without the extension and a dot, no media file itself), one rename each,
// keeping the device and inode, never another video nor another video's companion whose name begins with the ticked
// video's, never the folder. Each name has an intent row before its rename and an outcome row after it, between the job's
// ReplacementQuarantineStarted and ReplacementQuarantined, none with an actor; the job rests at Quarantined, owing the
// library a notice. A season moves episode by episode: one that can't be renamed stops the job for review with the earlier
// episodes left in the quarantine and the later ones where they are, and so do the switch turned off and the folder
// unticked between two names — read under each name's intent's write lock, so a Media folders Save committed right before
// it is seen. The owner's Retry of such a stop moves the companions left waiting beside their video, each checked again
// first, and settles the job. While replacement is switched off a verified job stays as it is and nothing but the gates is
// read; a job an owner's action holds waits for the next pass. Nothing is written to qBittorrent: check 6 reads its one
// listing. The checks' stops are ReplacementQuarantineChecksHttpTests', the torrents'
// ReplacementQuarantineHoldsHttpTests', a restart's ReplacementQuarantineRecoveryHttpTests'.
public sealed class ReplacementQuarantineHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string AlienFolder = "/media/movies/Alien (1979)";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalNfoPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.nfo";
    private const string TheatricalSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const string TheatricalUkSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    // Another video of the folder whose name begins with the ticked one's and a dot, its own subtitles, and a poster.
    private const string UhdPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.2160p.mkv";
    private const string UhdUkSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.2160p.uk.srt";
    private const string PosterPath = "/media/movies/Alien (1979)/poster.jpg";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const string TorrentsListing = "GET /api/v2/torrents/info?includeFiles=true";
    private const string LookRequest = "POST /Library/Media/Updated";
    private const string YellowstoneImdbId = "tt4236770";
    private const string YellowstoneTmdbTvId = "73586";
    private const string SeasonRelease = "Yellowstone (S2) (2019) WEB-DL 1080p";
    private const string PackFolder = "Yellowstone.S02.1080p.WEB-DL";
    private const string NewSeasonFolder = "/media/tv-shows/Yellowstone.S02.1080p.WEB-DL";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string EpisodeNotMovedReason =
        "“Yellowstone S02E02.mkv” couldn't be moved into the quarantine: The app may not write in the folder of "
        + "“/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv”. 1 of 3 names is in the quarantine.";
    private const string NotAllowedError = "The app may not write in the folder of “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv”.";
    private const string SwitchedOffWhileMovingReason =
        "Replacement was switched off while the old copies were moving: 1 of 4 names is in the quarantine.";
    private const string LeftTickedWhileMovingReason =
        "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.nfo” left the folders ticked for replacement while the old copies were moving: "
        + "1 of 4 names is in the quarantine.";
    // Nothing got as far as the quarantine: no count at all.
    private const string LeftBeforeAnyNameReason =
        "“/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv” left the folders ticked for replacement while the old copies were moving.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private int _qbittorrentMark;

    public ReplacementQuarantineHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми, with its subtitles and its .nfo beside it.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _factory.Files.AddFile(TheatricalNfoPath, TestMedia.File(inode: 2, sizeBytes: 4_567));
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: 51_234));
        _factory.Files.AddFile(TheatricalUkSrtPath, TestMedia.File(inode: 4, sizeBytes: 52_345));
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── moved ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_of_a_verified_film_moves_its_video_and_own_companions_into_the_quarantine_keeping_their_identity()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        // The library won't take the notice now, so it stays owed whatever the notice does with it; the notice itself is
        // ReplacementLibraryNoticeHttpTests'.
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        var addRequests = _harness.QBittorrent.AddRequests.Count;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        var moved = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Quarantined, (string?)null, now, (DateTime?)now, (DateTime?)now, (DateTime?)now),
            (moved.State, moved.Reason, moved.StateChangedUtc, moved.QuarantineCheckedUtc, moved.QuarantinedUtc, moved.LibraryNoticeDueUtc));

        // The video, then its own companions in the folder's order, each where it came from and where it went in the
        // quarantine of /media, with the identity the move expected and kept.
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                (0, ReplacementNameKind.Video, TheatricalPath, QuarantinePathOf(replacement, TheatricalPath), 1L, 3_891_719_630L, ReplacementNameState.Quarantined),
                (1, ReplacementNameKind.Companion, TheatricalNfoPath, QuarantinePathOf(replacement, TheatricalNfoPath), 2L, 4_567L, ReplacementNameState.Quarantined),
                (2, ReplacementNameKind.Companion, TheatricalSrtPath, QuarantinePathOf(replacement, TheatricalSrtPath), 3L, 51_234L, ReplacementNameState.Quarantined),
                (3, ReplacementNameKind.Companion, TheatricalUkSrtPath, QuarantinePathOf(replacement, TheatricalUkSrtPath), 4L, 52_345L, ReplacementNameState.Quarantined),
            ],
            names.Select(name => (name.Position, name.Kind, name.AppPath, name.QuarantinePath, name.Inode, name.SizeBytes, name.State)));
        Assert.All(
            names,
            name => Assert.Equal(
                (replacement.Id, copy.Id, ReplacementVerificationHarness.MediaRoot, TestMedia.DeviceId, TestMedia.ModifiedUtc, (int?)1, (string?)null, (DateTime?)now),
                (name.ReplacementJobId, name.ReplacementJobFileId, name.AllowedRoot, name.DeviceId, name.ModifiedUtc, name.LinkCount, name.Problem, name.MovedUtc)));
        Assert.Equal((true, (string?)null), (copy.PairedNewFileId is not null, copy.Problem));

        // One rename per name, in that order, never a copy: each is at its quarantine path with its device and inode and
        // nothing is at its old one; the folder stays. The only folders made are the quarantine's.
        Assert.Equal(names.Select(name => (name.AppPath, name.QuarantinePath)), _factory.Quarantine.Renames);
        foreach (var name in names)
        {
            Assert.Equal((FileInventoryStatus.NotFound, (long?)null), await WhatIsAtAsync(name.AppPath, cancellationToken));
            Assert.Equal((FileInventoryStatus.Found, (long?)name.Inode), await WhatIsAtAsync(name.QuarantinePath, cancellationToken));
        }

        Assert.True(_factory.Files.IsFolder(AlienFolder));
        Assert.Equal(
            [
                ReplacementVerificationHarness.QuarantineRoot,
                $"{ReplacementVerificationHarness.QuarantineRoot}/{replacement.Id}",
                $"{ReplacementVerificationHarness.QuarantineRoot}/{replacement.Id}/movies",
                $"{ReplacementVerificationHarness.QuarantineRoot}/{replacement.Id}/movies/Alien (1979)",
            ],
            _factory.Quarantine.CreatedFolders);
        Assert.Empty(_factory.Quarantine.Deletions);

        // The trail, save by save: the claim, each name's intent then its outcome, the end — all the app's own.
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameMoved, names[0]),
                NameAudited(AuditAction.ReplacementNameMoving, names[1]),
                NameAudited(AuditAction.ReplacementNameMoved, names[1]),
                NameAudited(AuditAction.ReplacementNameMoving, names[2]),
                NameAudited(AuditAction.ReplacementNameMoved, names[2]),
                NameAudited(AuditAction.ReplacementNameMoving, names[3]),
                NameAudited(AuditAction.ReplacementNameMoved, names[3]),
                JobAudited(AuditAction.ReplacementQuarantined, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));

        // ADR 0031 — check 6 read qBittorrent's one listing of every torrent with its files and wrote nothing to it; the
        // library only hears requests to look.
        AssertQBittorrentOnlyRead(addRequests);
        Assert.All(_harness.Writes(), write => Assert.Equal(LookRequest, write));
    }

    [Fact]
    public async Task Quarantine_leaves_another_videos_companion_whose_name_begins_with_the_ticked_videos_name_in_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0030 "What": "Alien (1979) Theatrical Cut.2160p.uk.srt" begins with the ticked video's name and a dot, but
        // with the longer name of the other video too: it is that one's.
        _factory.Files.AddFile(UhdPath, TestMedia.File(inode: 5, sizeBytes: 9_000_000_000));
        _factory.Files.AddFile(UhdUkSrtPath, TestMedia.File(inode: 6, sizeBytes: 53_456));
        _factory.Files.AddFile(PosterPath, TestMedia.File(inode: 8, sizeBytes: 345_678));
        var (_, replacement) = await VerifiedFilmAsync(cancellationToken);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal(ReplacementJobState.Quarantined, (await _harness.ReplacementAsync(cancellationToken)).State);
        Assert.Equal(
            [TheatricalPath, TheatricalNfoPath, TheatricalSrtPath, TheatricalUkSrtPath],
            (await NamesAsync(cancellationToken)).Select(name => name.AppPath));
        // The other video, its subtitles and the poster stay where they are, as they were.
        Assert.Equal((FileInventoryStatus.Found, (long?)5), await WhatIsAtAsync(UhdPath, cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, (long?)6), await WhatIsAtAsync(UhdUkSrtPath, cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, (long?)8), await WhatIsAtAsync(PosterPath, cancellationToken));
        Assert.DoesNotContain(_factory.Quarantine.Renames, rename => rename.From is UhdPath or UhdUkSrtPath or PosterPath);
        Assert.Equal((FileInventoryStatus.NotFound, (long?)null), await WhatIsAtAsync(QuarantinePathOf(replacement, UhdUkSrtPath), cancellationToken));
    }

    [Fact]
    public async Task Quarantine_of_a_season_whose_second_episode_cant_be_renamed_keeps_the_first_in_the_quarantine_and_leaves_the_third_where_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedSeasonAsync(cancellationToken);
        // The library won't take the notice, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeHttpTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The kernel refuses the second episode's rename (EACCES).
        _factory.Quarantine.Refuse(EpisodePathOf(2), FileQuarantineStatus.NotAllowed, NotAllowedError);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)EpisodeNotMovedReason), (line!.State, line.Reason));
        var stopped = await _harness.ReplacementAsync(cancellationToken);
        // A name left the library, so the library is owed a notice; nothing was deleted.
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (DateTime?)now, (DateTime?)null, (DateTime?)now),
            (stopped.State, stopped.QuarantineCheckedUtc, stopped.QuarantinedUtc, stopped.LibraryNoticeDueUtc));

        // The first episode is in the quarantine, the second stayed with the kernel's sentence, the third was never tried.
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [
                (EpisodePathOf(1), ReplacementNameState.Quarantined, (string?)null),
                (EpisodePathOf(2), ReplacementNameState.Stayed, NotAllowedError),
                (EpisodePathOf(3), ReplacementNameState.Planned, null),
            ],
            names.Select(name => (name.AppPath, name.State, name.Problem)));
        Assert.Equal([(EpisodePathOf(1), QuarantinePathOf(replacement, EpisodePathOf(1)))], _factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, (long?)21), await WhatIsAtAsync(QuarantinePathOf(replacement, EpisodePathOf(1)), cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, (long?)22), await WhatIsAtAsync(EpisodePathOf(2), cancellationToken));
        Assert.Equal((FileInventoryStatus.Found, (long?)23), await WhatIsAtAsync(EpisodePathOf(3), cancellationToken));
        Assert.Empty(_factory.Quarantine.Deletions);

        // The episode that stopped the job names it; the others name nothing.
        Assert.Equal(
            [(string?)null, EpisodeNotMovedReason, null],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => copy.Problem));
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id, SeasonRelease),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameMoved, names[0]),
                NameAudited(AuditAction.ReplacementNameMoving, names[1]),
                NameAudited(AuditAction.ReplacementNameNotMoved, names[1]),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id, SeasonRelease),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_switched_off_between_two_names_stops_for_review_with_the_names_already_moved_and_moves_no_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        // The library won't take the notice, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeHttpTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The owner switches replacement off on the Media folders page right after the video's rename.
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, false)));
        };

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)SwitchedOffWhileMovingReason), (line!.State, line.Reason));
        Assert.NotNull((await _harness.ReplacementAsync(cancellationToken)).LibraryNoticeDueUtc);
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(
            [ReplacementNameState.Quarantined, ReplacementNameState.Planned, ReplacementNameState.Planned, ReplacementNameState.Planned],
            names.Select(name => name.State));
        Assert.Equal([(TheatricalPath, QuarantinePathOf(replacement, TheatricalPath))], _factory.Quarantine.Renames);
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(TheatricalNfoPath, cancellationToken));
        // No copy stopped it: the switch did.
        Assert.Null(Assert.Single(await _harness.CopiesAsync(cancellationToken)).Problem);
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                NameAudited(AuditAction.ReplacementNameMoving, names[0]),
                NameAudited(AuditAction.ReplacementNameMoved, names[0]),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_whose_folder_is_unticked_between_two_names_stops_for_review_with_the_names_already_moved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, _) = await VerifiedFilmAsync(cancellationToken);
        // The owner unticks Фільми right after the video's rename.
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await _factory.WithDbAsync(db => db.ReplacementFolders.ExecuteDeleteAsync());
        };

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)LeftTickedWhileMovingReason), (line!.State, line.Reason));
        Assert.Equal(
            [ReplacementNameState.Quarantined, ReplacementNameState.Planned, ReplacementNameState.Planned, ReplacementNameState.Planned],
            (await NamesAsync(cancellationToken)).Select(name => name.State));
        Assert.Single(_factory.Quarantine.Renames);
    }

    [Fact]
    public async Task Quarantine_whose_media_folder_leaves_the_list_right_before_a_names_intent_renames_nothing_and_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        // A Save of the Media folders taking /media out of the list commits once the first name is about to be intended:
        // the pass's first transaction is its claim, the second the first name's intent, under whose write lock the gates
        // are read — a read before it would rename into a quarantine no allowed root holds any more.
        var transactions = 0;
        _factory.TransactionHook.BeforeNextTransaction(
            _ => Interlocked.Increment(ref transactions) == 2,
            token => _factory.WithDbAsync(db => db.AllowedRoots.ExecuteUpdateAsync(setters => setters.SetProperty(root => root.Path, "/srv"), token)));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)LeftBeforeAnyNameReason), (line!.State, line.Reason));
        Assert.All(await NamesAsync(cancellationToken), name => Assert.Equal(ReplacementNameState.Planned, name.State));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
        Assert.Equal((FileInventoryStatus.Found, (long?)1), await WhatIsAtAsync(TheatricalPath, cancellationToken));
        // No intent was written for the name: the gate closed under its lock, and its transaction wrote nothing.
        Assert.Equal(
            [
                JobAudited(AuditAction.ReplacementQuarantineStarted, replacement.Id),
                JobAudited(AuditAction.ReplacementNeedsReview, replacement.Id),
            ],
            await _harness.QuarantineAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Quarantine_retried_after_a_stop_between_a_video_and_its_companions_moves_the_companions_left_waiting()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        // The library won't take the notice, so it stays owed whatever the notice does with it (its own tests are
        // ReplacementLibraryNoticeHttpTests').
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        // The owner switches replacement off right after the video's rename, then on again, and presses Retry.
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, false)));
        };
        await QuarantineAsync(cancellationToken);
        var stopped = await NamesAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, true), cancellationToken));
        var retried = await _factory.PostHttpRequest($"/api/replacements/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        var qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(
            [ReplacementNameState.Quarantined, ReplacementNameState.Planned, ReplacementNameState.Planned, ReplacementNameState.Planned],
            stopped.Select(name => name.State));
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Quarantined, (string?)null, 1, 0, false),
            (line!.State, line.Reason, line.QuarantinedCopyCount, line.StayedFileCount, line.CanRetry));
        // The same rows in their places, the companions in the quarantine beside their video now; the video isn't moved
        // twice.
        var names = await NamesAsync(cancellationToken);
        Assert.Equal(stopped.Select(name => (name.Id, name.Position)), names.Select(name => (name.Id, name.Position)));
        Assert.All(names, name => Assert.Equal(ReplacementNameState.Quarantined, name.State));
        Assert.Equal(names.Select(name => (name.AppPath, name.QuarantinePath)), _factory.Quarantine.Renames);
        foreach (var name in names.Skip(1))
            Assert.Equal((FileInventoryStatus.Found, (long?)name.Inode), await WhatIsAtAsync(name.QuarantinePath, cancellationToken));
        // ADR 0031 — the companions were checked against every torrent again before they moved: one listing, nothing
        // written.
        Assert.Single(
            _factory.QBittorrentHttp.Requests.Skip(qbittorrentMark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}"),
            call => call == TorrentsListing);
    }

    [Fact]
    public async Task Quarantine_retried_with_companions_left_waiting_that_changed_meanwhile_leaves_them_and_settles_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.ServiceUnavailable;
        _factory.Quarantine.AfterRename = async (_, _, _) =>
        {
            _factory.Quarantine.AfterRename = null;
            await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, false)));
        };
        await QuarantineAsync(cancellationToken);
        // While they waited, the .nfo was rewritten and the Ukrainian subtitles deleted by hand.
        _factory.Files.AddFile(TheatricalNfoPath, TestMedia.File(inode: 2, sizeBytes: 9_999));
        _factory.Files.Remove(TheatricalUkSrtPath);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, true), cancellationToken));
        await _factory.PostHttpRequest($"/api/replacements/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // A companion left waiting never stops a job whose video is in the quarantine: it stays, or is lost, saying why.
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, 1, 1), (line!.State, line.QuarantinedCopyCount, line.StayedFileCount));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameState.Quarantined, (string?)null),
                (TheatricalNfoPath, ReplacementNameState.Stayed, $"“{TheatricalNfoPath}” changed since it was picked for replacement, so it stays where it is."),
                (TheatricalSrtPath, ReplacementNameState.Quarantined, null),
                (TheatricalUkSrtPath, ReplacementNameState.Lost, $"“{TheatricalUkSrtPath}” is gone, so there is nothing to move."),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.State, name.Problem)));
        Assert.Equal([TheatricalPath, TheatricalSrtPath], _factory.Quarantine.Renames.Select(rename => rename.From));
        Assert.Equal((FileInventoryStatus.Found, (long?)2), await WhatIsAtAsync(TheatricalNfoPath, cancellationToken));
    }

    // ── switched off ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_while_replacement_is_switched_off_leaves_the_job_verified_and_reads_nothing_but_the_gates()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedFilmAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(settings => settings.SetProperty(row => row.IsEnabled, false)));
        var described = _factory.Files.DescribedPaths.Count;
        var qbittorrentRequests = _factory.QBittorrentHttp.Requests.Count;
        var jellyfinRequests = _factory.JellyfinHttp.Requests.Count;

        //Act
        await QuarantineAsync(cancellationToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);

        //Assert
        // ADR 0033 — nothing written: the job stays as verified, and its line says nothing will move while it is off.
        var unchanged = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Verified, replacement.StateChangedUtc, (DateTime?)null, (DateTime?)null),
            (unchanged.State, unchanged.StateChangedUtc, unchanged.QuarantineCheckedUtc, unchanged.LibraryNoticeDueUtc));
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, true), (line!.State, line.SwitchedOff));
        Assert.Empty(await NamesAsync(cancellationToken));
        Assert.Empty(await _harness.QuarantineAuditsAsync(cancellationToken));
        // Nothing read but the gates in the database: no file looked at, no client nor library asked, nothing renamed.
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
        Assert.Equal(qbittorrentRequests, _factory.QBittorrentHttp.Requests.Count);
        Assert.Equal(jellyfinRequests, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    // ── the loop ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantine_of_a_job_an_owners_action_holds_waits_for_the_next_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, replacement) = await VerifiedFilmAsync(cancellationToken);
        var inHand = _factory.Resolve<ReplacementJobsInHand>();
        inHand.TryTake(replacement.Id);
        try
        {
            //Act
            await QuarantineAsync(cancellationToken);
            var held = await _harness.ReplacementAsync(cancellationToken);
            inHand.Release(replacement.Id);
            await QuarantineAsync(cancellationToken);

            //Assert
            Assert.Equal(ReplacementJobState.Verified, held.State);
            Assert.Equal(ReplacementJobState.Quarantined, (await _harness.ReplacementAsync(cancellationToken)).State);
            Assert.False(inHand.Holds(replacement.Id));
        }
        finally
        {
            inHand.Release(replacement.Id);
        }
    }

    [Fact]
    public async Task Tracker_runs_a_pass_as_soon_as_it_starts_and_stops_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await VerifiedFilmAsync(cancellationToken);
        var tracker = _factory.Resolve<ReplacementQuarantineTracker>();
        _factory.Logs.Clear();

        //Act
        // The host's own start and stop, which the test host leaves to the test.
        await tracker.StartAsync(cancellationToken);
        var quarantined = await WaitForQuarantinedAsync(cancellationToken);
        await tracker.StopAsync(cancellationToken);

        //Assert
        // The first pass right after the start, without waiting PassInterval; the stop ends the wait for the next one,
        // which is no error. On .NET 10 a stop can also win the race before the loop runs at all.
        Assert.True(quarantined);
        Assert.True(tracker.ExecuteTask is { Status: TaskStatus.RanToCompletion or TaskStatus.Canceled }, $"{tracker.ExecuteTask?.Status}");
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release, verified: the new film downloaded
    /// to the Movies category's folder, on the disk, and identified by Jellyfin as Alien.
    /// </summary>
    private async Task<(DownloadJobDto Job, ReplacementJobEntity Replacement)> VerifiedFilmAsync(CancellationToken cancellationToken)
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
        return (job, await _harness.ReplacementAsync(cancellationToken));
    }

    /// <summary>
    /// The owner's replacement of Yellowstone's season 2 episodes 1 to 3 by a season pack, verified: each new episode on the
    /// disk, and identified by Jellyfin as its episode of the series.
    /// </summary>
    private async Task<(DownloadJobDto Job, ReplacementJobEntity Replacement)> VerifiedSeasonAsync(CancellationToken cancellationToken)
    {
        var yellowstone = await _harness.PlantSeriesAsync("Yellowstone", 2018, YellowstoneImdbId, YellowstoneTmdbTvId);
        var seriesItemId = _harness.SeriesItemIdOf(yellowstone);
        for (var number = 1; number <= 3; number++)
            await _harness.PlantEpisodeAsync(yellowstone, 2, number, $"Episode {number}", EpisodePathOf(number), inode: 20 + number);
        await _harness.InspectAsync(cancellationToken);

        var release = await _harness.FindAsync("Yellowstone", 2018, SeasonRelease, ReleaseKind.Tv, YellowstoneImdbId);
        var job = await _harness.VerifiedAsync(
            release,
            _harness.TvShows,
            [EpisodePathOf(1), EpisodePathOf(2), EpisodePathOf(3)],
            [.. Enumerable.Range(1, 3).Select(number => new FakeQBittorrentFile(NewEpisodeNameOf(number), NewSize, 1.0))],
            [.. Enumerable.Range(1, 3).Select(number => (NewEpisodePathOf(number), TestMedia.File(inode: 30 + number, sizeBytes: NewSize), TestMedia.Continental()))],
            paths =>
            {
                foreach (var path in paths)
                {
                    var number = Enumerable.Range(1, 3).Single(candidate => ReplacementVerificationHarness.InJellyfin(NewEpisodePathOf(candidate)) == path);
                    _harness.JellyfinServer.PlantEpisode(path, $"Episode {number}", seriesItemId, 2, number);
                }
            },
            cancellationToken);
        return (job, await _harness.ReplacementAsync(cancellationToken));
    }

    private static string EpisodePathOf(int number) =>
        string.Create(CultureInfo.InvariantCulture, $"/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv");

    private static string NewEpisodeNameOf(int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{PackFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv");

    private static string NewEpisodePathOf(int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{NewSeasonFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv");

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

    /// <summary>What the disk shows at <paramref name="appPath"/> inside /media: whether a file is there, and its inode —
    /// on the one device every test file is on, or none.</summary>
    private async Task<(FileInventoryStatus Status, long? Inode)> WhatIsAtAsync(string appPath, CancellationToken cancellationToken)
    {
        var described = await _factory.Files.DescribeAsync(appPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken);
        return (described.Status, described.Value?.DeviceId == TestMedia.DeviceId ? described.Value.Inode : null);
    }

    /// <summary>Every request to qBittorrent since the replacement was verified was a read or the sign-in, its one listing
    /// of every torrent among them, and nothing was added.</summary>
    private void AssertQBittorrentOnlyRead(int addRequests)
    {
        var calls = _factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark).Select(request => $"{request.Method} {request.Uri.PathAndQuery}").ToList();
        Assert.All(calls, call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Single(calls, call => call == TorrentsListing);
        Assert.Equal(addRequests, _harness.QBittorrent.AddRequests.Count);
    }

    /// <summary>Whether the replacement is Quarantined within five seconds, read every 50 ms: for the one test that lets
    /// the tracker's own loop run.</summary>
    private async Task<bool> WaitForQuarantinedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await _harness.ReplacementAsync(cancellationToken)).State == ReplacementJobState.Quarantined)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        return false;
    }

    private static string QuarantinePathOf(ReplacementJobEntity replacement, string appPath) =>
        ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, appPath);

    /// <summary>An audit row of the job, the app's own (no actor), naming it by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) JobAudited(AuditAction action, Guid replacementJobId, string release = AlienRelease) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, release);

    /// <summary>An audit row of one name, the app's own (no actor), naming it by the file's own name.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) NameAudited(AuditAction action, ReplacementJobNameEntity name) =>
        (action, null, AuditTargetKind.ReplacementJobName, name.Id, name.AppPath[(name.AppPath.LastIndexOf('/') + 1)..]);
}
