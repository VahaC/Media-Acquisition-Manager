using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Core.Tests.Paths;

// MAM-30 — the way back (ADR 0039): where a folder of the app is as a download client sees it, through that client's
// path mappings, the mirror of Map step for step — every pair whose app prefix covers the path is applied, agreeing
// ones collapse by the service's own rules, and two places are Ambiguous, never settled by the longest prefix. And the
// property the destination list relies on: what maps back maps forward onto itself, for every set of pairs the save
// rules accept.
public sealed class PathMapperBackTests
{
    // The reference installation (ADR 0003): the download client sees the media folder as /downloads, the app and
    // Jellyfin as /media.
    private static readonly PathMappingPair DownloadsToMedia = new("/downloads", "/media");

    private static readonly IntegrationAdapterDescriptor DownloadClient = new(
        "transmission",
        "Transmission",
        IntegrationKind.DownloadClient,
        UserName: CredentialRequirement.Optional,
        Password: CredentialRequirement.Optional,
        ApiKey: CredentialRequirement.NotUsed,
        ReportsFilePaths: true);

    [Fact]
    public void MapBack_without_pairs_is_the_identity()
    {
        //Arrange
        const string appPath = "/media/movies//Чужі (1986)/";

        //Act
        var result = PathMapper.MapBack(appPath, []);

        //Assert
        Assert.Equal(PathMapStatus.Identity, result.Status);
        Assert.Equal("/media/movies/Чужі (1986)", result.ServicePath);
        Assert.Null(result.Reason);
        Assert.True(result.HasServicePath);
    }

