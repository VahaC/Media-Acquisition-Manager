using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
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

// MAM-30, D11 — a client that keeps an unfinished file under its name and .part (Transmission) holds that name: beside an
// old copy, a download still in progress whose name starts like the copy's is taken for the copy's companion, and moving
// it into the quarantine would lose the torrent its data. Read right before the move, the torrent's file holds its .part
// name, so it stays where it is, named on the job, and the old copy moves; a .part no torrent lists moves with its video
// as any companion does. The replacement itself went through qBittorrent (ReplacementVerificationHarness); the folder
// client is a second download client the read asks.
public sealed class ReplacementQuarantinePartialFileHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;

    // A single-file torrent downloading into the film's own folder: its file is that name with .part until it is done.
    private const string DownloadingName = "Alien (1979) Theatrical Cut.2160p.mkv";
    private const string DownloadingPartPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.2160p.mkv.part";
    private const string DownloadingHash = "5555555555555555555555555555555555555555";
    private const string HeldProblem = "Held by the torrent “Alien (1979) Theatrical Cut.2160p.mkv” in “Transmission”, so it stays where it is.";

    private readonly FolderClientApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public ReplacementQuarantinePartialFileHttpTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Quarantine_leaves_an_unfinished_download_beside_the_old_copy_where_it_is_and_moves_the_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (job, replacement) = await VerifiedAsync(cancellationToken);
        await AddFolderClientAsync();
        _factory.FolderClient.AddTorrent(new FakeFolderTorrent(
            DownloadingHash,
            DownloadingName,
            "/downloads/movies/Alien (1979)",
            [new ClientJobFile(DownloadingName, 4_000_000_000, 0.2, true)],
            ClientJobState.Downloading,
            0.2));
        _factory.Files.AddFile(DownloadingPartPath, TestMedia.File(inode: 9, sizeBytes: 800_000_000));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        var line = await _harness.LineAsync(job.Id, cancellationToken);
        Assert.Equal((ReplacementJobState.Quarantined, (string?)null), (line!.State, line.Reason));
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameKind.Video, ReplacementNameState.Quarantined, (string?)null),
                (DownloadingPartPath, ReplacementNameKind.Companion, ReplacementNameState.Stayed, HeldProblem),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.Kind, name.State, name.Problem)));
        // Only the old copy was renamed; the download in progress is where the client writes it.
        Assert.Equal([(TheatricalPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, TheatricalPath))], _factory.Quarantine.Renames);
        Assert.Equal(
            FileInventoryStatus.Found,
            (await _factory.Files.DescribeAsync(DownloadingPartPath, [ReplacementVerificationHarness.MediaRoot], cancellationToken)).Status);
        // Read, never written to.
        Assert.Equal(["ListJobsWithFiles"], _factory.FolderClient.Calls);
    }

    [Fact]
    public async Task Quarantine_moves_a_part_file_no_torrent_lists_with_its_video()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, replacement) = await VerifiedAsync(cancellationToken);
        await AddFolderClientAsync();
        // What a download removed from the client left behind: nobody's file, so the copy's companion like any other.
        _factory.Files.AddFile(DownloadingPartPath, TestMedia.File(inode: 9, sizeBytes: 800_000_000));

        //Act
        await QuarantineAsync(cancellationToken);

        //Assert
        Assert.Equal(
            [
                (TheatricalPath, ReplacementNameKind.Video, ReplacementNameState.Quarantined),
                (DownloadingPartPath, ReplacementNameKind.Companion, ReplacementNameState.Quarantined),
            ],
            (await NamesAsync(cancellationToken)).Select(name => (name.AppPath, name.Kind, name.State)));
        Assert.Contains((DownloadingPartPath, ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, DownloadingPartPath)), _factory.Quarantine.Renames);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The owner's replacement of the theatrical cut by the director's cut through qBittorrent, verified.</summary>
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

    /// <summary>A Transmission-like folder client whose <c>/downloads</c> is the app's <c>/media</c>, answering.</summary>
    private async Task AddFolderClientAsync()
    {
        await new IntegrationConnectionSeeder(_factory.Scope).Create(
            FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", ReplacementVerificationHarness.MediaRoot)]);
        _factory.FolderClient.Arm();
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
}
