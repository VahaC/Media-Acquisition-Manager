using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-8 — which Jellyfin library holds a file: one root folder covering it folder by folder, or no
// library at all rather than a guess.
public sealed class LibraryLocationMatcherTests
{
    // The media libraries of VendorSamples/Jellyfin/virtual-folders.json.
    private static readonly LibraryLocation Movies = new("78ee018b5034640ba414833f0fe9d6b3", "Фільми", ["/media/movies"]);
    private static readonly LibraryLocation Cartoons = new("2e7d31f7c627f72ad0912eff1c00353d", "Мульфільми", ["/media/cartoons"]);
    private static readonly LibraryLocation Collections = new("f28915b8cd74d585b0c181975ec68c3a", "Колекції", ["/config/data/collections"]);

    private static readonly LibraryLocation[] Sample = [Movies, Cartoons, Collections];

    [Fact]
    public void Find_is_the_library_whose_root_holds_the_file()
    {
        //Arrange
        const string servicePath =
            "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, Sample);

        //Assert
        Assert.Same(Movies, location);
    }

    [Fact]
    public void Find_matches_whole_folder_names_only()
    {
        //Arrange
        const string servicePath = "/media/movies2/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, Sample);

        //Assert
        Assert.Null(location);
    }

    [Fact]
    public void Find_of_a_file_under_no_root_is_null()
    {
        //Arrange
        const string servicePath = "/downloads/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, Sample);

        //Assert
        Assert.Null(location);
    }

    [Fact]
    public void Find_of_a_file_under_the_roots_of_two_libraries_is_null()
    {
        //Arrange
        // Nested roots: both cover the file, and picking the deeper one would be a guess.
        LibraryLocation everything = new("all", "Усе", ["/media"]);
        const string servicePath = "/media/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, [everything, Movies]);

        //Assert
        Assert.Null(location);
    }

    [Fact]
    public void Find_of_a_file_under_two_roots_of_one_library_is_that_library()
    {
        //Arrange
        LibraryLocation twoRoots = new("both", "Фільми", ["/media", "/media/movies"]);
        const string servicePath = "/media/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, [twoRoots, Cartoons]);

        //Assert
        Assert.Same(twoRoots, location);
    }

    [Fact]
    public void Find_of_the_root_folder_itself_is_its_library()
    {
        //Arrange
        const string servicePath = "/media/movies/";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, Sample);

        //Assert
        Assert.Same(Movies, location);
    }

    [Fact]
    public void Find_compares_windows_paths_in_any_letter_case()
    {
        //Arrange
        LibraryLocation windows = new("win", "Movies", [@"D:\Media\Movies"]);
        const string servicePath = @"d:/media/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, [windows]);

        //Assert
        Assert.Same(windows, location);
    }

    [Fact]
    public void Find_compares_posix_paths_with_their_letter_case()
    {
        //Arrange
        // ext4: /Media is not /media.
        LibraryLocation posix = new("posix", "Movies", ["/Media/Movies"]);
        const string servicePath = "/media/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, [posix]);

        //Assert
        Assert.Null(location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("movies/Aliens (1986).mkv")]
    [InlineData("/media/movies/../cartoons/x.mkv")]
    [InlineData("/media/movies/Aliens\u0007 (1986).mkv")]
    public void Find_of_a_path_it_can_not_parse_is_null(string servicePath)
    {
        //Arrange
        LibraryLocation root = new("root", "Everything", ["/"]);

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, [root]);

        //Assert
        Assert.Null(location);
    }

    [Fact]
    public void Find_skips_a_root_it_can_not_parse()
    {
        //Arrange
        LibraryLocation unparseable = new("bad", "Broken", ["", "relative/movies", "/media/../movies"]);
        LibraryLocation mixed = new("mixed", "Фільми", ["movies", "/media/movies"]);
        const string servicePath = "/media/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, [unparseable, mixed]);

        //Assert
        Assert.Same(mixed, location);
    }

    [Fact]
    public void Find_without_libraries_is_null()
    {
        //Arrange
        const string servicePath = "/media/movies/Aliens (1986).mkv";

        //Act
        var location = LibraryLocationMatcher.Find(servicePath, []);

        //Assert
        Assert.Null(location);
    }
}
