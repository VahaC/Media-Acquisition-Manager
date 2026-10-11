using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-25, ADR 0035, ADR 0032, ADR 0017 — the check of a season pack's new files: each new episode is Yellowstone's by its
// series' registry ids in Jellyfin, at Jellyfin's season and episode number, and each ticked episode pairs with exactly
// one new file of its place — the extra episode the pack brings is simply added. Jellyfin's end number is read for every
// new file and every ticked copy, each by Jellyfin's own path of it (/data, never the app's /media; ADR 0003). A new file
// covering a ticked episode and the next, by Jellyfin's end number or by a range in its name, a ticked copy Jellyfin
// numbers as two episodes, a ticked copy that is one half of an episode, a ticked episode the pack holds no file of, two
// new files of one episode, a new file Jellyfin makes a film or a ticked episode it now lists as one or numbers otherwise
// than when it was scheduled, and a series whose ids conflict with the show's each stop the job for review, both copies
// staying; a file of two episodes no ticked copy is among is simply added. Jellyfin hears one POST with every new file's
// path — one per batch of the most one call names, for a pack of more —; everything else is a read.
public sealed class ReplacementVerificationEpisodesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string YellowstoneImdbId = "tt4236770";
    private const string YellowstoneTmdbTvId = "73586";
    private const string OtherTmdbTvId = "99999";
    private const string Release = "Yellowstone (S2) (2019) WEB-DL 1080p";
    private const string PackFolder = "Yellowstone.S02.1080p.WEB-DL";
    // The TV Shows category saves to /downloads/tv-shows, which qBittorrent's mapping brings to /media/tv-shows: Серіали's
    // folder.
    private const string NewFolder = "/media/tv-shows/Yellowstone.S02.1080p.WEB-DL";
    // The same folder as Jellyfin knows it, under the /data/tv-shows it reports for Серіали.
    private const string NewLibraryFolder = "/data/tv-shows/Yellowstone.S02.1080p.WEB-DL";
    private const string HalfPath = "/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05a.mkv";
    private const string TwoEpisodesByEndName = "Yellowstone.S02E02.1080p.WEB-DL.mkv";
    private const string TwoEpisodesByRangeName = "Yellowstone.S02E02-E03.1080p.WEB-DL.mkv";
    private const string HdName = "Yellowstone.S02E01.720p.WEB-DL.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const long ContinentalMilliseconds = 5_179_168;

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string TwoEpisodesByEndReason =
        "“/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E02.1080p.WEB-DL.mkv” covers episodes 2–3 of season 2, "
        + "so it can't replace “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv” alone.";
    private const string TwoEpisodesByRangeReason =
        "“/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E02-E03.1080p.WEB-DL.mkv” covers episodes 2–3 of season 2, "
        + "so it can't replace “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv” alone.";
    private const string OldTwoEpisodesReason =
        "“/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv” covers episodes 2–3 of season 2, so one new episode can't replace it.";
    private const string HalfReason =
        "“/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E05a.mkv” is one half of an episode (its name gives the episode the letter “a”), "
        + "so it can't be replaced on its own.";
    private const string NoNewFileReason =
        "No new file is Yellowstone (2018), season 2, episode 2, so “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E02.mkv” has nothing to replace it.";
    private const string TwoNewFilesReason =
        "Two new files are Yellowstone (2018), season 2, episode 1: “/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.1080p.WEB-DL.mkv” and "
        + "“/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.720p.WEB-DL.mkv”; the app can't tell which one replaces the old copy.";
    private const string MadeFilmReason =
        "“Living room” made “/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.1080p.WEB-DL.mkv” a film, "
        + "while the old copies are episodes of Yellowstone (2018).";
    private const string OldListedAsFilmReason =
        "“Living room” lists the old copy “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E01.mkv” as a film, while its new file would be an episode, "
        + "so the two can't be paired.";
    private const string OldRenumberedReason =
        "“Living room” now numbers the old copy “/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E01.mkv” season 2, episode 3, "
        + "not season 2, episode 1 as when it was scheduled.";
    private const string ConflictingSeriesReason =
        "“Living room” gave the series of “/media/tv-shows/Yellowstone.S02.1080p.WEB-DL/Yellowstone.S02E01.1080p.WEB-DL.mkv” conflicting ids: "
        + "IMDb tt4236770 is Yellowstone (2018)'s, TMDb TV 99999 is not.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private MediaWorkEntity _yellowstone = null!;
    private string _seriesItemId = null!;

    public ReplacementVerificationEpisodesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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

    // ── verified ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_season_pack_pairs_each_ticked_episode_and_simply_adds_the_extra_one_keeping_the_evidence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 and 2 ticked; the pack brings episode 3 too, a sample qBittorrent was told to skip, and an .nfo.
        var job = await DownloadedAsync(
            [OldPathOf(1), OldPathOf(2)],
            cancellationToken,
            Episode(1),
            Episode(2),
            Episode(3),
            new FakeQBittorrentFile($"{PackFolder}/Sample/sample.mkv", 1_000, 0, Priority: 0),
            new FakeQBittorrentFile($"{PackFolder}/Yellowstone.S02.nfo", 1_000, 1.0));
        PlantNewFiles(1, 2, 3);
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null), (NewPathOf(2), 2, null), (NewPathOf(3), 3, null));
        await _harness.VerifyAsync(cancellationToken);
        var jellyfinBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 2, null, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id }, await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        // The three episodes, by the client's place — never the skipped sample, nor the .nfo —, each Yellowstone's by its
        // series at its place.
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.Equal(
            [
                (0, (string?)NewPathOf(1), (LibraryFileKind?)LibraryFileKind.Episode, (string?)_seriesItemId, (int?)2, (int?)1, (int?)null, true, (Guid?)_yellowstone.Id, (string?)null),
                (1, NewPathOf(2), LibraryFileKind.Episode, _seriesItemId, 2, 2, null, true, _yellowstone.Id, null),
                (2, NewPathOf(3), LibraryFileKind.Episode, _seriesItemId, 2, 3, null, true, _yellowstone.Id, null),
            ],
            newFiles.Select(file => (file.Position, file.AppPath, file.Kind, file.SeriesItemId, file.SeasonNumber, file.EpisodeNumber,
                file.EpisodeNumberEnd, file.Refreshed, file.MediaWorkId, file.Problem)));
        Assert.All(newFiles, file => Assert.Equal(((long?)NewSize, (long?)ContinentalMilliseconds), (file.SizeBytes, file.DurationMilliseconds)));
        // ADR 0003 — each in the app by qBittorrent's mapping, and in Jellyfin by the folder Jellyfin reported.
        Assert.Equal(
            [
                ((string?)NewPathOf(1), (string?)NewLibraryPathOf(1)),
                (NewPathOf(2), NewLibraryPathOf(2)),
                (NewPathOf(3), NewLibraryPathOf(3)),
            ],
            newFiles.Select(file => (file.AppPath, file.LibraryPath)));
        // ADR 0017 — the series' own ids decide; the episodes carry none of their own.
        Assert.Equal(
            [
                (0, true, MediaIdentifierScheme.Imdb, YellowstoneImdbId), (0, true, MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId),
                (1, true, MediaIdentifierScheme.Imdb, YellowstoneImdbId), (1, true, MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId),
                (2, true, MediaIdentifierScheme.Imdb, YellowstoneImdbId), (2, true, MediaIdentifierScheme.TmdbTv, YellowstoneTmdbTvId),
            ],
            await _harness.NewFileIdentifiersAsync(cancellationToken));
        // Each ticked episode with its own; episode 3, which no copy is of, is simply added.
        Assert.Equal(
            [
                (OldPathOf(1), (Guid?)newFiles[0].Id, (int?)null, (long?)ContinentalMilliseconds, (string?)null),
                (OldPathOf(2), newFiles[1].Id, null, ContinentalMilliseconds, null),
            ],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.AppPath, copy.PairedNewFileId, copy.EpisodeNumberEnd, copy.DurationMilliseconds, copy.Problem)));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementVerified, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        // One request to look at the three new paths, Jellyfin's; the ticked copies' own items read by the paths Jellyfin
        // reported for them, for their kind, series and numbers.
        Assert.Equal(
            [(OldPathOf(1), OldLibraryPathOf(1)), (OldPathOf(2), OldLibraryPathOf(2))],
            (await _harness.CopiesAsync(cancellationToken)).Select(copy => (copy.AppPath, copy.ServicePath)));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Equal([NewLibraryPathOf(1), NewLibraryPathOf(2), NewLibraryPathOf(3)], Assert.Single(_harness.JellyfinServer.ScanRequests));
        var read = _factory.JellyfinHttp.Requests.Skip(jellyfinBefore).Select(request => request.Uri.AbsolutePath).ToList();
        Assert.Contains($"/Items/{JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, OldLibraryPathOf(1))}", read);
        Assert.Contains($"/Items/{JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, OldLibraryPathOf(2))}", read);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_pack_whose_file_of_two_episodes_touches_no_ticked_episode_simply_adds_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episode 1 ticked; the pack's second file covers episodes 3 and 4, none of which is held.
        var job = await DownloadedAsync([OldPathOf(1)], cancellationToken, Episode(1), Episode(3));
        PlantNewFiles(1, 3);
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null), (NewPathOf(3), 3, 4));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id }, await _harness.LineAsync(job.Id, cancellationToken));
        Assert.Equal(
            [((int?)null, (Guid?)_yellowstone.Id, (string?)null), (4, _yellowstone.Id, null)],
            (await _harness.NewFilesAsync(cancellationToken)).Select(file => (file.EpisodeNumberEnd, file.MediaWorkId, file.Problem)));
    }

    [Fact]
    public async Task Verify_of_a_pack_of_more_new_files_than_one_call_to_jellyfin_takes_asks_and_reads_it_in_batches_and_verifies_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A whole series' pack: one episode file more than one call to Jellyfin may name (IMediaLibraryFileLookup, which
        // refuses more as a caller's mistake), episode 1 ticked.
        var count = IMediaLibraryFileLookup.MaximumPaths + 1;
        var numbers = Enumerable.Range(1, count).ToArray();
        var job = await DownloadedAsync([OldPathOf(1)], cancellationToken, [.. numbers.Select(Episode)]);
        PlantNewFiles(numbers);
        AnswerTheRequestToLookWith([.. numbers.Select(number => (NewPathOf(number), number, (int?)null))]);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id }, await _harness.LineAsync(job.Id, cancellationToken));
        // Two requests to look: the most one call names, then the rest, in the client's order.
        Assert.Equal(["POST /Library/Media/Updated", "POST /Library/Media/Updated"], _harness.Writes());
        var asked = _harness.JellyfinServer.ScanRequests.ToList();
        Assert.Equal(2, asked.Count);
        Assert.Equal(numbers.Take(IMediaLibraryFileLookup.MaximumPaths).Select(NewLibraryPathOf), asked[0]);
        Assert.Equal([NewLibraryPathOf(count)], asked[1]);
        // Every new file read — with the ticked copy, more paths than one read names too — and each Yellowstone's; the
        // ticked episode paired, every other one simply added.
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.Equal(count, newFiles.Count);
        Assert.All(newFiles, file => Assert.Equal((true, (Guid?)_yellowstone.Id, (string?)null), (file.Refreshed, file.MediaWorkId, file.Problem)));
        Assert.Equal(((Guid?)newFiles[0].Id, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementVerified, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    // ── stopped ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    // Jellyfin numbers the file 2 to 3.
    [InlineData(TwoEpisodesByEndName, 3, TwoEpisodesByEndReason)]
    // Jellyfin numbers it 2 alone, while its name says 2 to 3: a name may stop a replacement, never let one go.
    [InlineData(TwoEpisodesByRangeName, null, TwoEpisodesByRangeReason)]
    public async Task Verify_of_a_new_file_covering_a_ticked_episode_and_the_next_stops_for_review(string newName, int? numberEnd, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), new FakeQBittorrentFile($"{PackFolder}/{newName}", NewSize, 1.0));
        PlantNewFile(NewPathOf(1), inode: 31);
        PlantNewFile($"{NewFolder}/{newName}", inode: 32);
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null), ($"{NewFolder}/{newName}", 2, numberEnd));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 2, reason, cancellationToken);
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.Equal(
            [((int?)1, (int?)null, (Guid?)_yellowstone.Id, (string?)null), (2, numberEnd, _yellowstone.Id, null)],
            newFiles.Select(file => (file.EpisodeNumber, file.EpisodeNumberEnd, file.MediaWorkId, file.Problem)));
        // Episode 1 had paired before the stop; episode 2 says why it can't.
        Assert.Equal(
            [((Guid?)newFiles[0].Id, (string?)null), (null, reason)],
            (await _harness.CopiesAsync(cancellationToken)).Select(CopyOutcome));
    }

    [Fact]
    public async Task Verify_of_a_ticked_copy_jellyfin_numbers_as_two_episodes_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Since the schedule, Jellyfin read the old episode 2's file as episodes 2 to 3.
        _harness.JellyfinServer.PlantEpisode(OldLibraryPathOf(2), "New Beginnings", _seriesItemId, 2, 2, episodeEnd: 3);
        var job = await DownloadedAsync([OldPathOf(2)], cancellationToken, Episode(2));
        PlantNewFiles(2);
        AnswerTheRequestToLookWith((NewPathOf(2), 2, null));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, OldTwoEpisodesReason, cancellationToken);
        Assert.Equal(((Guid?)_yellowstone.Id, (string?)null), NewFileOutcome(Assert.Single(await _harness.NewFilesAsync(cancellationToken))));
        // The copy keeps Jellyfin's end number of it beside why it stopped.
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(((int?)3, (Guid?)null, (string?)OldTwoEpisodesReason), (copy.EpisodeNumberEnd, copy.PairedNewFileId, copy.Problem));
    }

    [Fact]
    public async Task Verify_of_a_ticked_copy_that_is_one_half_of_an_episode_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-37's segment letter: the old file holds the first half of episode 5, which the library numbers 5.
        await _harness.PlantEpisodeAsync(_yellowstone, 2, 5, "Half the Money", HalfPath, inode: 25);
        await _harness.InspectAsync(cancellationToken);
        var job = await DownloadedAsync([HalfPath], cancellationToken, Episode(5));
        PlantNewFiles(5);
        AnswerTheRequestToLookWith((NewPathOf(5), 5, null));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, HalfReason, cancellationToken);
        Assert.Equal(((Guid?)_yellowstone.Id, (string?)null), NewFileOutcome(Assert.Single(await _harness.NewFilesAsync(cancellationToken))));
        Assert.Equal(((Guid?)null, (string?)HalfReason), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_ticked_episode_the_pack_holds_no_new_file_of_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Episodes 1 and 2 ticked; the pack holds 1 and 3.
        var job = await DownloadedAsync([OldPathOf(1), OldPathOf(2)], cancellationToken, Episode(1), Episode(3));
        PlantNewFiles(1, 3);
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null), (NewPathOf(3), 3, null));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 2, NoNewFileReason, cancellationToken);
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.All(newFiles, file => Assert.Equal(((Guid?)_yellowstone.Id, (string?)null), NewFileOutcome(file)));
        Assert.Equal(
            [((Guid?)newFiles[0].Id, (string?)null), (null, NoNewFileReason)],
            (await _harness.CopiesAsync(cancellationToken)).Select(CopyOutcome));
    }

    [Fact]
    public async Task Verify_of_two_new_files_of_one_ticked_episode_stops_for_review_as_an_unclear_pair()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(
            [OldPathOf(1)], cancellationToken, Episode(1), new FakeQBittorrentFile($"{PackFolder}/{HdName}", NewSize, 1.0));
        PlantNewFile(NewPathOf(1), inode: 31);
        PlantNewFile($"{NewFolder}/{HdName}", inode: 32);
        // Jellyfin numbers both episode 1.
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null), ($"{NewFolder}/{HdName}", 1, null));
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, TwoNewFilesReason, cancellationToken);
        Assert.All(
            await _harness.NewFilesAsync(cancellationToken),
            file => Assert.Equal(((Guid?)_yellowstone.Id, (string?)null), NewFileOutcome(file)));
        Assert.Equal(((Guid?)null, (string?)TwoNewFilesReason), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_new_file_jellyfin_makes_a_film_for_an_episode_copy_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync([OldPathOf(1)], cancellationToken, Episode(1));
        PlantNewFiles(1);
        // A movie item, with the show's own IMDb id: one kind on one side, another on the other.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Yellowstone", 2018, imdbId: YellowstoneImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, MadeFilmReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((LibraryFileKind?)LibraryFileKind.Movie, (string?)null, (Guid?)null, (string?)MadeFilmReason),
            (newFile.Kind, newFile.SeriesItemId, newFile.MediaWorkId, newFile.Problem));
        Assert.Equal([(0, false, MediaIdentifierScheme.Imdb, YellowstoneImdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));
        // The copy waits on the file, unpaired, with no problem of its own.
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_ticked_episode_jellyfin_now_lists_as_a_film_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync([OldPathOf(1)], cancellationToken, Episode(1));
        PlantNewFiles(1);
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null));
        // Since the schedule, Jellyfin re-made the old file's item as a film: the same work is a film on one side, an
        // episode on the other.
        _harness.JellyfinServer.Episodes.RemoveAll(item => (string?)item["Path"] == OldLibraryPathOf(1));
        _harness.JellyfinServer.PlantFilm(OldLibraryPathOf(1), "Yellowstone S02E01", 2019);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, OldListedAsFilmReason, cancellationToken);
        Assert.Equal(((Guid?)_yellowstone.Id, (string?)null), NewFileOutcome(Assert.Single(await _harness.NewFilesAsync(cancellationToken))));
        Assert.Equal(((Guid?)null, (string?)OldListedAsFilmReason), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_ticked_episode_jellyfin_now_numbers_otherwise_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync([OldPathOf(1)], cancellationToken, Episode(1), Episode(3));
        PlantNewFiles(1, 3);
        AnswerTheRequestToLookWith((NewPathOf(1), 1, null), (NewPathOf(3), 3, null));
        // Since the schedule, the owner renumbered the old file's item as episode 3 (ADR 0017: the library alone says what an
        // item is): the new episode 1 is no longer its own.
        _harness.JellyfinServer.Episodes.RemoveAll(item => (string?)item["Path"] == OldLibraryPathOf(1));
        _harness.JellyfinServer.PlantEpisode(OldLibraryPathOf(1), "A Thousand Acres", _seriesItemId, 2, 3);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, OldRenumberedReason, cancellationToken);
        Assert.Equal(((Guid?)null, (string?)OldRenumberedReason), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_new_episode_whose_series_ids_conflict_with_the_shows_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin placed the new episode under a series of its own with Yellowstone's IMDb id and another show's TMDb id.
        var otherSeriesId = Guid.NewGuid().ToString("N");
        _harness.JellyfinServer.Series.Add(JellyfinSamples.SeriesWith(otherSeriesId, "Yellowstone", 2018, NewLibraryFolder, YellowstoneImdbId, OtherTmdbTvId));
        var job = await DownloadedAsync([OldPathOf(1)], cancellationToken, Episode(1));
        PlantNewFiles(1);
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantEpisode(paths[0], "Episode 1", otherSeriesId, 2, 1);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, 1, ConflictingSeriesReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)otherSeriesId, (Guid?)null, (string?)ConflictingSeriesReason), (newFile.SeriesItemId, newFile.MediaWorkId, newFile.Problem));
        Assert.Equal(
            [(0, true, MediaIdentifierScheme.Imdb, YellowstoneImdbId), (0, true, MediaIdentifierScheme.TmdbTv, OtherTmdbTvId)],
            await _harness.NewFileIdentifiersAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string OldPathOf(int number) => $"/media/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv";

    private static string OldLibraryPathOf(int number) => $"/data/tv-shows/Yellowstone/Season 2/Yellowstone S02E{number:00}.mkv";

    private static string NewPathOf(int number) => $"{NewFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv";

    private static string NewLibraryPathOf(int number) => $"{NewLibraryFolder}/Yellowstone.S02E{number:00}.1080p.WEB-DL.mkv";

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
    /// episode of Yellowstone's series item in season 2, at its number — and to its end number, when given —, read and
    /// refreshed.
    /// </summary>
    private void AnswerTheRequestToLookWith(params (string Path, int Number, int? NumberEnd)[] items)
    {
        var byLibraryPath = items.ToDictionary(item => ReplacementVerificationHarness.InJellyfin(item.Path), StringComparer.Ordinal);
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            foreach (var path in paths)
            {
                if (byLibraryPath.TryGetValue(path, out var item))
                    _harness.JellyfinServer.PlantEpisode(path, $"Episode {item.Number}", _seriesItemId, 2, item.Number, item.NumberEnd);
            }
        };
    }

    /// <summary>
    /// The job stopped for review with <paramref name="reason"/> — on the Jobs page too, its <paramref name="copyCount"/>
    /// copies kept —, with its two audit rows of the app's own, and nothing touched: Jellyfin asked once to look, qBittorrent
    /// only read, the old copies where they were.
    /// </summary>
    private async Task AssertNeedsReviewAsync(DownloadJobDto job, int copyCount, string reason, CancellationToken cancellationToken)
    {
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, copyCount, reason, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)reason), (replacement.State, replacement.Reason));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    /// <summary>An audit row of the check as it should be: the app's own (no actor), naming the replacement by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) Audited(AuditAction action, Guid replacementJobId) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, Release);

    private static (Guid? MediaWorkId, string? Problem) NewFileOutcome(ReplacementJobNewFileEntity file) => (file.MediaWorkId, file.Problem);

    private static (Guid? PairedNewFileId, string? Problem) CopyOutcome(ReplacementJobFileEntity copy) => (copy.PairedNewFileId, copy.Problem);
}
