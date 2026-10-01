using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

public sealed class ReleaseSearchRulesTests
{
    [Theory]
    [InlineData("Interstellar")]
    [InlineData("  Інтерстеллар  ")]
    [InlineData("a")]
    public void ValidateText_accepts_a_title(string text)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateText(text);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null, "Enter the title to search for.")]
    [InlineData("", "Enter the title to search for.")]
    [InlineData("   ", "Enter the title to search for.")]
    [InlineData("Inter\nstellar", "The search text can't contain line breaks or other control characters.")]
    [InlineData("Inter\u0000stellar", "The search text can't contain line breaks or other control characters.")]
    public void ValidateText_refuses_a_missing_or_broken_title(string? text, string expected)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateText(text);

        //Assert
        Assert.Equal(expected, error);
    }

    [Fact]
    public void ValidateText_takes_at_most_two_hundred_characters_after_trimming()
    {
        //Arrange
        var longest = $"  {new string('a', ReleaseSearchRules.TextMaximumLength)}  ";
        var tooLong = new string('a', ReleaseSearchRules.TextMaximumLength + 1);

        //Act
        var longestError = ReleaseSearchRules.ValidateText(longest);
        var tooLongError = ReleaseSearchRules.ValidateText(tooLong);

        //Assert
        Assert.Null(longestError);
        Assert.Equal("The search text can be at most 200 characters long.", tooLongError);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("2014", 2014)]
    [InlineData(" 2014 ", 2014)]
    [InlineData("1", 1)]
    [InlineData("9999", 9999)]
    public void ValidateYear_reads_a_year_or_nothing(string? year, int? expected)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateYear(year, out var value);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("20l4", "“20l4” is not a year.")]
    [InlineData("0", "“0” is not a year.")]
    [InlineData("10000", "“10000” is not a year.")]
    [InlineData("-2014", "“-2014” is not a year.")]
    [InlineData("+2014", "“+2014” is not a year.")]
    [InlineData("2 014", "“2 014” is not a year.")]
    [InlineData("٢٠١٤", "“٢٠١٤” is not a year.")]
    [InlineData("123456789012345678901234567890123456", "“12345678901234567890123456789012…” is not a year.")]
    public void ValidateYear_refuses_anything_but_a_year(string year, string expected)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateYear(year, out var value);

        //Assert
        Assert.Equal(expected, error);
        Assert.Null(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("tt0816692")]
    [InlineData(" TT0816692 ")]
    [InlineData("tt12345")]
    public void ValidateImdbId_accepts_an_id_a_library_may_hold_or_nothing(string? imdbId)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateImdbId(imdbId);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("0816692", "“0816692” is not an IMDb id, which looks like tt0816692.")]
    [InlineData("https://www.imdb.com/title/tt0816692/", "“https://www.imdb.com/title/tt081…” is not an IMDb id, which looks like tt0816692.")]
    public void ValidateImdbId_refuses_anything_else(string imdbId, string expected)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateImdbId(imdbId);

        //Assert
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("157336")]
    [InlineData(" 157336 ")]
    public void ValidateTmdbId_accepts_a_number_or_nothing(string? tmdbId)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateTmdbId(tmdbId);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("tt0816692", "“tt0816692” is not a TMDb id, which is a number such as 157336.")]
    [InlineData("1234567890123", "“1234567890123” is not a TMDb id, which is a number such as 157336.")]
    public void ValidateTmdbId_refuses_anything_else(string tmdbId, string expected)
    {
        //Arrange

        //Act
        var error = ReleaseSearchRules.ValidateTmdbId(tmdbId);

        //Assert
        Assert.Equal(expected, error);
    }
}
