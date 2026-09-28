using System.Globalization;
using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Core.Tests.Accounts;

public sealed class UserNameRulesTests
{
    // Spelled out rather than read from UserNameRules: they are what the setup and user forms show.
    private const string LengthError = "A user name must be 3 to 32 characters long.";
    private const string CharactersError = "A user name can contain only letters, digits, dots, hyphens and underscores.";

    [Theory]
    [InlineData("abc")]
    [InlineData("alice")]
    [InlineData("Alice.Smith")]
    [InlineData("bob-2")]
    [InlineData("snake_case")]
    [InlineData("a.b-c_d")]
    [InlineData("0123456789")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void Validate_of_a_valid_name_is_null(string userName)
    {
        //Arrange

        //Act
        var error = UserNameRules.Validate(userName);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Validate_of_a_name_outside_3_to_32_characters_is_the_length_sentence(string? userName)
    {
        //Arrange

        //Act
        var error = UserNameRules.Validate(userName);

        //Assert
        Assert.Equal(LengthError, error);
    }

    [Theory]
    [InlineData("al ice")]
    [InlineData("alice!")]
    [InlineData("ali@ce")]
    [InlineData("a/b/c")]
    [InlineData("a+b")]
    [InlineData("tab\tname")]
    [InlineData("élodie")]
    [InlineData("Алиса")]
    public void Validate_of_a_name_with_other_characters_is_the_characters_sentence(string userName)
    {
        //Arrange

        //Act
        var error = UserNameRules.Validate(userName);

        //Assert
        Assert.Equal(CharactersError, error);
    }

    [Theory]
    [InlineData(" alice ")]
    [InlineData("\talice\n")]
    public void Validate_ignores_surrounding_whitespace(string userName)
    {
        //Arrange

        //Act
        var error = UserNameRules.Validate(userName);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("alice", "ALICE")]
    [InlineData("Alice", "ALICE")]
    [InlineData(" Alice ", "ALICE")]
    [InlineData("a.b-c_D9", "A.B-C_D9")]
    public void Normalize_trims_and_upper_cases(string userName, string expected)
    {
        //Arrange

        //Act
        var normalized = UserNameRules.Normalize(userName);

        //Assert
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Normalize_is_the_same_under_any_culture()
    {
        //Arrange
        // Turkish upper-cases "i" to a dotted "İ": a culture-sensitive normalisation would make
        // "alice" a different account on a server whose locale happens to be Turkish.
        var original = CultureInfo.CurrentCulture;
        string normalized;

        //Act
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            normalized = UserNameRules.Normalize("alice");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        //Assert
        Assert.Equal("ALICE", normalized);
    }
}
