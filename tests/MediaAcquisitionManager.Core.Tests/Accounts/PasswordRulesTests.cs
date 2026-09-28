using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Core.Tests.Accounts;

public sealed class PasswordRulesTests
{
    // Spelled out rather than read from PasswordRules: they are what the setup and user forms show.
    private const string TooShortError = "A password must be at least 8 characters long.";
    private const string TooLongError = "A password can be at most 128 characters long.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void Validate_of_fewer_than_8_characters_is_the_too_short_sentence(string? password)
    {
        //Arrange

        //Act
        var error = PasswordRules.Validate(password);

        //Assert
        Assert.Equal(TooShortError, error);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(128)]
    public void Validate_of_8_to_128_characters_is_null(int length)
    {
        //Arrange
        var password = new string('p', length);

        //Act
        var error = PasswordRules.Validate(password);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(129)]
    [InlineData(10_000)]
    public void Validate_of_more_than_128_characters_is_the_too_long_sentence(int length)
    {
        //Arrange
        var password = new string('p', length);

        //Act
        var error = PasswordRules.Validate(password);

        //Assert
        Assert.Equal(TooLongError, error);
    }

    // No composition rules (NIST SP 800-63B): one letter repeated, spaces only, any script — the
    // length is all that counts, and spaces are not trimmed away from it.
    [Theory]
    [InlineData("aaaaaaaa")]
    [InlineData("        ")]
    [InlineData("пароль12")]
    [InlineData("correct horse")]
    public void Validate_has_no_composition_rules(string password)
    {
        //Arrange

        //Act
        var error = PasswordRules.Validate(password);

        //Assert
        Assert.Null(error);
    }
}
