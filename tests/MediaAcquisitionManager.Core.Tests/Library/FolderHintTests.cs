using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34 — the name of the folder a library file sits in, one more query for a work without a TMDb id: a hint only, never
// the library root's own name, and nothing from a path that doesn't parse.
public sealed class FolderHintTests
{
    private static readonly string[] Roots = ["/media/movies", "/media/cartoons"];

    [Fact]
    public void Of_a_file_in_a_folder_is_that_folders_name()
    {
        //Arrange
        const string servicePath = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)/120. Landing Stripling (1962).mkv";

        //Act
        var hint = FolderHint.Of(servicePath, Roots);

        //Assert
        Assert.Equal("Tom & Jerry e001-e161 (1940-1967)", hint);
    }

    [Fact]
    public void Of_a_file_deeper_down_is_its_own_folders_name()
    {
        //Arrange
        const string servicePath = "/media/movies/John Wick. Collection/The Continental/Season 1/S01E01.mkv";

        //Act
        var hint = FolderHint.Of(servicePath, Roots);

        //Assert
        Assert.Equal("Season 1", hint);
    }

    [Fact]
    public void Of_a_file_directly_in_a_library_root_is_nothing()
    {
        //Arrange
        const string servicePath = "/media/cartoons/Merry Madagascar (2009).mkv";

        //Act
        var hint = FolderHint.Of(servicePath, Roots);

        //Assert
        Assert.Null(hint);
    }

    [Fact]
    public void Of_compares_the_root_folder_by_folder_name()
    {
        //Arrange
        // A trailing separator on the root changes nothing; /media/cartoons2 is not /media/cartoons.
        const string inRoot = "/media/cartoons/Merry Madagascar (2009).mkv";
        const string beside = "/media/cartoons2/Merry Madagascar (2009).mkv";

        //Act
        var inRootHint = FolderHint.Of(inRoot, ["/media/cartoons/"]);
        var besideHint = FolderHint.Of(beside, ["/media/cartoons/"]);

        //Assert
        Assert.Null(inRootHint);
        Assert.Equal("cartoons2", besideHint);
    }

    [Fact]
    public void Of_a_file_with_no_known_root_is_its_folders_name()
    {
        //Arrange

        //Act
        var hint = FolderHint.Of("/srv/films/Aliens (1986)/Aliens.mkv", []);

        //Assert
        Assert.Equal("Aliens (1986)", hint);
    }

    [Fact]
    public void Of_a_windows_path_reads_its_folder()
    {
        //Arrange

        //Act
        var hint = FolderHint.Of(@"D:\Media\Cartoons\Tom & Jerry\Landing Stripling.mkv", [@"d:\media\cartoons"]);

        //Assert
        Assert.Equal("Tom & Jerry", hint);
    }

    [Theory]
    [InlineData("Landing Stripling.mkv")]
    [InlineData("")]
    [InlineData("/Landing Stripling.mkv")]
    [InlineData("/media/../etc/passwd")]
    public void Of_a_path_without_a_folder_of_its_own_is_nothing(string servicePath)
    {
        //Arrange

        //Act
        var hint = FolderHint.Of(servicePath, Roots);

        //Assert
        Assert.Null(hint);
    }
}
