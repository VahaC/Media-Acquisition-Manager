using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Core.Tests.Media;

// MAM-10 — one rule for the ids a library reports and the ids a search is asked for, so a search with a
// library work's own id is never refused.
public sealed class MediaIdentifierRulesTests
{
    [Theory]
    [InlineData("tt0816692", true)]
    [InlineData("TT0816692", true)]
    // Five digits: the shortest a library may hold.
    [InlineData("tt12345", true)]
    [InlineData("tt1234", false)]
    [InlineData("0816692", false)]
    [InlineData("tt0816692\n", false)]
    [InlineData(" tt0816692", false)]
    // Arabic-Indic digits are digits to \d, not to an IMDb id.
    [InlineData("tt٠٨١٦٦٩٢", false)]
    [InlineData("", false)]
    public void IsImdbId_takes_tt_and_five_to_sixty_two_digits(string value, bool expected)
    {
        //Arrange

        //Act
        var isImdbId = MediaIdentifierRules.IsImdbId(value);

        //Assert
        Assert.Equal(expected, isImdbId);
    }

    [Fact]
    public void IsImdbId_takes_at_most_sixty_two_digits()
    {
        //Arrange
        var longest = "tt" + new string('1', 62);
        var tooLong = "tt" + new string('1', 63);

        //Act
        var longestIsImdbId = MediaIdentifierRules.IsImdbId(longest);
        var tooLongIsImdbId = MediaIdentifierRules.IsImdbId(tooLong);

        //Assert
        Assert.True(longestIsImdbId);
        Assert.False(tooLongIsImdbId);
    }

    [Theory]
    [InlineData("157336", true)]
    [InlineData("1", true)]
    [InlineData("123456789012", true)]
    [InlineData("1234567890123", false)]
    [InlineData("-157336", false)]
    [InlineData("157336\n", false)]
    [InlineData("tt157336", false)]
    [InlineData("", false)]
    public void IsTmdbId_takes_one_to_twelve_digits(string value, bool expected)
    {
        //Arrange

        //Act
        var isTmdbId = MediaIdentifierRules.IsTmdbId(value);

        //Assert
        Assert.Equal(expected, isTmdbId);
    }
}
