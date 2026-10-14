using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-30, D11 — check 6's read right before a move, of a client that keeps an unfinished file under its name and .part
// (Transmission), asked the way the mover asks it: a torrent's file holds its .part name too, whatever size it lists;
// the disk is read at the .part name when the file isn't at its own yet, so a file that is only there is placed, not
// left uncertain; and for a client the app sent a replacement to, a file outside the allowed folders counts as maybe a
// name when that name is its name or its name with .part. ReplacementQuarantinePartialFileHttpTests runs the move.
public sealed class ReplacementHoldingReaderPartialFilesTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string MediaRoot = "/media";
    private const string VideoPath = "/media/movies/Alien (1979)/Alien (1979).mkv";
    private const string PartPath = "/media/movies/Alien (1979)/Alien (1979).Extended.mkv.part";
    private const long VideoSize = 3_891_719_630;
    private const long PartSize = 900_000_000;
    private const string DownloadingHash = "1111111111111111111111111111111111111111";
    private const string ElsewhereRoot = "/srv/elsewhere";

    private readonly FolderClientApiFactory _factory;
    private Guid _clientId;

    public ReplacementHoldingReaderPartialFilesTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        // Transmission's /downloads is the app's /media.
        _clientId = (await new IntegrationConnectionSeeder(_factory.Scope).Create(
            FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", MediaRoot)])).Id;
        _factory.FolderClient.Arm();
        // The film, and beside it the unfinished download of another cut, named like its companion.
        _factory.Files.AddFile(VideoPath, TestMedia.File(inode: 1, sizeBytes: VideoSize), TestMedia.Taken());
        _factory.Files.AddFile(PartPath, TestMedia.File(inode: 2, sizeBytes: PartSize));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Read_of_the_names_before_a_move_holds_a_name_that_is_a_torrent_files_unfinished_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Of another size than on the disk: the client writes into it as it downloads.
        _factory.FolderClient.AddTorrent(Downloading("/downloads/movies/Alien (1979)", ("Alien (1979).Extended.mkv", 4_000_000_000)));

        //Act
        var holdings = await ReadAsync([VideoPath, PartPath], cancellationToken);

        //Assert
        Assert.Equal(new Dictionary<string, string> { [PartPath] = $"Transmission/{DownloadingHash}/Alien.1979.Extended/False" }, Described(holdings));
        Assert.Equal(
            [("Transmission", ReplacementClientCheck.Read, 0, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents, client.OutsideTorrents)));
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_finds_a_torrent_file_at_its_unfinished_name_when_it_is_not_at_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The film's own file, adopted by a torrent saving in another folder under another name: on the disk only as that
        // name with .part while the client checks it — one file, reached through a link.
        _factory.FolderClient.AddTorrent(Downloading("/downloads/torrents", ("Alien.1979.mkv", VideoSize)));
        _factory.Files.AddFile("/media/torrents/Alien.1979.mkv.part", TestMedia.File(inode: 1, sizeBytes: VideoSize));

        //Act
        var holdings = await ReadAsync([VideoPath, PartPath], cancellationToken);

        //Assert
        // Placed by its .part name, so the client is read whole and the film held.
        Assert.Equal(new Dictionary<string, string> { [VideoPath] = $"Transmission/{DownloadingHash}/Alien.1979.Extended/False" }, Described(holdings));
        Assert.Equal(
            [("Transmission", ReplacementClientCheck.Read, 0, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents, client.OutsideTorrents)));
        Assert.Contains("/media/torrents/Alien.1979.mkv.part", _factory.Files.DescribedPaths);
    }

    [Theory]
    [InlineData("Alien (1979).Extended.mkv", 1)]
    [InlineData("Alien (1979).Extended.mkv.part", 1)]
    [InlineData("Alien (1979).Sample.mkv", 0)]
    public async Task Read_of_the_names_before_a_move_counts_a_torrent_outside_the_allowed_roots_by_a_names_file_name_with_or_without_its_suffix(
        string file, int outside)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The client's mapping pointed outside /media since the app sent it a replacement: its torrents' files lie there.
        await _factory.WithDbAsync(db => db.PathMappings
            .Where(mapping => mapping.IntegrationConnectionId == _clientId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(mapping => mapping.AppPrefix, ElsewhereRoot), cancellationToken));
        _factory.FolderClient.AddTorrent(Downloading("/downloads/movies/Alien (1979)", (file, 1_234_567)));

        //Act
        var holdings = await ReadAsync([VideoPath, PartPath], cancellationToken, [_clientId]);

        //Assert
        // A name ending in .part may be the torrent's own file behind the mapping: the torrent can't be ruled out.
        Assert.Equal(
            [("Transmission", outside > 0 ? ReplacementClientCheck.Partial : ReplacementClientCheck.Read, outside)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.OutsideTorrents)));
        Assert.Empty(holdings.Holds);
        Assert.DoesNotContain(_factory.Files.DescribedPaths, path => path.StartsWith(ElsewhereRoot, StringComparison.Ordinal));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The read of <paramref name="paths"/> right before a move, inside <see cref="MediaRoot"/>, from a scope of its own as
    /// the mover's pass asks it; <paramref name="libraryClients"/> are those the app sent a replacement to.
    /// </summary>
    private async Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<string> paths, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? libraryClients = null)
    {
        var targets = new List<ReplacementHoldingTarget>();
        foreach (var path in paths)
        {
            var described = await _factory.Files.DescribeAsync(path, [MediaRoot], cancellationToken);
            NormalizedPath.TryParse(path, out var appPath, out _);
            targets.Add(new ReplacementHoldingTarget(appPath!, described.Value!));
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReplacementHoldingReader>().ReadAsync(targets, [MediaRoot], libraryClients ?? [], cancellationToken);
    }

    /// <summary>A torrent still downloading into <paramref name="downloadDir"/>, of one file directly in it.</summary>
    private static FakeFolderTorrent Downloading(string downloadDir, (string Name, long Size) file) =>
        new(DownloadingHash, "Alien.1979.Extended", downloadDir, [new ClientJobFile(file.Name, file.Size, 0.2, true)], ClientJobState.Downloading, 0.2);

    /// <summary>Each held name's holds as <c>client/torrent id/torrent/second name</c>, by path.</summary>
    private static Dictionary<string, string> Described(ReplacementHoldings holdings) =>
        holdings.Holds.ToDictionary(
            held => held.Key.ToString(),
            held => string.Join(", ", held.Value.Select(hold => $"{hold.ClientName}/{hold.TorrentId}/{hold.TorrentName}/{hold.SecondName}")));
}
