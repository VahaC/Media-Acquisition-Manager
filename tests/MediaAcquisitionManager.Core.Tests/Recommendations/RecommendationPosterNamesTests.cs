using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33 — the file names a recommended work's poster is served under: a catalog poster path without its slash, letters,
// digits, _ and - then a JPEG or PNG extension, and nothing that could name a path outside the posters' folder. The list
// hands out a URL only for such a name, so a card never asks for one the endpoint refuses.
public sealed class RecommendationPosterNamesTests
{
    [Theory]
    [InlineData("r2J02Z2OpNTctfOSN1Ydgii51I3.jpg")]
    [InlineData("a.jpeg")]
    [InlineData("kZ9-DZp4_oYxQ.png")]
    public void IsFileName_takes_a_catalog_poster_file_name(string fileName)
    {
        //Arrange

        //Act
        var taken = RecommendationPosterNames.IsFileName(fileName);

        //Assert
        Assert.True(taken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".jpg")]
    [InlineData("poster")]
    [InlineData("poster.gif")]
    [InlineData("poster.JPG")]
    [InlineData("a.b.jpg")]
    [InlineData("../mam.jpg")]
    [InlineData("..\\mam.jpg")]
    [InlineData("C:mam.jpg")]
    [InlineData("/abc.jpg")]
    [InlineData("abc.jpg\n")]
    [InlineData("abc .jpg")]
    [InlineData("аbc.jpg")]
    public void IsFileName_refuses_anything_else(string? fileName)
    {
        //Arrange

        //Act
        var taken = RecommendationPosterNames.IsFileName(fileName);

        //Assert
        // A trailing line break too (\z, not $), and a Cyrillic "а" that only looks Latin.
        Assert.False(taken);
    }

    [Fact]
    public void IsFileName_refuses_a_name_longer_than_a_hundred_characters_before_its_extension()
    {
        //Arrange
        var longest = new string('a', 100) + ".jpg";
        var longer = new string('a', 101) + ".jpg";

        //Act
        var takes = (RecommendationPosterNames.IsFileName(longest), RecommendationPosterNames.IsFileName(longer));

        //Assert
        Assert.Equal((true, false), takes);
    }

    [Theory]
    [InlineData("/r2J02Z2OpNTctfOSN1Ydgii51I3.jpg", "r2J02Z2OpNTctfOSN1Ydgii51I3.jpg")]
    [InlineData("/poster.png", "poster.png")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("poster.jpg", null)]
    [InlineData("//poster.jpg", null)]
    [InlineData("/../poster.jpg", null)]
    [InlineData("/poster.gif", null)]
    public void FileNameOf_is_the_poster_paths_name_only_when_it_is_served(string? posterPath, string? expected)
    {
        //Arrange

        //Act
        var fileName = RecommendationPosterNames.FileNameOf(posterPath);

        //Assert
        Assert.Equal(expected, fileName);
    }

    [Theory]
    [InlineData("poster.jpg", "image/jpeg")]
    [InlineData("poster.jpeg", "image/jpeg")]
    [InlineData("poster.png", "image/png")]
    public void MediaTypeOf_is_the_extensions(string fileName, string expected)
    {
        //Arrange

        //Act
        var mediaType = RecommendationPosterNames.MediaTypeOf(fileName);

        //Assert
        Assert.Equal(expected, mediaType);
    }
}