    [Fact]
    public void MapBack_puts_the_remainder_under_the_service_prefix()
    {
        //Arrange
        const string appPath = "/media/movies";

        //Act
        var result = PathMapper.MapBack(appPath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/downloads/movies", result.ServicePath);
        Assert.Null(result.Reason);
        Assert.True(result.HasServicePath);
    }

    [Fact]
    public void MapBack_of_the_app_prefix_itself_is_the_service_prefix()
    {
        //Arrange
        const string appPath = "/media/";

        //Act
        var result = PathMapper.MapBack(appPath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/downloads", result.ServicePath);
    }

    [Fact]
    public void MapBack_matches_whole_segments_only()
    {
        //Arrange
        const string appPath = "/media2/movies";

        //Act
        var result = PathMapper.MapBack(appPath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.NotCovered, result.Status);
        Assert.Null(result.ServicePath);
        Assert.False(result.HasServicePath);
    }

    [Theory]
    // The app's root as the app prefix covers every app path.
    [InlineData("/data", "/", "/media/movies", "/data/media/movies")]
    // The service's root as the service prefix takes the whole remainder.
    [InlineData("/", "/mnt/nas", "/mnt/nas/movies", "/movies")]
    [InlineData("/", "/mnt/nas", "/mnt/nas", "/")]
    public void MapBack_with_the_root_as_a_prefix_covers_every_path(string servicePrefix, string appPrefix, string appPath, string expected)
    {
        //Arrange
        PathMappingPair[] pairs = [new(servicePrefix, appPrefix)];

        //Act
        var result = PathMapper.MapBack(appPath, pairs);

        //Assert
        Assert.Equal((PathMapStatus.Mapped, expected), (result.Status, result.ServicePath));
    }

    [Fact]
    public void MapBack_of_a_path_no_pair_leads_to_is_not_covered_not_identity()
    {
        //Arrange
        // Jellyfin's Collections folder: the client was never told where it is.
        const string appPath = "/config/data/collections";

        //Act
        var result = PathMapper.MapBack(appPath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.NotCovered, result.Status);
        Assert.Null(result.ServicePath);
        Assert.Equal("No path mapping of this integration leads to “/config/data/collections”.", result.Reason);
    }

    [Fact]
    public void MapBack_with_nested_pairs_that_agree_is_mapped()
    {
        //Arrange
        PathMappingPair[] pairs = [DownloadsToMedia, new("/downloads/movies", "/media/movies")];

        //Act
        var result = PathMapper.MapBack("/media/movies/Чужі (1986)", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/downloads/movies/Чужі (1986)", result.ServicePath);
    }

    [Fact]
    public void MapBack_with_two_service_folders_onto_one_app_folder_is_ambiguous_and_names_both()
    {
        //Arrange
        // Two mounts of one folder in the client: fine forward, but which one the client should write to is a guess.
        PathMappingPair[] pairs = [DownloadsToMedia, new("/data", "/media")];

        //Act
        var result = PathMapper.MapBack("/media/movies", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Ambiguous, result.Status);
        Assert.Null(result.ServicePath);
        Assert.False(result.HasServicePath);
        Assert.Equal(
            "The path mappings of this integration lead to “/media/movies” from different places: “/downloads/movies” and “/data/movies”.",
            result.Reason);
    }

    [Fact]
    public void MapBack_with_nested_app_prefixes_whose_service_prefixes_do_not_nest_is_ambiguous()
    {
        //Arrange
        PathMappingPair[] pairs = [DownloadsToMedia, new("/incoming", "/media/movies")];

        //Act
        var movies = PathMapper.MapBack("/media/movies", pairs);
        var cartoons = PathMapper.MapBack("/media/cartoons", pairs);

        //Assert
        // The longer app prefix never wins on its own; a folder only the outer pair covers is still mapped.
        Assert.Equal(
            "The path mappings of this integration lead to “/media/movies” from different places: “/downloads/movies” and “/incoming”.",
            movies.Reason);
        Assert.Equal(PathMapStatus.Ambiguous, movies.Status);
        Assert.Equal((PathMapStatus.Mapped, "/downloads/cartoons"), (cartoons.Status, cartoons.ServicePath));
    }

    [Theory]
    [InlineData("media/movies")]
    [InlineData("/media/../etc")]
    [InlineData("/media/./movies")]
    [InlineData("")]
    [InlineData("/media/mo\nvies")]
    public void MapBack_of_a_relative_or_dotted_path_is_invalid(string appPath)
    {
        //Arrange
        PathMappingPair[] pairs = [DownloadsToMedia];

        //Act
        var result = PathMapper.MapBack(appPath, pairs);

        //Assert
        Assert.Equal(PathMapStatus.Invalid, result.Status);
        Assert.Null(result.ServicePath);
        Assert.Equal($"“{appPath}” is not a full path the app can map back.", result.Reason);
    }

    [Fact]
    public void MapBack_of_a_posix_app_path_respects_letter_case()
    {
        //Arrange
        // ext4: /media is not /Media.
        PathMappingPair[] pairs = [new("/downloads", "/Media")];

        //Act
        var result = PathMapper.MapBack("/media/movies", pairs);

        //Assert
        Assert.Equal(PathMapStatus.NotCovered, result.Status);
    }

    [Fact]
    public void MapBack_keeps_the_letter_case_of_the_app_path_below_the_prefix()
    {
        //Arrange

        //Act
        var result = PathMapper.MapBack("/media/Movies", [DownloadsToMedia]);

        //Assert
        Assert.Equal("/downloads/Movies", result.ServicePath);
    }

    [Theory]
    [InlineData(@"D:\Torrents", @"D:\Torrents\movies")]
    [InlineData(@"d:/torrents/", @"D:\torrents\movies")]
    [InlineData(@"\\nas\media", @"\\nas\media\movies")]
    public void MapBack_onto_a_windows_service_prefix_writes_its_separators(string servicePrefix, string expected)
    {
        //Arrange
        // Transmission on Windows, the app and Jellyfin in containers.
        PathMappingPair[] pairs = [new(servicePrefix, "/media")];

        //Act
        var result = PathMapper.MapBack("/media/movies", pairs);

        //Assert
        Assert.Equal((PathMapStatus.Mapped, expected), (result.Status, result.ServicePath));
    }

    [Fact]
    public void MapBack_with_windows_service_pairs_collapses_two_spellings_of_one_folder()
    {
        //Arrange
        // The service's own rules decide: on Windows these are one folder, and the first pair's spelling is kept.
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media"), new(@"d:\TORRENTS\movies", "/media/movies")];

        //Act
        var result = PathMapper.MapBack("/media/movies", pairs);

        //Assert
        Assert.Equal((PathMapStatus.Mapped, @"D:\Torrents\movies"), (result.Status, result.ServicePath));
    }

    [Fact]
    public void MapBack_of_a_posix_folder_name_with_a_backslash_onto_a_windows_service_prefix_is_invalid()
    {
        //Arrange
        // A legal ext4 name that a Windows path would read as two folders.
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media")];

        //Act
        var result = PathMapper.MapBack(@"/media/a\b", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Invalid, result.Status);
        Assert.Null(result.ServicePath);
        Assert.Equal(
            @"“/media/a\b” can't be written under “D:\Torrents”: its folder name “a\b” contains \, which separates folders in a Windows path.",
            result.Reason);
    }

    [Fact]
    public void MapBack_never_throws_on_a_malformed_stored_pair()
    {
        //Arrange
        PathMappingPair[] pairs =
        [
            new("downloads", "/media"),
            new("/downloads", "media"),
            new(null!, null!),
            null!,
            new("/downloads", "/media/../x"),
            DownloadsToMedia,
        ];

        //Act
        var result = PathMapper.MapBack("/media/movies", pairs);

        //Assert
        Assert.Equal((PathMapStatus.Mapped, "/downloads/movies"), (result.Status, result.ServicePath));
    }

    [Fact]
    public void MapBack_with_nested_posix_service_pairs_onto_windows_app_paths_is_ambiguous_in_another_letter_case()
    {
        //Arrange
        // The mirror of ADR 0003's hazard, only when the app runs on Windows: the save rules accept these pairs, and the
        // app path's letter case then decides which POSIX folder each leads to.
        PathMappingPair[] pairs = [new("/downloads", @"D:\Media"), new("/downloads/movies", @"D:\Media\movies")];

        //Act
        var result = PathMapper.MapBack(@"D:\Media\Movies", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Ambiguous, result.Status);
        Assert.Equal(
            @"The path mappings of this integration lead to “D:\Media\Movies” from different places: “/downloads/Movies” and “/downloads/movies”.",
            result.Reason);
    }

    [Fact]
    public async Task MapBack_of_the_adr_0003_samples_puts_every_jellyfin_library_into_the_client_folder_of_its_category()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var categories = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.VendorSamples, "QBittorrent", "categories.json"), cancellationToken));
        using var libraries = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.VendorSamples, "Jellyfin", "virtual-folders.json"), cancellationToken));
        var categoryFolders = categories.RootElement.EnumerateObject()
            .Select(category => category.Value.GetProperty("savePath").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
        // The Jellyfin integration has no mappings, so each root folder is the app's as it is.
        var libraryFolders = libraries.RootElement.EnumerateArray()
            .SelectMany(library => library.GetProperty("Locations").EnumerateArray())
            .Select(location => location.GetString()!)
            .ToList();

        //Act
        var mapped = libraryFolders.Select(folder => (Folder: folder, Result: PathMapper.MapBack(folder, [DownloadsToMedia]))).ToList();

        //Assert
        // The four media libraries land in the four folders qBittorrent's categories save to; the Collections library
        // keeps Jellyfin's own records, which no mapping leads to.
        Assert.Equal(
            categoryFolders,
            mapped.Where(entry => entry.Result.HasServicePath).Select(entry => entry.Result.ServicePath!).Order(StringComparer.Ordinal));
        var collections = Assert.Single(mapped, entry => !entry.Result.HasServicePath);
        Assert.Equal(("/config/data/collections", PathMapStatus.NotCovered), (collections.Folder, collections.Result.Status));
    }

    [Fact]
    public void MapBack_then_Map_lands_on_the_same_app_folder_for_every_set_of_pairs_the_save_rules_accept()
    {
        //Arrange
        PathMappingPair[][] pairSets =
        [
            [],
            [DownloadsToMedia],
            [DownloadsToMedia, new("/downloads/movies", "/media/movies")],
            [DownloadsToMedia, new("/data", "/media")],
            [DownloadsToMedia, new("/incoming", "/media/movies")],
            [DownloadsToMedia, new("/tv", "/media/tv-shows"), new("/downloads/cartoons", "/media/cartoons")],
            [new("/", "/mnt/nas")],
            [new("/data", "/")],
            [new(@"D:\Torrents", "/media")],
            [new(@"\\nas\media", "/media")],
        ];
        string[] appPaths =
        [
            "/", "/media", "/media/movies", "/media/Movies", "/media/movies/Чужі (1986)", "/media/cartoons", "/media/tv-shows/Show/Season 1",
            "/mnt/nas/movies", "/config/data/collections",
        ];
        var accepted = pairSets.Where(pairs => PathMappingRules.Validate(pairs, DownloadClient) is null).ToList();

        //Act
        var checkedPaths = new List<string>();
        var failures = new List<string>();
        foreach (var pairs in accepted)
        {
            foreach (var appPath in appPaths)
            {
                if (PathMapper.MapBack(appPath, pairs) is not { HasServicePath: true, ServicePath: { } servicePath })
                    continue;

                checkedPaths.Add(appPath);
                var forward = PathMapper.Map(servicePath, pairs);
                if (forward.AppPath != appPath)
                    failures.Add($"{appPath} → {servicePath} → {forward.AppPath ?? forward.Reason}");
            }
        }

        //Assert
        // Every set is one the rules accept, and enough folders went both ways that an empty run can't pass.
        Assert.Equal(pairSets.Length, accepted.Count);
        Assert.True(checkedPaths.Count >= 40, $"Only {checkedPaths.Count} app paths mapped back.");
        Assert.Empty(failures);
    }
}
