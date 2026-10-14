using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-30, ADR 0031, ADR 0032 — a replacement through a real Transmission, end to end: the real adapter over HTTP to a
// FakeTransmissionServer (ReplacementVerificationHarness of Transmission). The owner's schedule sends one torrent-add into
// the Movies destination's folder with its name as the label; Transmission finishing it makes the replacement Downloaded,
// the new file is verified, and the old copy moves into the quarantine — not while a Transmission torrent lists it, which
// stops the job for review naming the torrent until the owner removes it there and presses Retry; an unfinished download
// beside the old copy, its file on the disk with .part, stays where it is (D11). The Download dialog's held marks come from
// Transmission's whole listing; a .torrent whose places no reading can predict is refused with why, nothing sent. Nothing
// but that one add is ever written to Transmission.
public sealed class ReplacementTransmissionFlowHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalSrtPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.srt";
    private const long TheatricalSize = 3_891_719_630;
    private const long SrtSize = 51_234;
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const string LookRequest = "POST /Library/Media/Updated";

    // A torrent of the theatrical cut seeding in Transmission's Movies folder, its files below its own folder.
    private const string HoldingTorrent = "Alien (1979)";
    private const string TheatricalInTorrent = "Alien (1979)/Alien (1979) Theatrical Cut.mkv";

    // A single-file torrent downloading into the film's own folder: its file is that name with .part until it is done.
    private const string DownloadingName = "Alien (1979) Theatrical Cut.2160p.mkv";
    private const string DownloadingPartPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.2160p.mkv.part";

    // Sentences the Jobs page and the dialog show, spelled out rather than read from product code.
    private const string HeldReason =
        "Still held by the torrent “Alien (1979)” in “Transmission”: remove that torrent there without its files, then press Retry.";
    private const string PartHeldProblem = "Held by the torrent “Alien (1979) Theatrical Cut.2160p.mkv” in “Transmission”, so it stays where it is.";
    private const string RefusedBeforeSentReason = "The download was refused before it was sent, so nothing will be replaced.";
    private const string FileListUncheckedError =
        "The app couldn't check every place this release would write in the folder of the destination “Movies” (the .torrent's list of files can't be read), so nothing was sent and no copy will be replaced. Pick Keep both to download it.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public ReplacementTransmissionFlowHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory, ReplacementHarnessClient.Transmission);
        // Alien's theatrical cut, inspected, in the ticked Фільми, with its subtitles beside it.
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        _factory.Files.AddFile(TheatricalSrtPath, TestMedia.File(inode: 3, sizeBytes: SrtSize));
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Flow_sends_one_add_into_the_movies_folder_verifies_the_new_file_and_moves_the_old_copy_once_the_torrent_holding_it_is_removed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        var adds = _harness.Transmission.AddRequests;
        var holding = AddHoldingTorrent();

        //Act
        await QuarantineAsync(cancellationToken);
        var heldLine = await _harness.LineAsync(job.Id, cancellationToken);
        var renamesWhileHeld = _factory.Quarantine.Renames.Count;
        // The owner removes the torrent in Transmission without its files, then presses Retry on the job.
        _harness.Transmission.RemoveTorrent(holding.HashString);
        using var retried = await _factory.PostHttpRequest($"{BaseUrl}/{replacement.Id}/retry", _harness.Owner.AccessToken);
        _factory.Time.Advance(ReplacementQuarantineTracker.PassInterval);
        await QuarantineAsync(cancellationToken);

        //Assert
        // The one add: the Movies destination's folder, its name as the torrent's label, started.
        var add = Assert.Single(adds);
        Assert.Equal(
            ((string?)ReplacementVerificationHarness.TransmissionMoviesFolder, (bool?)false),
            (add.DownloadDir, add.Paused));
        Assert.Equal(["Movies"], add.Labels!);
        Assert.Equal(DownloadOverwriteCheck.Checked, (await DownloadRowAsync(cancellationToken)).OverwriteCheck);

        // Held: stopped for review by the mover naming the torrent and Transmission, nothing renamed.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, HeldReason, false, 0)
            {
                Id = replacement.Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Move,
            },
            heldLine);
        Assert.Equal(0, renamesWhileHeld);

        // Retried and moved: both names in the quarantine, the job at rest there.
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Quarantined, 1, null, false, 0) { Id = replacement.Id, QuarantinedCopyCount = 1, CanRestore = true, CanDeleteForGood = true },
            await _harness.LineAsync(job.Id, cancellationToken));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath)),
                (TheatricalSrtPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalSrtPath)),
            ],
            _factory.Quarantine.Renames);

        // ADR 0031 — the owner removed the torrent in Transmission, never the app: since the verified download only reads,
        // and the library heard of the move.
        Assert.Equal([LookRequest], _harness.Writes());
        Assert.Equal(["torrent-add"], _harness.Transmission.Writes.Select(call => call.Method));
    }

    [Fact]
    public async Task Quarantine_leaves_an_unfinished_transmission_download_beside_the_old_copy_where_it_is_and_moves_the_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        // Another cut downloading into the film's own folder: a fifth of it there, under its name with .part.
        _harness.Transmission.AddTorrent(
            HashOf(DownloadingName),
            DownloadingName,
            "/downloads/movies/Alien (1979)",
            files: [new FakeTransmissionFile(DownloadingName, 4_000_000_000, 800_000_000)]);
        _factory.Files.AddFile(DownloadingPartPath, TestMedia.File(inode: 9, sizeBytes: 800_000_000));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        // D11 — Transmission's file is the .part on the disk until it is done: moving it would lose the torrent its data.
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameKind.Video, ReplacementNameState.Quarantined, (string?)null),
                (DownloadingPartPath, ReplacementNameKind.Companion, ReplacementNameState.Stayed, PartHeldProblem),
                (TheatricalSrtPath, ReplacementNameKind.Companion, ReplacementNameState.Quarantined, (string?)null),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.Kind, name.State, name.Problem)));
        Assert.DoesNotContain(_factory.Quarantine.Renames, rename => rename.Item1 == DownloadingPartPath);
        Assert.Equal(
            FileInventoryStatus.Found,
            (await _factory.Files.DescribeAsync(DownloadingPartPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken)).Status);
        Assert.Equal([LookRequest], _harness.Writes());
    }

    [Fact]
    public async Task Options_with_holdings_mark_the_copy_a_transmission_torrent_lists_and_only_read_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        AddHoldingTorrent();

        //Act
        var response = await _factory.GetHttpRequest(
            $"{BaseUrl}/options?releaseId={release.Id}&destinationId={_harness.Movies.Id}&holdings=true", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Null(options.Unavailable);
        Assert.Equal(
            [("Transmission", ReplacementClientCheck.Read, (string?)null, 0)],
            options.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        var copy = Assert.Single(options.Works.SelectMany(work => work.Copies));
        Assert.Equal(
            (TheatricalPath, (ReplacementCopyUnavailableReason?)null),
            (copy.File.AppPath, copy.Unavailable));
        Assert.Equal([("Transmission", HoldingTorrent, false)], copy.Holds.Select(hold => (hold.ClientName, hold.TorrentName, hold.SecondName)));
        // One listing of every torrent with its files, and nothing written.
        Assert.Single(_harness.Transmission.Calls, call => call.Method == "torrent-get" && call.Arguments["fields"]!.ToJsonString().Contains("\"files\"", StringComparison.Ordinal));
        Assert.Empty(_harness.Transmission.Writes);
    }

    [Fact]
    public async Task Schedule_of_a_torrent_whose_places_no_reading_can_predict_is_refused_saying_why_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        _harness.Feed.TorrentFile = WindowsCyrillicTorrent();
        var theatrical = await _harness.FileIdAsync(TheatricalPath, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl,
            _harness.Owner.AccessToken,
            new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _harness.Client.Id, _harness.Movies.Id, [theatrical]));

        //Assert
        // ADR 0032 — a replacement goes only after a check that looked everywhere: the owner picks Keep both himself.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, (string?)FileListUncheckedError), (job.Status, job.Error));
        var row = await DownloadRowAsync(cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, (string?)FileListUncheckedError, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App), (row.Status, row.Error, row.ErrorOrigin));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Failed, (string?)RefusedBeforeSentReason), (replacement.State, replacement.Reason));
        Assert.Empty(_harness.Transmission.AddRequests);
        Assert.Empty(_harness.Transmission.Writes);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner's replacement of Alien's theatrical cut by the Director's Cut release through Transmission, verified: the
    /// new film downloaded into the Movies destination's folder, on the disk, and identified by Jellyfin as Alien.
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
        return (job, await _harness.ReplacementAsync(cancellationToken));
    }

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

    /// <summary>The one download job there is, as stored now.</summary>
    private async Task<DownloadJobEntity> DownloadRowAsync(CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken));
        return row!;
    }

    /// <summary>A torrent of the theatrical cut, seeding in Transmission's Movies folder, as someone added it there.</summary>
    private FakeTransmissionTorrent AddHoldingTorrent() =>
        _harness.Transmission.AddTorrent(
            HashOf(HoldingTorrent),
            HoldingTorrent,
            ReplacementVerificationHarness.TransmissionMoviesFolder,
            files: [new FakeTransmissionFile(TheatricalInTorrent, TheatricalSize, TheatricalSize)]);

    /// <summary>A torrent's hashes made of its name.</summary>
    private static TorrentInfoHashes HashOf(string name) =>
        new(Convert.ToHexStringLower(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(name))), null);

    /// <summary>
    /// A torrent of one file under the folder Alien, its subfolder named in Windows-1251 bytes ("Сезон 1") with no
    /// <c>path.utf-8</c> beside it, as old torrents hold it.
    /// </summary>
    private static byte[] WindowsCyrillicTorrent()
    {
        var content = TestTorrent.Bytes(10, seed: 3);
        return TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>
            {
                TestTorrent.Dictionary(("length", 10L), ("path", new List<object> { new byte[] { 0xD1, 0xE5, 0xE7, 0xEE, 0xED, 0x20, 0x31 }, NewName })),
            }),
            ("name", "Alien"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", SHA1.HashData(content))))));
    }
}
