using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-43 — a Blu-ray or DVD Jellyfin reports by its folder: the sync lists the clips in the disc's BDMV or VIDEO_TS
// folder inside the owner's media folders and stores them as the version's files, in Jellyfin's terms and in its
// library, so the Library page shows them, the Jellyfin row counts their room, and the folder scan no longer lists them
// in no library. A folder that doesn't map, lies outside the media folders or is away leaves the version without a file
// and the sync succeeds; a version Jellyfin names a file of is never walked. Every sync is the real runner against the
// fake Jellyfin, every walk the fake inventory, every scan and look the owner's real requests and workers.
public sealed class LibraryDiscClipsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";
    private const string DiscId = "d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1d1";
    private const string DiscFolder = "/media/movies/DIRTY_DOZEN_1967_RMX";
    private const string FirstClip = DiscFolder + "/BDMV/STREAM/00000.m2ts";
    private const string SecondClip = DiscFolder + "/BDMV/STREAM/00001.m2ts";
    // A video beside the disc's own folders: Jellyfin doesn't take it for the disc, nor does the sync.
    private const string SampleBeside = DiscFolder + "/Sample/dirty.dozen.sample.mkv";
    private const long FirstClipSizeBytes = 21_914_664_960;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private readonly FakeJellyfinServer _jellyfin = new() { ApiKey = ApiKey };
    private AuthorizedUser _owner = null!;

    public LibraryDiscClipsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_stores_the_clips_of_a_blu_ray_folder_as_its_versions_files_in_its_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await ArrangeDiscAsync();

        //Act
        var run = await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (run.Status, run.FilesCount));
        var files = await FilesAsync(cancellationToken);
        // Named in Jellyfin's terms, by Jellyfin, in Фільми — as any file it reports; no size, which it didn't report.
        Assert.Equal([FirstClip, SecondClip], files.Select(file => file.ServicePath));
        Assert.All(files, file => Assert.Equal(
            (ProvenanceSourceKind.MediaLibrary, (Guid?)livingRoom.Id, DiscId, (long?)null),
            (file.SourceKind, file.SourceConnectionId, file.SourceItemId, file.SizeBytes)));
        var movies = await LibraryAsync("Фільми", cancellationToken);
        Assert.All(files, file => Assert.Equal(movies, file.LibraryLocationId));
        Assert.Equal([DiscFolder], _factory.Files.ListedFolders);
    }

    [Fact]
    public async Task Works_list_shows_the_disc_film_with_its_clips_as_its_versions_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await ArrangeDiscAsync();
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest("/api/library/works", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var film = Assert.Single(list.Items);
        var version = Assert.Single(film.Versions);
        Assert.Equal(("The Dirty Dozen", 1967), (film.Title, film.Year));
        Assert.Equal(
            [(FirstClip, FirstClip, PathMapStatus.Identity, "Фільми"), (SecondClip, SecondClip, PathMapStatus.Identity, "Фільми")],
            version.Files.Select(file => (file.ServicePath, file.AppPath, file.PathStatus, file.LocationName)));
    }

    [Fact]
    public async Task Scan_after_the_sync_lists_no_clip_of_the_disc_in_no_library_and_the_jellyfin_row_counts_their_room()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await ArrangeDiscAsync();
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Act
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);

        //Assert
        var unreported = await ReadUnreportedAsync(cancellationToken);
        // The clips are Jellyfin's now. MAM-44 — the sample beside the disc's folders, in a folder named Sample, is an
        // extra: in no library, yet counted with the extras rather than warned of.
        Assert.Empty(unreported.Items);
        var extras = await ReadUnreportedAsync(cancellationToken, "?view=Extras");
        Assert.Equal([SampleBeside], extras.Items.Select(file => file.AppPath));
        var sources = await ReadSourcesAsync(cancellationToken);
        var jellyfinRow = Assert.Single(sources.Items);
        Assert.Equal((FirstClipSizeBytes + 2_000_000_000L, 2), (jellyfinRow.Space.SizeBytes, jellyfinRow.Space.FileCount));
        Assert.Equal((1, 0, 1), (sources.Scan.Space.FileCount, sources.Scan.Files.InspectedCount, sources.Scan.ExtrasCount));
    }

    [Fact]
    public async Task Sync_stores_the_vobs_of_a_dvd_folder_and_nothing_outside_video_ts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string dvd = "/media/movies/Casablanca (1942) DVD";
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(dvd + "/VIDEO_TS/VTS_01_1.VOB", TestMedia.File(inode: 301));
        _factory.Files.AddFile(dvd + "/VIDEO_TS/VTS_01_2.VOB", TestMedia.File(inode: 302));
        _factory.Files.AddFile(dvd + "/Extras/trailer.mkv", TestMedia.File(inode: 303));
        _jellyfin.Movies.Add(JellyfinSamples.DiscMovieWith(DiscId, "Casablanca", 1942, dvd, videoType: "Dvd"));
        var livingRoom = await JellyfinAsync();

        //Act
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Assert
        Assert.Equal(
            [dvd + "/VIDEO_TS/VTS_01_1.VOB", dvd + "/VIDEO_TS/VTS_01_2.VOB"],
            (await FilesAsync(cancellationToken)).Select(file => file.ServicePath));
    }

    [Fact]
    public async Task Sync_names_the_clips_in_jellyfins_terms_when_its_paths_map_into_the_app()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string jellyfinFolder = "/data/movies/DIRTY_DOZEN_1967_RMX";
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(FirstClip, TestMedia.File(inode: 201));
        _jellyfin.Movies.Add(JellyfinSamples.DiscMovieWith(DiscId, "The Dirty Dozen", 1967, jellyfinFolder));
        var livingRoom = await JellyfinAsync([new PathMappingPair("/data", "/media")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Act
        await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);

        //Assert
        // Walked where the app finds it, stored where Jellyfin has it, and the scan takes it for Jellyfin's.
        Assert.Equal([DiscFolder], _factory.Files.ListedFolders);
        Assert.Equal([jellyfinFolder + "/BDMV/STREAM/00000.m2ts"], (await FilesAsync(cancellationToken)).Select(file => file.ServicePath));
        Assert.Empty((await ReadUnreportedAsync(cancellationToken)).Items);
    }

    [Fact]
    public async Task Sync_gives_a_disc_outside_the_media_folders_no_file_and_succeeds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media/tv-shows");
        _factory.Files.AddFile(FirstClip, TestMedia.File(inode: 201));
        _jellyfin.Movies.Add(JellyfinSamples.DiscMovieWith(DiscId, "The Dirty Dozen", 1967, DiscFolder));
        var livingRoom = await JellyfinAsync();

        //Act
        var run = await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 0), (run.Status, run.VersionsCount, run.FilesCount));
        Assert.Empty(await FilesAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_with_no_media_folder_allowed_walks_nothing_and_gives_the_disc_no_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.Files.AddFile(FirstClip, TestMedia.File(inode: 201));
        _jellyfin.Movies.Add(JellyfinSamples.DiscMovieWith(DiscId, "The Dirty Dozen", 1967, DiscFolder));
        var livingRoom = await JellyfinAsync();

        //Act
        var run = await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Empty(await FilesAsync(cancellationToken));
        Assert.Empty(_factory.Files.ListedFolders);
    }

    [Fact]
    public async Task Sync_walks_no_folder_for_a_film_jellyfin_names_a_file_of()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string path = "/media/movies/Dune (2021)/Dune (2021).mkv";
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(path, TestMedia.File(inode: 401));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(new string('d', 32), "Дюна", "Dune", 2021, path, "tt1160419", "438631"));
        var livingRoom = await JellyfinAsync();

        //Act
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, _owner.User);

        //Assert
        Assert.Empty(_factory.Files.ListedFolders);
        Assert.Equal([path], (await FilesAsync(cancellationToken)).Select(file => file.ServicePath));
    }

    [Fact]
    public async Task Sync_again_forgets_a_clip_gone_from_the_disc_and_every_clip_once_jellyfin_drops_the_disc()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await ArrangeDiscAsync();
        var snapshots = new LibrarySnapshotSeeder(_factory.Scope);
        await snapshots.Sync(livingRoom, _owner.User);
        var first = (await FilesAsync(cancellationToken)).Single(file => file.ServicePath == FirstClip);

        //Act
        _factory.Files.Remove(SecondClip);
        await snapshots.Sync(livingRoom, _owner.User);
        var afterClipGone = await FilesAsync(cancellationToken);
        _jellyfin.Movies.Clear();
        await snapshots.Sync(livingRoom, _owner.User);

        //Assert
        // The clip still there keeps its row, as any file found again (ADR 0008).
        Assert.Equal([(first.Id, FirstClip)], afterClipGone.Select(file => (file.Id, file.ServicePath)));
        Assert.Empty(await FilesAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Dirty Dozen as production holds it: a Blu-ray remux folder in Фільми with two clips and a sample beside the
    /// disc's folders, all in the media folders, and Jellyfin listing the folder as one film.
    /// </summary>
    private async Task<IntegrationConnectionEntity> ArrangeDiscAsync()
    {
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        _factory.Files.AddFile(FirstClip, TestMedia.File(inode: 201, sizeBytes: FirstClipSizeBytes), TestMedia.Taken());
        _factory.Files.AddFile(SecondClip, TestMedia.File(inode: 202, sizeBytes: 2_000_000_000), TestMedia.Taken());
        _factory.Files.AddFile(SampleBeside, TestMedia.File(inode: 203, sizeBytes: 50_000_000), TestMedia.Taken());
        _jellyfin.Movies.Add(JellyfinSamples.DiscMovieWith(DiscId, "The Dirty Dozen", 1967, DiscFolder, imdbId: "tt0061578"));
        return await JellyfinAsync();
    }

    private Task<IntegrationConnectionEntity> JellyfinAsync(IReadOnlyList<PathMappingPair>? pathMappings = null) =>
        new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey, pathMappings: pathMappings);

    /// <summary>Every name a library reported, in path order (ordinal).</summary>
    private async Task<List<PhysicalFileEntity>> FilesAsync(CancellationToken cancellationToken)
    {
        var rows = new List<PhysicalFileEntity>();
        await _factory.WithDbAsync(async db => rows.AddRange(
            (await db.PhysicalFiles.AsNoTracking().Where(file => file.MediaVersionId != null).ToListAsync(cancellationToken))
            .OrderBy(file => file.ServicePath, StringComparer.Ordinal)));
        return rows;
    }

    private async Task<Guid> LibraryAsync(string name, CancellationToken cancellationToken)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.LibraryLocations.Where(location => location.Name == name).Select(location => location.Id).SingleAsync(cancellationToken));
        return id;
    }

    private async Task<UnreportedFileListResponse> ReadUnreportedAsync(CancellationToken cancellationToken, string query = "")
    {
        var response = await _factory.GetHttpRequest("/api/library/unreported-files" + query, _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UnreportedFileListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<LibrarySourceListResponse> ReadSourcesAsync(CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest("/api/library/sources", _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
