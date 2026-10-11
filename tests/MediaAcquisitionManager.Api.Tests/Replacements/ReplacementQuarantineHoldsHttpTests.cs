using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0031 — check 6, right before the move: one read of every download client's torrents with their files
// (qBittorrent's GET torrents/info?includeFiles=true, nothing else sent to it, nothing written), for every name the move
// would rename. A video any torrent lists — seeding, queued or stopped — stops the job for review naming the torrent, each
// of several once, until the owner removes it in qBittorrent without its files and presses Retry; a torrent's file the
// app can't place (a save path no pair maps), a client that doesn't answer, one switched off and a qBittorrent too old to
// list files are uncertain and stop it the same way. A companion a torrent lists stays where it is, named on the job, and
// its video moves; a second name of a torrent's file moves, its row saying another name keeps the data. Once the torrent
// is gone and the job is retried, the next pass moves it. The moves themselves are ReplacementQuarantineHttpTests'.
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
        var seedbox = await AddSeedboxAsync();
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

    /// <summary>A second qBittorrent, "seedbox", whose <c>/downloads</c> is the app's <c>/media</c> too, armed beside the
    /// harness's one.</summary>
    private async Task<FakeQBittorrentServer> AddSeedboxAsync()
    {
        var seedbox = new FakeQBittorrentServer(SeedboxUrl);
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _harness.QBittorrent, seedbox);
        await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "qbittorrent",
            "seedbox",
            seedbox.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", ReplacementVerificationHarness.MediaRoot)]);
        return seedbox;
    }

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
        var stopped = await _harness.ReplacementAsync(cancellationToken);
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
