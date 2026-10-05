using MediaAcquisitionManager.Core.Files;

namespace MediaAcquisitionManager.Core.Tests.Files;

// MAM-18 — which names the scan of the media folders takes as media files (Vasyl's decision, ADR 0021): a video
// container's extension after the last dot, letter case ignored, and nothing else. The list is spelled out here
// rather than read from MediaFileNames, so a dropped or added extension fails a test.
public sealed class MediaFileNamesTests
{
    public static TheoryData<string> Extensions =>
    [
        "mkv", "mp4", "m4v", "avi", "mov", "ts", "m2ts", "mts", "mpg", "mpeg", "vob", "wmv", "asf", "flv",
        "webm", "ogv", "rm", "rmvb", "iso",
    ];

    [Fact]
    public void VideoExtensions_are_the_nineteen_decided_in_lower_case_without_a_dot()
    {
        //Arrange
        string[] decided =
        [
            "mkv", "mp4", "m4v", "avi", "mov", "ts", "m2ts", "mts", "mpg", "mpeg", "vob", "wmv", "asf", "flv",
            "webm", "ogv", "rm", "rmvb", "iso",
        ];

        //Act
        var extensions = MediaFileNames.VideoExtensions;

        //Assert
        Assert.Equal(decided, extensions);
    }

    [Theory]
    [MemberData(nameof(Extensions))]
    public void IsMediaFile_takes_every_listed_extension(string extension)
    {
        //Arrange
        var name = $"The Movie (2020).{extension}";

        //Act
        var isMedia = MediaFileNames.IsMediaFile(name);

        //Assert
        Assert.True(isMedia);
    }

    [Theory]
    [MemberData(nameof(Extensions))]
    public void IsMediaFile_ignores_letter_case(string extension)
    {
        //Arrange
        var upper = $"MOVIE.{extension.ToUpperInvariant()}";
        var mixed = $"movie.{char.ToUpperInvariant(extension[0])}{extension[1..]}";

        //Act
        var upperIsMedia = MediaFileNames.IsMediaFile(upper);
        var mixedIsMedia = MediaFileNames.IsMediaFile(mixed);

        //Assert
        Assert.True(upperIsMedia);
        Assert.True(mixedIsMedia);
    }

    [Theory]
    [InlineData("movie")]
    [InlineData("mkv")]
    [InlineData("movie.")]
    [InlineData("")]
    public void IsMediaFile_refuses_a_name_without_an_extension(string name)
    {
        //Arrange
        var fileName = name;

        //Act
        var isMedia = MediaFileNames.IsMediaFile(fileName);

        //Assert
        Assert.False(isMedia);
    }

    [Fact]
    public void IsMediaFile_takes_a_name_that_is_only_the_extension()
    {
        //Arrange
        // A file named ".mkv": hidden on Linux, and still a file with that extension.
        var name = ".mkv";

        //Act
        var isMedia = MediaFileNames.IsMediaFile(name);

        //Assert
        Assert.True(isMedia);
    }

    [Theory]
    [InlineData("movie.mkv.part")]
    [InlineData("movie.mkv.!qB")]
    [InlineData("movie.mp4.tmp")]
    public void IsMediaFile_refuses_a_download_not_yet_finished(string name)
    {
        //Arrange
        // Only what follows the last dot counts.
        var fileName = name;

        //Act
        var isMedia = MediaFileNames.IsMediaFile(fileName);

        //Assert
        Assert.False(isMedia);
    }

    [Theory]
    [InlineData("poster.jpg")]
    [InlineData("movie.nfo")]
    [InlineData("movie.en.srt")]
    [InlineData("movie.mkv.nfo")]
    [InlineData("soundtrack.flac")]
    [InlineData("readme.txt")]
    [InlineData("movie.mk")]
    [InlineData("movie.mkvv")]
    [InlineData("movie.m")]
    public void IsMediaFile_refuses_any_other_extension(string name)
    {
        //Arrange
        var fileName = name;

        //Act
        var isMedia = MediaFileNames.IsMediaFile(fileName);

        //Assert
        Assert.False(isMedia);
    }

    [Fact]
    public void IsMediaFile_reads_only_the_part_after_the_last_dot()
    {
        //Arrange
        // A title with dots in it, as scene release names have.
        var name = "The.Movie.2020.1080p.BluRay.x264.mkv";

        //Act
        var isMedia = MediaFileNames.IsMediaFile(name);

        //Assert
        Assert.True(isMedia);
    }
}
