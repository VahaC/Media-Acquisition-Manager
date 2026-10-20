using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0031 — check 6's read right before a move (IReplacementHoldingReader's names overload), asked the way the mover
// asks it, from a scope of its own: every name the move would rename — the video and its companions —, each held by any
// torrent that lists it, at its own path whatever size the torrent lists; and only the download clients whose mappings reach
// the allowed folders, a client whose every mapping leads elsewhere left out unasked rather than counted uncertain. A client
// without mappings names the app's own paths and is asked. MAM-28 — but a client the app sent a replacement to is asked
// whatever its mappings say, and a torrent of it listing, outside the allowed folders, a file of a name's file name (any
// letter case), of no size, or of a name's size unless that name's file has several names — then it is the torrent's own
// name of it, which the move leaves seeding — reads it in part, never looked at there, each such torrent counted once and
// logged on a line of its own. The read only reads. The Download
// dialog's read of the same clients is ReplacementOptionsHoldingsHttpTests'.
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
    private const string ElsewhereRoot = "/srv/elsewhere";
    // MAM-28 — a torrent's own name of the video, another name of the same file.
    private const string TwinFile = "Alien.1979.1080p.BluRay.mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private FakeQBittorrentServer _seedbox = null!;
    private Guid _qbittorrentId;

    public ReplacementHoldingReaderTargetsTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _qbittorrent = new FakeQBittorrentServer();
        _seedbox = new FakeQBittorrentServer("http://seedbox.test:8080");
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, _seedbox);
        // qBittorrent's /downloads is the app's /media.
        _qbittorrentId = (await CreateClientAsync("qBittorrent", _qbittorrent, [new PathMappingPair("/downloads", MediaRoot)])).Id;
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
            holdings.Clients.Select(client => (client.ClientName, client.Status, TestTexts.En(client.Error), client.UnplacedTorrents)));
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
            holdings.Clients.Select(client => (client.ClientName, client.Status, TestTexts.En(client.Error), client.UnplacedTorrents)));
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_leaves_out_a_client_the_app_never_sent_a_replacement_to_whose_mappings_reach_no_allowed_root_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A seedbox whose every mapping leads outside /media, with a torrent saving where none of them reaches — which the
        // dialog's read would count as a torrent it couldn't place —, and another one switched off the same way. The app
        // sent a replacement to qBittorrent only (MAM-28): a seedbox saving elsewhere stops no move.
        _seedbox.AddTorrent(Torrent("Elsewhere", ElsewhereHash, "/elsewhere", ("Alien.mkv", VideoSize)));
        await CreateClientAsync("seedbox", _seedbox, [new PathMappingPair("/downloads", "/srv/seedbox"), new PathMappingPair("/data", "/mnt/other")]);
        await CreateClientAsync("old seedbox", new FakeQBittorrentServer("http://old.test:8080"), [new PathMappingPair("/downloads", "/srv/old")], isEnabled: false);
        _qbittorrent.AddTorrent(Torrent("Alien.1979", SeedingHash, "/downloads/movies", ("Alien (1979)/Alien (1979).mkv", VideoSize)));

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken, [_qbittorrentId]);

        //Assert
        // ADR 0031 reads "each download client whose paths reach the allowed folders": the others are neither read nor
        // uncertain.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, TestTexts.En(client.Error), client.UnplacedTorrents)));
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

    // ── a client the app sent a replacement to (MAM-28) ─────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(ElsewhereRoot)]
    public async Task Read_of_the_names_before_a_move_asks_a_client_the_app_sent_a_replacement_to_whatever_its_mappings_and_counts_a_torrent_they_put_outside_the_allowed_roots(
        string? appPrefix)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // qBittorrent's mapping deleted (its paths the app's own then) or pointed outside /media: the release's torrent, listing
        // the film and its subtitle at /downloads/movies, lies outside every allowed root now.
        await RemapQBittorrentAsync(appPrefix, cancellationToken);
        _qbittorrent.AddTorrent(Torrent(
            "Alien.1979", SeedingHash, "/downloads/movies", ("Alien (1979)/Alien (1979).mkv", VideoSize), ("Alien (1979)/Alien (1979).uk.srt", SubtitleSize)));

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken, [_qbittorrentId]);

        //Assert
        // Read in part, as the torrent can't be ruled out — counted once, however many of its files could be a name —; none
        // held, as nothing was looked at there.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Partial, (string?)null, 0, 1)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, TestTexts.En(client.Error), client.UnplacedTorrents, client.OutsideTorrents)));
        Assert.Empty(holdings.Holds);
        // One listing of the whole client, nothing but reads, and the disk never asked about a path outside /media.
        Assert.Single(QBittorrentCalls(), call => call == IncludeFilesCall);
        Assert.All(QBittorrentCalls(), call => Assert.True(call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));
        Assert.All(_factory.Files.DescribedPaths, path => Assert.StartsWith(MediaRoot + "/", path, StringComparison.Ordinal));
        // The log: MAM-26's line as it was, and the count outside the roots on a line of its own, ids and counts only.
        Assert.Equal(
            [
                $"Holdings listing of integration {_qbittorrentId} (qbittorrent) read 1 torrents; 0 could not be placed.",
                $"Holdings listing of integration {_qbittorrentId} (qbittorrent), a client the app sent a replacement to, found 1 torrents listing a file "
                + "outside the allowed folders that may be an old copy.",
            ],
            HoldingsLines());
    }

    [Theory]
    [InlineData(null, "Alien.1979.mkv", VideoSize, 1)]
    [InlineData(null, "Alien.1979.uk.srt", SubtitleSize, 1)]
    [InlineData(null, "Alien.1979.mkv", -1L, 1)]
    [InlineData(null, "ALIEN (1979).NFO", 1_200L, 1)]
    [InlineData(null, "Sample.mkv", 1_234_567L, 0)]
    [InlineData(ElsewhereRoot, "Alien.1979.mkv", VideoSize, 1)]
    [InlineData(ElsewhereRoot, "ALIEN (1979).NFO", 1_200L, 1)]
    [InlineData(ElsewhereRoot, "Sample.mkv", 1_234_567L, 0)]
    public async Task Read_of_the_names_before_a_move_counts_a_torrent_outside_the_allowed_roots_only_for_a_file_of_a_names_file_name_or_size_or_of_no_size(
        string? appPrefix, string file, long size, int outside)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // qBittorrent's mapping deleted, or pointed outside /media — then it is asked only because the app sent a replacement
        // to it: one torrent outside /media listing one file — of the video's size, of the subtitle's, of a size qBittorrent
        // can't tell, of the .nfo's own name in capitals at another size, or like none of them.
        await RemapQBittorrentAsync(appPrefix, cancellationToken);
        _qbittorrent.AddTorrent(Torrent("Alien.1979", SeedingHash, "/downloads/movies", ($"Alien (1979)/{file}", size)));

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken, [_qbittorrentId]);

        //Assert
        // Only a file that could be a name counts; one like none of them leaves the client read — asked all the same.
        Assert.Equal(
            [("qBittorrent", outside > 0 ? ReplacementClientCheck.Partial : ReplacementClientCheck.Read, 0, outside)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents, client.OutsideTorrents)));
        Assert.Empty(holdings.Holds);
        Assert.Single(QBittorrentCalls(), call => call == IncludeFilesCall);
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_counts_no_torrent_outside_the_allowed_roots_for_a_client_the_app_never_sent_a_replacement_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A seedbox reaching /media through /downloads, whose /data is the app's /srv/seedbox: a torrent there lists a file of
        // the video's name and size. The app sent a replacement to qBittorrent only.
        _seedbox.AddTorrent(Torrent("Alien.1979", ElsewhereHash, "/data/movies", ("Alien (1979)/Alien (1979).mkv", VideoSize)));
        var seedbox = await CreateClientAsync("seedbox", _seedbox, [new PathMappingPair("/downloads", MediaRoot), new PathMappingPair("/data", "/srv/seedbox")]);

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken, [_qbittorrentId]);

        //Assert
        // The MAM-26 rule for it: a file outside the allowed roots holds nothing and leaves nothing uncertain; and nothing
        // counted outside the roots is logged for it.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, 0, 0), ("seedbox", ReplacementClientCheck.Read, 0, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents, client.OutsideTorrents)));
        Assert.Empty(holdings.Holds);
        Assert.Equal(
            [
                $"Holdings listing of integration {_qbittorrentId} (qbittorrent) read 0 torrents; 0 could not be placed.",
                $"Holdings listing of integration {_qbittorrentId} (qbittorrent), a client the app sent a replacement to, found 0 torrents listing a file "
                + "outside the allowed folders that may be an old copy.",
                $"Holdings listing of integration {seedbox.Id} (qbittorrent) read 1 torrents; 0 could not be placed.",
            ],
            HoldingsLines());
    }

    [Theory]
    [InlineData(2, TwinFile, 0)]
    [InlineData(1, TwinFile, 1)]
    [InlineData(null, TwinFile, 1)]
    [InlineData(2, "Alien (1979)/Alien (1979).mkv", 1)]
    public async Task Read_of_the_names_before_a_move_counts_a_torrent_outside_the_allowed_roots_for_a_client_the_app_sent_a_replacement_to_whose_mappings_also_reach_them_but_not_a_second_name_of_a_video_with_several_by_its_size(
        int? linkCount, string file, int outside)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The same seedbox, but the app sent a replacement to it: its /data torrent lists, outside /media, a file of the
        // video's size — under the torrent's own name, as /data/torrents hardlinked into /data/media seeds it, or under the
        // video's own name —, while its /downloads still reaches /media. The video has two names, one, or a number the disk
        // doesn't tell.
        _factory.Files.AddFile(VideoPath, TestMedia.File(inode: 1, sizeBytes: VideoSize) with { LinkCount = linkCount }, TestMedia.Taken());
        _seedbox.AddTorrent(Torrent("Alien.1979", ElsewhereHash, "/data/movies", (file, VideoSize)));
        var seedbox = await CreateClientAsync("seedbox", _seedbox, [new PathMappingPair("/downloads", MediaRoot), new PathMappingPair("/data", "/srv/seedbox")]);

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken, [_qbittorrentId, seedbox.Id]);

        //Assert
        // Whether or not its mappings still reach the allowed roots, a file there that may be a name is never ruled out — but
        // a file only of the size of a video with several names is the torrent's own name of it, which moving the video's
        // leaves seeding: the move goes ahead. Of its name, or of a video with one name or a number unknown, it may be the
        // video itself.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, 0, 0), ("seedbox", outside > 0 ? ReplacementClientCheck.Partial : ReplacementClientCheck.Read, 0, outside)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, client.UnplacedTorrents, client.OutsideTorrents)));
        Assert.Empty(holdings.Holds);
        Assert.All(_factory.Files.DescribedPaths, path => Assert.StartsWith(MediaRoot + "/", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_counts_no_torrent_whose_file_a_mapping_puts_at_a_bare_root_for_its_name_and_never_throws()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // qBittorrent's mapping deleted, and an answer no real torrent gives: saving to / a file named only by a separator,
        // which places it at the root itself — no file name to compare —, at a size no name has.
        await RemapQBittorrentAsync(null, cancellationToken);
        _qbittorrent.AddTorrent(Torrent("Odd", ElsewhereHash, "/", ("/", 1_234_567)));

        //Act
        var holdings = await ReadAsync([VideoPath, SubtitlePath, InfoPath], cancellationToken, [_qbittorrentId]);

        //Assert
        // The reader's word: it never throws but for the caller's cancellation. A root names no file, so the file is no
        // name's by its name, and its size is none of theirs.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0, 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, TestTexts.En(client.Error), client.UnplacedTorrents, client.OutsideTorrents)));
        Assert.Empty(holdings.Holds);
    }

    [Fact]
    public async Task Read_of_the_names_before_a_move_answers_for_a_switched_off_client_the_app_sent_a_replacement_to_whatever_its_mappings()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A seedbox the app sent a replacement to, its mapping since pointed outside /media, and switched off: it may still
        // run and seed.
        var seedbox = await CreateClientAsync("seedbox", _seedbox, [new PathMappingPair("/downloads", ElsewhereRoot)], isEnabled: false);

        //Act
        var holdings = await ReadAsync([VideoPath], cancellationToken, [_qbittorrentId, seedbox.Id]);

        //Assert
        // Not read, with the app's sentence, rather than left out; and never asked.
        Assert.Equal(
            [("qBittorrent", ReplacementClientCheck.Read, (string?)null, 0), ("seedbox", ReplacementClientCheck.NotRead, "“seedbox” is switched off.", 0)],
            holdings.Clients.Select(client => (client.ClientName, client.Status, TestTexts.En(client.Error), client.OutsideTorrents)));
        Assert.DoesNotContain(_factory.QBittorrentHttp.Requests, request => request.Uri.Host == "seedbox.test");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Check 6's read of the names at <paramref name="paths"/>, each with its identity on the disk now, inside
    /// <see cref="MediaRoot"/>, from a scope of its own as the mover's pass asks it; <paramref name="libraryClients"/> are
    /// the clients the app sent a replacement to (none unless given).</summary>
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

    /// <summary>MAM-28 — qBittorrent's one mapping deleted (<paramref name="appPrefix"/> <c>null</c>), or its app side
    /// pointed at <paramref name="appPrefix"/>.</summary>
    private Task RemapQBittorrentAsync(string? appPrefix, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db =>
        {
            var mappings = db.PathMappings.Where(mapping => mapping.IntegrationConnectionId == _qbittorrentId);
            return appPrefix is null
                ? mappings.ExecuteDeleteAsync(cancellationToken)
                : mappings.ExecuteUpdateAsync(setters => setters.SetProperty(mapping => mapping.AppPrefix, appPrefix), cancellationToken);
        });

    private Task<IntegrationConnectionEntity> CreateClientAsync(
        string name, FakeQBittorrentServer server, IReadOnlyList<PathMappingPair> pathMappings, bool isEnabled = true) =>
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

    /// <summary>MAM-28 — the reader's log lines of the clients it read, in their order.</summary>
    private string[] HoldingsLines() =>
    [
        .. _factory.Logs.Entries
            .Where(entry => entry.Category == typeof(ReplacementHoldingReader).FullName && entry.Message.Contains(" torrents", StringComparison.Ordinal))
            .Select(entry => entry.Message),
    ];
}
