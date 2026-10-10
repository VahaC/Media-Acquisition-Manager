using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-25, ADR 0035, ADR 0034, ADR 0003 — checks 1 and 2 of a replacement's new file, before Jellyfin is asked anything:
// qBittorrent's own word, read fresh, that the download finished without an error and that the file is complete; then the
// file placed in the app through the client's mappings and in the library folder that holds it, and read by the media
// inspector inside the allowed folders. A download qBittorrent reports an error for, lists twice or no longer has, a client
// removed, a file it hasn't finished, a file no mapping places or no library folder holds or the folders of two libraries
// do, the old copy itself, and a file another download also writes — by the same path, or by a link at another one (ADR
// 0034's accepted gap, closed) — each stop the job for review; a file ffprobe can't read, an empty one, one without a video
// stream and one without a running time fail it — Failed is for a bad file only. qBittorrent unreachable or switched off,
// and another download the app can't place — a magnet still without its files, a folder no mapping brings in — are waited
// for an hour from the start of the check, then the job stops for review; a qBittorrent too old to list its other downloads
// can't clear the new file, so it stops at once, and a file of the new one's size that is another file lets it through.
// Each ends with its reason, its evidence and its two audit rows, nothing asked of Jellyfin, nothing but reads sent to
// qBittorrent, and the old copy where it was.
public sealed class ReplacementVerificationFilesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    // Фільми's folder as Jellyfin reports it (/data/movies) followed by the file's name: its path in Jellyfin.
    private const string NewLibraryPath = "/data/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const string PackFolder = "Alien.1979.Directors.Cut";
    private const string BonusName = "Alien.1979.Directors.Cut.Bonus.1080p.mkv";
    // A folder qBittorrent's mapping brings into the app, inside the allowed /media, which no library holds.
    private const string ElsewherePath = "/media/elsewhere/Alien.1979.Directors.Cut.2160p.mkv";
    // Where another category saves the same release, inside the allowed /media: a second name of the new file, if linked.
    private const string CrossSeedPath = "/media/cross-seed/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const long TheatricalSize = 3_891_719_630;
    private const long TakenMilliseconds = 5_604_672;
    private const string OtherHash = "2222222222222222222222222222222222222222";
    private const string IntegrationsUrl = "/api/settings/integrations";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string ErrorReason = "“qBittorrent” reports an error for this download, so the app can't confirm its files are complete.";
    private const string GoneReason = "“qBittorrent” no longer has this download, so the app can't confirm its files are complete.";
    private const string UnfinishedReason = "“qBittorrent” hasn't finished “Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.Bonus.1080p.mkv” (73 % done).";
    private const string UnknownPathReason =
        "The app can't tell where the new file “Alien.1979.Directors.Cut.2160p.mkv” is: No path mapping of this integration covers “/srv/elsewhere/Alien.1979.Directors.Cut.2160p.mkv”.";
    private const string NoLibraryReason =
        "The new file “/media/elsewhere/Alien.1979.Directors.Cut.2160p.mkv” is in no library folder the app knows, so no library can be asked to confirm it.";
    private const string UnreadableReason =
        "ffprobe couldn't read “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” as media: Invalid data found when processing input.";
    private const string EmptyReason = "“/media/movies/Alien.1979.Directors.Cut.2160p.mkv” is empty.";
    private const string NoVideoReason = "“/media/movies/Alien.1979.Directors.Cut.2160p.mkv” has no video stream.";
    private const string NoRunningTimeReason = "The media inspector found no running time in “/media/movies/Alien.1979.Directors.Cut.2160p.mkv”.";
    private const string OldCopyItselfReason = "The new file “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” is the old copy itself.";
    private const string OtherDownloadReason =
        "“qBittorrent”'s download “Alien remux” also writes “/media/movies/Alien.1979.Directors.Cut.2160p.mkv”, so the app can't tell the new file is this download's own.";
    private const string UnreachableReason =
        "The app couldn't ask “qBittorrent” about this download within an hour: "
        + "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.";
    private const string TooOldReason =
        "This version of “qBittorrent” can't list the files of its other downloads, so the app can't tell whether another one writes the new files.";
    private const string CrossSeedReason =
        "“qBittorrent”'s download “Alien cross-seed” also writes “/media/movies/Alien.1979.Directors.Cut.2160p.mkv”, so the app can't tell the new file is this download's own.";
    private const string UnplacedMagnetReason =
        "The app can't place “qBittorrent”'s download “Alien magnet” in its folders, so it can't tell whether that download writes the new files.";
    private const string UnplacedElsewhereReason =
        "The app can't place “qBittorrent”'s download “Alien elsewhere” in its folders, so it can't tell whether that download writes the new files.";
    private const string SwitchedOffReason = "“qBittorrent” is switched off, so the new files can't be checked.";
    private const string ClientRemovedReason =
        "The download client “qBittorrent” was removed, so the app can't confirm the new files are complete.";
    private const string TwoDownloadsReason =
        "“qBittorrent” lists two downloads for this release, so the app can't tell which one holds the new files.";
    private const string TwoLibrariesReason =
        "The new file “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” is in folders of “Фільми” and “Фільми 4K”, so the app can't tell which library should confirm it.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public ReplacementVerificationFilesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── check 1: qBittorrent's word ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("error")]
    [InlineData("missingFiles")]
    public async Task Verify_of_a_download_qbittorrent_reports_an_error_for_stops_for_review_without_reading_a_file(string state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // The download failed in qBittorrent after it was done.
        _harness.QBittorrent.UpdateTorrent(job.ClientJobId!, torrent => torrent with { State = state });

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, ErrorReason, cancellationToken);
        // Stopped before the files were listed: there is no evidence of one, and none was read.
        Assert.Empty(await _harness.NewFilesAsync(cancellationToken));
        Assert.DoesNotContain(NewPath, _factory.Files.ReadPaths);
    }

    [Fact]
    public async Task Verify_of_a_download_qbittorrent_no_longer_has_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Removed in qBittorrent, its files left on the disk.
        _harness.QBittorrent.RemoveTorrent(job.ClientJobId!);

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, GoneReason, cancellationToken);
        Assert.Empty(await _harness.NewFilesAsync(cancellationToken));
        Assert.DoesNotContain(NewPath, _factory.Files.ReadPaths);
    }

    [Fact]
    public async Task Verify_of_a_pack_one_of_whose_new_files_qbittorrent_hasnt_finished_stops_for_review_with_how_far_it_came()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Seeding at 100 % for the download, while the pack's second file is 73 % done: 100 % alone doesn't say which file
        // is complete.
        var job = await DownloadedAsync(
            cancellationToken,
            new FakeQBittorrentFile($"{PackFolder}/{NewName}", NewSize, 1.0),
            new FakeQBittorrentFile($"{PackFolder}/{BonusName}", NewSize, 0.739));
        _factory.Files.AddFile($"/media/movies/{PackFolder}/{NewName}", TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _factory.Files.AddFile($"/media/movies/{PackFolder}/{BonusName}", TestMedia.File(inode: 8, sizeBytes: NewSize), TestMedia.Taken());

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, UnfinishedReason, cancellationToken);
        // qBittorrent's word on each file is the evidence, the unfinished one saying why; neither is placed or read.
        Assert.Equal(
            [
                ($"{PackFolder}/{NewName}", (long?)NewSize, (double?)1.0, (string?)null, (DateTime?)null, (string?)null),
                ($"{PackFolder}/{BonusName}", NewSize, 0.739, null, null, UnfinishedReason),
            ],
            (await _harness.NewFilesAsync(cancellationToken))
                .Select(file => (file.RelativePath, file.ClientSizeBytes, file.ClientProgress, file.AppPath, file.CheckedUtc, file.Problem)));
        Assert.DoesNotContain(_factory.Files.ReadPaths, path => path.Contains(PackFolder, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_while_qbittorrent_is_unreachable_keeps_checking_until_the_hour_runs_out_then_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.QBittorrent.Unreachable = true;
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.FilesWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // An hour from the start of the check, every pass asking again; never a pass on an unread client.
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, beforeTheHour.Reason));
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, UnreachableReason, cancellationToken);
        Assert.Empty(await _harness.NewFilesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_while_qbittorrent_is_switched_off_keeps_checking_until_the_hour_runs_out_then_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // The owner switched qBittorrent off under Integrations once the download was done: it may be switched on again.
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _harness.Client.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var qbittorrentBefore = _factory.QBittorrentHttp.Requests.Count;
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.FilesWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, beforeTheHour.Reason));
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, SwitchedOffReason, cancellationToken);
        // Nothing asked of a switched-off client, and no file read without its word.
        Assert.Empty(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(qbittorrentBefore, _factory.QBittorrentHttp.Requests.Count);
        Assert.DoesNotContain(NewPath, _factory.Files.ReadPaths);
    }

    [Fact]
    public async Task Verify_of_a_download_whose_client_was_removed_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // The owner removed qBittorrent under Integrations: the download keeps its name, its link to the client gone.
        using var removed = await _factory.DeleteHttpRequest($"{IntegrationsUrl}/{_harness.Client.Id}", _harness.Owner.AccessToken);

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, ClientRemovedReason, cancellationToken);
        Assert.Empty(await _harness.NewFilesAsync(cancellationToken));
        Assert.DoesNotContain(NewPath, _factory.Files.ReadPaths);
    }

    [Fact]
    public async Task Verify_of_a_release_qbittorrent_lists_two_downloads_for_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // qBittorrent lists a second download carrying the release's hash: which one wrote the new files can't be told.
        _harness.QBittorrent.Intercept = request =>
            request.RequestUri!.AbsolutePath == "/api/v2/torrents/info" && request.RequestUri.Query.Contains("hashes=", StringComparison.Ordinal)
                ? TwoDownloadsOf(job.ClientJobId!)
                : null;

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, TwoDownloadsReason, cancellationToken);
        Assert.Empty(await _harness.NewFilesAsync(cancellationToken));
        Assert.DoesNotContain(NewPath, _factory.Files.ReadPaths);
    }

    // ── where the new file is ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_new_file_no_mapping_places_in_the_app_stops_for_review_as_an_unknown_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        // qBittorrent moved the download to a folder its mappings don't bring into the app.
        _harness.QBittorrent.UpdateTorrent(job.ClientJobId!, torrent => torrent with { SavePath = "/srv/elsewhere", ContentPath = "/srv/elsewhere/Alien" });

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, UnknownPathReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)null, (string?)UnknownPathReason, (DateTime?)null), (newFile.AppPath, newFile.Problem, newFile.CheckedUtc));
        // Nothing the app can't place is read.
        Assert.DoesNotContain(_factory.Files.ReadPaths, path => path.Contains("Directors", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_of_a_new_file_in_no_library_folder_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        // Moved inside the app, but out of every folder a library reports: no library could confirm it.
        _harness.QBittorrent.UpdateTorrent(
            job.ClientJobId!, torrent => torrent with { SavePath = "/downloads/elsewhere", ContentPath = "/downloads/elsewhere/Alien" });
        _factory.Files.AddFile(ElsewherePath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, NoLibraryReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((string?)ElsewherePath, (Guid?)null, (string?)null, (long?)7, (string?)NoLibraryReason),
            (newFile.AppPath, newFile.LibraryIntegrationId, newFile.LibraryPath, newFile.Inode, newFile.Problem));
    }

    [Fact]
    public async Task Verify_of_a_new_file_in_folders_of_two_libraries_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Since the schedule, the owner made Jellyfin a second library over the same folder: each would read the file, and
        // either could make another item of it.
        await new LibraryLocationSeeder(_factory.Scope).Create(_harness.Jellyfin, "Фільми 4K", ReplacementVerificationHarness.FilmsLibraryFolder);

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, TwoLibrariesReason, cancellationToken);
        // Read by the media inspector all the same, in no one library.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((string?)NewPath, (Guid?)null, (string?)null, (long?)7, (string?)TwoLibrariesReason),
            (newFile.AppPath, newFile.LibraryIntegrationId, newFile.LibraryPath, newFile.Inode, newFile.Problem));
    }

    // ── check 2: the media inspector's look ─────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_new_file_the_media_inspector_cant_read_fails_it_without_asking_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        // ffprobe fails on it.
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize));

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.Failed, UnreadableReason, cancellationToken);
        Assert.Null((await _harness.ReplacementAsync(cancellationToken)).LibraryAskedUtc);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((string?)NewPath, (Guid?)_harness.Jellyfin.Id, (string?)NewLibraryPath, (string?)UnreadableReason, (long?)null),
            (newFile.AppPath, newFile.LibraryIntegrationId, newFile.LibraryPath, newFile.Problem, newFile.DurationMilliseconds));
    }

    [Theory]
    [InlineData(0L, true, TakenMilliseconds, EmptyReason)]
    [InlineData((long)NewSize, false, TakenMilliseconds, NoVideoReason)]
    [InlineData((long)NewSize, true, null, NoRunningTimeReason)]
    [InlineData((long)NewSize, true, 0L, NoRunningTimeReason)]
    public async Task Verify_of_a_new_file_that_is_no_usable_video_fails_it(long sizeBytes, bool hasVideo, long? durationMilliseconds, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        var taken = TestMedia.Taken();
        _factory.Files.AddFile(
            NewPath,
            TestMedia.File(inode: 7, sizeBytes: sizeBytes),
            taken with
            {
                Duration = durationMilliseconds is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null,
                Tracks = [.. taken.Tracks.Where(track => hasVideo || track.Kind != MediaTrackKind.Video)],
            });

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.Failed, reason, cancellationToken);
        // What the media inspector read is kept beside why it fails.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((long?)7, (long?)sizeBytes, (string?)reason),
            (newFile.Inode, newFile.SizeBytes, newFile.Problem));
    }

    [Fact]
    public async Task Verify_of_a_new_file_that_is_the_old_copy_itself_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // qBittorrent's file is a second name of the theatrical cut — a hard link: replacing the copy by itself is no replacement.
        var job = await DownloadedAsync(cancellationToken, new FakeQBittorrentFile(NewName, TheatricalSize, 1.0));
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 1, linkCount: 2), TestMedia.Taken());

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, OldCopyItselfReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((long?)TestMedia.DeviceId, (long?)1, (string?)OldCopyItselfReason), (newFile.DeviceId, newFile.Inode, newFile.Problem));
    }

    // ── another download ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_new_file_another_download_also_writes_stops_for_review_without_asking_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // ADR 0034's gap: a download of another category, added since, saves the same file to the same folder.
        _harness.QBittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(OtherHash, null), "Alien remux", "Other", savePath: "/downloads/movies") with
        {
            Files = [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
        });

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, OtherDownloadReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)OtherDownloadReason, (long?)7, (long?)TakenMilliseconds), (newFile.Problem, newFile.Inode, newFile.DurationMilliseconds));
    }

    [Fact]
    public async Task Verify_of_a_new_file_another_download_reaches_by_a_link_at_another_path_stops_for_review_without_asking_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize, linkCount: 2), TestMedia.Taken());
        // A cross-seed of the same release in another category, saving elsewhere: its file is a hard link of the new one —
        // one file by two paths, which no comparison of paths finds.
        _harness.QBittorrent.AddTorrent(CrossSeed());
        _factory.Files.AddFile(CrossSeedPath, TestMedia.File(inode: 7, sizeBytes: NewSize, linkCount: 2));

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, CrossSeedReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)CrossSeedReason, (long?)7), (newFile.Problem, newFile.Inode));
        Assert.Contains(CrossSeedPath, _factory.Files.DescribedPaths);
    }

    [Fact]
    public async Task Verify_of_a_new_file_another_download_holds_a_file_of_its_size_of_another_identity_passes_the_files_checks()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // The same cross-seed, its file a copy of its own: the same size, another file.
        _harness.QBittorrent.AddTorrent(CrossSeed());
        _factory.Files.AddFile(CrossSeedPath, TestMedia.File(inode: 9, sizeBytes: NewSize));

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // Looked at on the disk, told apart, and Jellyfin asked to look at the new file.
        Assert.Contains(CrossSeedPath, _factory.Files.DescribedPaths);
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (replacement.State, replacement.Reason));
        Assert.NotNull(replacement.LibraryAskedUtc);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((long?)7, (string?)null), (newFile.Inode, newFile.Problem));
        Assert.Equal(
            [(AuditAction.ReplacementCheckStarted, (Guid?)null, (AuditTargetKind?)AuditTargetKind.ReplacementJob, (Guid?)replacement.Id, (string?)AlienRelease)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Equal([NewLibraryPath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Theory]
    // A magnet of the same release, added in another category, still fetching its metadata: no files listed yet.
    [InlineData("Alien magnet", "/downloads/movies", false, UnplacedMagnetReason)]
    // One saving to a folder no mapping of qBittorrent's brings into the app.
    [InlineData("Alien elsewhere", "/srv/elsewhere", true, UnplacedElsewhereReason)]
    public async Task Verify_where_another_download_cant_be_placed_keeps_checking_until_the_hour_runs_out_then_stops_for_review(
        string name, string savePath, bool hasMetadata, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // It may write the new file as well as not: an unclear answer is never an all-clear (ClientJobWithFiles).
        _harness.QBittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(OtherHash, null), name, "Other", savePath: savePath) with
        {
            HasMetadata = hasMetadata,
            Files = [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
        });
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.FilesWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // An hour from the start of the check, each pass reading the listing again; Jellyfin never asked meanwhile.
        Assert.Equal(
            (ReplacementJobState.Inspecting, (string?)null, (DateTime?)null),
            (beforeTheHour.State, beforeTheHour.Reason, beforeTheHour.LibraryAskedUtc));
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, reason, cancellationToken);
        // The new file itself passed both checks; the reason is the other download's, not the file's.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((long?)7, (string?)null), (newFile.Inode, newFile.Problem));
    }

    [Fact]
    public async Task Verify_where_qbittorrent_is_too_old_to_list_its_other_downloads_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // A qBittorrent before 5.2 follows a download, but can't list every download's files with it: waiting won't change
        // that, and nothing then says no other download writes the new file.
        _harness.QBittorrent.Version = "v5.1.2";

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertStoppedAsync(job, ReplacementJobState.NeedsReview, TooOldReason, cancellationToken);
        // The file itself passed both checks.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((long?)7, (string?)null), (newFile.Inode, newFile.Problem));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner schedules the replacement of Alien's theatrical cut by the Director's Cut release, through the real route,
    /// and qBittorrent finishes it holding <paramref name="files"/> — the director's cut alone, in the Movies category's
    /// folder, unless given —: the replacement is Downloaded.
    /// </summary>
    private async Task<DownloadJobDto> DownloadedAsync(CancellationToken cancellationToken, params FakeQBittorrentFile[] files)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        return await _harness.DownloadedAsync(
            release, _harness.Movies, [TheatricalPath], files.Length > 0 ? files : [new FakeQBittorrentFile(NewName, NewSize, 1.0)], cancellationToken);
    }

    /// <summary>
    /// A cross-seed of the release qBittorrent was given since: another download, of another category, holding the new
    /// file's name and size in <see cref="CrossSeedPath"/>'s folder.
    /// </summary>
    private static FakeQBittorrentTorrent CrossSeed() =>
        FakeQBittorrentTorrent.Of(new TorrentInfoHashes(OtherHash, null), "Alien cross-seed", "Cross-seed", savePath: "/downloads/cross-seed") with
        {
            Files = [new FakeQBittorrentFile(NewName, NewSize, 1.0)],
        };

    /// <summary>
    /// <c>torrents/info</c>'s answer listing the download qBittorrent calls <paramref name="hash"/> beside a second one that
    /// carries the same v1 info hash under another id.
    /// </summary>
    private static HttpResponseMessage TwoDownloadsOf(string hash) =>
        VendorResponses.Text(
            new JsonArray([
                .. new[] { hash, OtherHash }.Select(id => (JsonNode)new JsonObject
                {
                    ["category"] = "Movies",
                    ["content_path"] = $"/downloads/movies/{NewName}",
                    ["hash"] = id,
                    ["infohash_v1"] = hash,
                    ["infohash_v2"] = "",
                    ["name"] = NewName,
                    ["progress"] = 1.0,
                    ["save_path"] = "/downloads/movies",
                    ["state"] = "stalledUP",
                }),
            ]).ToJsonString(),
            HttpStatusCode.OK,
            "application/json");

    /// <summary>
    /// The job ended <paramref name="state"/> with <paramref name="reason"/> — on the Jobs page too —, with its two audit rows
    /// of the app's own, and nothing touched: Jellyfin never asked anything, qBittorrent only read, the old copy where it was.
    /// </summary>
    private async Task AssertStoppedAsync(DownloadJobDto job, ReplacementJobState state, string reason, CancellationToken cancellationToken)
    {
        Assert.Equal(new DownloadJobReplacementDto(state, 1, reason, false, 0), await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((state, (string?)reason), (replacement.State, replacement.Reason));
        var stopped = state == ReplacementJobState.Failed ? AuditAction.ReplacementFailed : AuditAction.ReplacementNeedsReview;
        Assert.Equal(
            [
                (AuditAction.ReplacementCheckStarted, (Guid?)null, (AuditTargetKind?)AuditTargetKind.ReplacementJob, (Guid?)replacement.Id, (string?)AlienRelease),
                (stopped, null, AuditTargetKind.ReplacementJob, replacement.Id, AlienRelease),
            ],
            await _harness.CheckAuditsAsync(cancellationToken));
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)null), (copy.PairedNewFileId, copy.Problem));
        Assert.Empty(_harness.Writes());
        Assert.Empty(_harness.JellyfinServer.ScanRequests);
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }
}
