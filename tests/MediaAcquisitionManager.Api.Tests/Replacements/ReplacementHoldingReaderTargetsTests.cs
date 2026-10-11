using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0031 — check 6's read right before a move (IReplacementHoldingReader's names overload), asked the way the mover
// asks it, from a scope of its own: every name the move would rename — the video and its companions —, each held by any
// torrent that lists it, at its own path whatever size the torrent lists; and only the download clients whose mappings reach
// the allowed folders, a client whose every mapping leads elsewhere left out unasked rather than counted uncertain. A client
// without mappings names the app's own paths and is asked. The read only reads. The Download dialog's read of the same
// clients is ReplacementOptionsHoldingsHttpTests'.
public sealed class ReplacementHoldingReaderTargetsTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string MediaRoot = "/media";
    private const string VideoPath = "/media/movies/Alien (1979)/Alien (1979).mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979).uk.srt";
    private const string InfoPath = "/media/movies/Alien (1979)/Alien (1979).nfo";
    private const long VideoSize = 3_891_719_630;
    private const long SubtitleSize = 84_211;
    private const long InfoSize = 1_500;
    private const string SeedingHash = "1111111111111111111111111111111111111111";
    private const string ErroredHash = "2222222222222222222222222222222222222222";
    private const string ElsewhereHash = "3333333333333333333333333333333333333333";
    private const string IncludeFilesCall = "GET /api/v2/torrents/info?includeFiles=true";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private FakeQBittorrentServer _seedbox = null!;

    public ReplacementHoldingReaderTargetsTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _qbittorrent = new FakeQBittorrentServer();
        _seedbox = new FakeQBittorrentServer("http://seedbox.test:8080");
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, _seedbox);
        // qBittorrent's /downloads is the app's /media.
        await CreateClientAsync("qBittorrent", _qbittorrent, [new PathMappingPair("/downloads", MediaRoot)]);
        // The film, its subtitle and its .nfo, each a file of its own.
        _factory.Files.AddFile(VideoPath, TestMedia.File(inode: 1, sizeBytes: VideoSize), TestMedia.Taken());
        _factory.Files.AddFile(SubtitlePath, TestMedia.File(inode: 2, sizeBytes: SubtitleSize));
        _factory.Files.AddFile(InfoPath, TestMedia.File(inode: 3, sizeBytes: InfoSize));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Read_of_the_names_before_a_move_marks_the_video_and_each_companion_a_torrent_lists_and_only_reads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The release's torrent lists the film and its subtitle; the .nfo is the owner's own, which no torrent lists.
        _qbittorrent.AddTorrent(Torrent(
            "Alien.1979", SeedingHash, "/downloads/movies", ("Alien (1979)/Alien (1979).mkv", VideoSize), ("Alien (1979)/Alien (1979).uk.srt", SubtitleSize)));

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken);

        //Assert
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        Assert.Equal(
            new Dictionary<string, string>
            {
                [SubtitlePath] = $"qBittorrent/{SeedingHash}/Alien.1979/False",
                [VideoPath] = $"qBittorrent/{SeedingHash}/Alien.1979/False",
            },
            Described(holdings));
        // One listing of the whole client, and nothing but reads.
        Assert.Single(QBittorrentCalls(), call => call == IncludeFilesCall);
        Assert.All(QBittorrentCalls(), call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.Empty(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_marks_a_name_a_torrent_lists_at_its_path_whatever_size_it_lists()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The .nfo the owner edited since the torrent wrote it: the torrent still lists its first size, and a recheck would
        // write it back. And an errored torrent that lists the film at its path with no size it can tell.
        _qbittorrent.AddTorrent(Torrent("Alien.1979.Extras", SeedingHash, "/downloads/movies", ("Alien (1979)/Alien (1979).nfo", 1_200)));
        _qbittorrent.AddTorrent(Torrent("Alien.1979", ErroredHash, "/downloads/movies", ("Alien (1979)/Alien (1979).mkv", -1)) with { State = "error" });

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken);

        //Assert
        // ADR 0031: an old copy any torrent lists, in any state. Its place is known, so neither leaves the client uncertain.
        Assert.Equal(
            new Dictionary<string, string>
            {
                [InfoPath] = $"qBittorrent/{SeedingHash}/Alien.1979.Extras/False",
                [VideoPath] = $"qBittorrent/{ErroredHash}/Alien.1979/False",
            },
            Described(holdings));
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_leaves_out_a_client_whose_mappings_reach_no_allowed_root_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A seedbox whose every mapping leads outside /media, with a torrent saving where none of them reaches — which the
        // dialog's read would count as a torrent it couldn't place —, and another one switched off the same way.
        _seedbox.AddTorrent(Torrent("Elsewhere", ElsewhereHash, "/elsewhere", ("Alien.mkv", VideoSize)));
        await CreateClientAsync("seedbox", _seedbox, [new PathMappingPair("/downloads", "/srv/seedbox"), new PathMappingPair("/data", "/mnt/other")]);
        await CreateClientAsync("old seedbox", new FakeQBittorrentServer("http://old.test:8080"), [new PathMappingPair("/downloads", "/srv/old")], isEnabled: false);
        _qbittorrent.AddTorrent(Torrent("Alien.1979", SeedingHash, "/downloads/movies", ("Alien (1979)/Alien (1979).mkv", VideoSize)));

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken);

        //Assert
        // ADR 0031 reads "each download client whose paths reach the allowed folders": the others are neither read nor
        // uncertain.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.Error, client.UnplacedTorrents)));
        Assert.Equal([VideoPath], Described(holdings).Keys);
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, request => request.Uri.Host is "seedbox.test" or "old.test");
    }

    [Theory]
    [InlineData("/media")]
    [InlineData("/media/movies")]
    [InlineData("/")]
    public async Task Read_of_the_names_before_a_move_asks_a_client_with_a_mapping_under_or_holding_an_allowed_root(string appPrefix)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One mapping of two reaching /media is enough: the same folder, one inside it, or one holding it.
        _seedbox.AddTorrent(Torrent("Elsewhere", ElsewhereHash, "/elsewhere", ("Alien.mkv", VideoSize)));
        await CreateClientAsync("seedbox", _seedbox, [new PathMappingPair("/data", "/srv/seedbox"), new PathMappingPair("/downloads", appPrefix)]);

        //Act
        var holdings = await ReadAsync([VideoPath], cancellationToken);

        //Assert
        // Asked, and its torrent saving where no mapping reaches is a place it couldn't tell.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, 0), ("seedbox", ReplacementClientCheck.Partial, 1)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents)));
        Assert.Contains(_factory.QBittorrentHttp.Requests, request => request.Uri.Host == "seedbox.test" && request.Uri.Query.Contains("includeFiles", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_asks_a_client_without_mappings_whose_paths_are_the_apps_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A seedbox that saves straight to the app's own /media: no mapping, and every path is the app's.
        _seedbox.AddTorrent(Torrent("Alien.1979", ElsewhereHash, "/media/movies", ("Alien (1979)/Alien (1979).uk.srt", SubtitleSize)));
        await CreateClientAsync("seedbox", _seedbox, []);

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath], cancellationToken);

        //Assert
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, 0), ("seedbox", ReplacementClientCheck.Read, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents)));
        Assert.Equal(new Dictionary<string, string> { [SubtitlePath] = $"seedbox/{ElsewhereHash}/Alien.1979/False" }, Described(holdings));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Check 6's read of the names at <paramref name="paths"/>, each with its identity on the disk now, inside
    /// <see cref="MediaRoot"/>, from a scope of its own as the mover's pass asks it.</summary>
    private async Task<ReplacementHoldings> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var targets = new List<ReplacementHoldingTarget>();
        foreach (var path in paths)
        {
            var described = await _factory.Files.DescribeAsync(path, [MediaRoot], cancellationToken);
            NormalizedPath.TryParse(path, out var appPath, out _);
            targets.Add(new ReplacementHoldingTarget(appPath!, described.Value!));
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReplacementHoldingReader>().ReadAsync(targets, [MediaRoot], cancellationToken);
    }

    private Task CreateClientAsync(string name, FakeQBittorrentServer server, IReadOnlyList<PathMappingPair> pathMappings, bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            name,
            server.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: isEnabled,
            pathMappings: pathMappings);

    /// <summary>A torrent of the Movies category saving to <paramref name="savePath"/>, listing <paramref name="files"/>
    /// (each under its root folder) with the sizes given; a negative size is one qBittorrent can't tell.</summary>
    private static FakeQBittorrentTorrent Torrent(string name, string hash, string savePath, params (string File, long Size)[] files) =>
        FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), name, "Movies", savePath: savePath)
            with { Files = [.. files.Select(file => new FakeQBittorrentFile(file.File, file.Size, 1))] };

    /// <summary>Each held name's holds as <c>client/torrent id/torrent/second name</c>, by path.</summary>
    private static Dictionary<string, string> Described(ReplacementHoldings holdings) =>
        holdings.Holds.ToDictionary(
            held => held.Key.ToString(),
            held => string.Join(", ", held.Value.Select(hold => $"{hold.ClientName}/{hold.TorrentId}/{hold.TorrentName}/{hold.SecondName}")));

    private string[] QBittorrentCalls() =>
        [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];
}
