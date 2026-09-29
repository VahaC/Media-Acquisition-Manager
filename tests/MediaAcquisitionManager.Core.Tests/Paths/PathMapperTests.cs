using System.Text.Json;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Core.Tests.Paths;

public sealed class PathMapperTests
{
    // The reference installation (ADR 0003): qBittorrent sees the media folder as /downloads, the app
    // and Jellyfin as /media.
    private static readonly PathMappingPair DownloadsToMedia = new("/downloads", "/media");

    private const string TakenPack = "Taken. Trilogy [Extended] (2008-2014) BDRemux 1080p [Hurtom]";

    [Fact]
    public void Map_without_pairs_is_the_identity()
    {
        //Arrange
        const string servicePath = "/media/movies//Чужі (1986)/Aliens.mkv";

        //Act
        var result = PathMapper.Map(servicePath, []);

        //Assert
        Assert.Equal(PathMapStatus.Identity, result.Status);
        Assert.Equal("/media/movies/Чужі (1986)/Aliens.mkv", result.AppPath);
        Assert.Null(result.Reason);
        Assert.True(result.HasAppPath);
    }

    [Fact]
    public void Map_puts_the_remainder_under_the_app_prefix()
    {
        //Arrange
        var servicePath = $"/downloads/movies/{TakenPack}/Taken (2008) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv";

        //Act
        var result = PathMapper.Map(servicePath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal($"/media/movies/{TakenPack}/Taken (2008) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", result.AppPath);
        Assert.Null(result.Reason);
        Assert.True(result.HasAppPath);
    }

    [Fact]
    public void Map_of_the_prefix_itself_is_the_app_prefix()
    {
        //Arrange
        const string servicePath = "/downloads/";

        //Act
        var result = PathMapper.Map(servicePath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/media", result.AppPath);
    }

    [Fact]
    public void Map_matches_whole_segments_only()
    {
        //Arrange
        const string servicePath = "/downloads2/movies/x.mkv";

        //Act
        var result = PathMapper.Map(servicePath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.NotCovered, result.Status);
        Assert.Null(result.AppPath);
        Assert.False(result.HasAppPath);
    }

    [Fact]
    public void Map_with_the_root_as_prefix_covers_every_path()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/", "/mnt/nas")];

        //Act
        var result = PathMapper.Map("/data/movies/x.mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/mnt/nas/data/movies/x.mkv", result.AppPath);
    }

    [Fact]
    public void Map_of_a_path_no_pair_covers_is_not_covered_not_identity()
    {
        //Arrange
        const string servicePath = "/media/movies/x.mkv";

        //Act
        var result = PathMapper.Map(servicePath, [DownloadsToMedia]);

        //Assert
        Assert.Equal(PathMapStatus.NotCovered, result.Status);
        Assert.Null(result.AppPath);
        Assert.Equal("No path mapping of this integration covers “/media/movies/x.mkv”.", result.Reason);
    }

    [Fact]
    public void Map_with_nested_pairs_that_agree_is_mapped()
    {
        //Arrange
        PathMappingPair[] pairs = [DownloadsToMedia, new("/downloads/movies", "/media/movies")];

        //Act
        var result = PathMapper.Map("/downloads/movies/x.mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/media/movies/x.mkv", result.AppPath);
    }

    [Fact]
    public void Map_with_pairs_that_disagree_is_ambiguous_and_names_both_places()
    {
        //Arrange
        // The longer prefix is not allowed to win: which one the owner meant is exactly the guess
        // ADR 0003 forbids.
        PathMappingPair[] pairs = [DownloadsToMedia, new("/downloads/movies", "/films")];

        //Act
        var result = PathMapper.Map("/downloads/movies/x.mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Ambiguous, result.Status);
        Assert.Null(result.AppPath);
        Assert.False(result.HasAppPath);
        Assert.Equal(
            "The path mappings of this integration send “/downloads/movies/x.mkv” to different places: “/media/movies/x.mkv” and “/films/x.mkv”.",
            result.Reason);
    }

    [Theory]
    [InlineData("downloads/movies/x.mkv")]
    [InlineData("/downloads/../etc/passwd")]
    [InlineData("/downloads/./x.mkv")]
    [InlineData("")]
    [InlineData("/downloads/x\n.mkv")]
    public void Map_of_a_relative_or_dotted_path_is_invalid(string servicePath)
    {
        //Arrange
        PathMappingPair[] pairs = [DownloadsToMedia];

        //Act
        var result = PathMapper.Map(servicePath, pairs);

        //Assert
        Assert.Equal(PathMapStatus.Invalid, result.Status);
        Assert.Null(result.AppPath);
        Assert.Equal($"“{servicePath}” is not a full path the app can map.", result.Reason);
    }

    [Fact]
    public void Map_of_a_windows_service_path_ignores_case_and_writes_the_app_separators()
    {
        //Arrange
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media")];

        //Act
        var result = PathMapper.Map(@"d:\TORRENTS\Movies\Taken (2008).mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/media/Movies/Taken (2008).mkv", result.AppPath);
    }

    [Fact]
    public void Map_with_nested_windows_service_pairs_onto_posix_paths_is_ambiguous_in_another_letter_case()
    {
        //Arrange
        // A hand-built row: the save-time rules refuse these pairs, because they agree only while the
        // service reports the letter case the owner typed.
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media"), new(@"D:\Torrents\movies", "/media/movies")];

        //Act
        var result = PathMapper.Map(@"D:\Torrents\Movies\Taken (2008).mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Ambiguous, result.Status);
        Assert.Null(result.AppPath);
        Assert.Equal(
            @"The path mappings of this integration send “D:\Torrents\Movies\Taken (2008).mkv” to different places: “/media/Movies/Taken (2008).mkv” and “/media/movies/Taken (2008).mkv”.",
            result.Reason);
    }

    [Fact]
    public void Map_of_a_posix_path_respects_letter_case()
    {
        //Arrange
        PathMappingPair[] pairs = [DownloadsToMedia];

        //Act
        var result = PathMapper.Map("/Downloads/movies/x.mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.NotCovered, result.Status);
    }

    [Fact]
    public void Map_of_a_posix_folder_name_with_a_backslash_under_a_windows_prefix_is_invalid()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", @"D:\Media")];

        //Act
        var result = PathMapper.Map(@"/downloads/a\b/x.mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Invalid, result.Status);
        Assert.Null(result.AppPath);
        Assert.Contains(@"“a\b”", result.Reason);
    }

    [Fact]
    public void Map_never_throws_on_a_malformed_stored_pair()
    {
        //Arrange
        PathMappingPair[] pairs =
        [
            new("downloads", "/media"),
            new("/downloads", "media"),
            new(null!, null!),
            null!,
            new("/downloads/../x", "/media"),
            DownloadsToMedia,
        ];

        //Act
        var result = PathMapper.Map("/downloads/movies/x.mkv", pairs);

        //Assert
        Assert.Equal(PathMapStatus.Mapped, result.Status);
        Assert.Equal("/media/movies/x.mkv", result.AppPath);
    }

    [Fact]
    public async Task Map_of_the_adr_0003_samples_puts_every_qbittorrent_category_inside_a_jellyfin_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var categories = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.VendorSamples, "QBittorrent", "categories.json"), cancellationToken));
        using var libraries = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.VendorSamples, "Jellyfin", "virtual-folders.json"), cancellationToken));
        var savePaths = categories.RootElement.EnumerateObject()
            .Select(category => category.Value.GetProperty("savePath").GetString()!)
            .ToList();
        var libraryLocations = libraries.RootElement.EnumerateArray()
            .SelectMany(library => library.GetProperty("Locations").EnumerateArray())
            .Select(location => NormalizedPath.TryParse(location.GetString(), out var path, out _) ? path : null)
            .OfType<NormalizedPath>()
            .ToList();

        //Act
        var mapped = savePaths.Select(savePath => PathMapper.Map(savePath, [DownloadsToMedia])).ToList();

        //Assert
        Assert.Equal(4, savePaths.Count);
        Assert.All(mapped, result =>
        {
            Assert.Equal(PathMapStatus.Mapped, result.Status);
            Assert.True(NormalizedPath.TryParse(result.AppPath, out var appPath, out _));
            Assert.Contains(libraryLocations, location => appPath.IsUnder(location));
        });
    }
}
