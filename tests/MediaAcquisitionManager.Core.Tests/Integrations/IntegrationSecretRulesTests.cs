using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Integrations;

public sealed class IntegrationSecretRulesTests
{
    // Spelled out rather than read from IntegrationSecretRules: they are what the dialog shows.
    private const string ApiKeyBlankError = "A new API key can't be blank.";
    private const string ApiKeyTooShortError = "An API key must be at least 8 characters long.";
    private const string ApiKeyTooLongError = "An API key can be at most 256 characters long.";
    private const string ApiKeyCharactersError = "An API key can contain only visible characters, without spaces, quotes or backslashes.";
    private const string PasswordBlankError = "A new password can't be blank.";
    private const string PasswordTooLongError = "A password can be at most 256 characters long.";
    private const string PasswordControlCharacterError = "A password can't contain line breaks or other control characters.";

    [Theory]
    [InlineData(null, ApiKeyBlankError)]
    [InlineData("", ApiKeyBlankError)]
    [InlineData("   ", ApiKeyBlankError)]
    [InlineData("1234567", ApiKeyTooShortError)]
    [InlineData("  1234567\n", ApiKeyTooShortError)]
    [InlineData("abcd efgh", ApiKeyCharactersError)]
    [InlineData("abcd\"efgh", ApiKeyCharactersError)]
    [InlineData("abcd\\efgh", ApiKeyCharactersError)]
    [InlineData("abcd\tefgh", ApiKeyCharactersError)]
    [InlineData("abcdéfgh", ApiKeyCharactersError)]
    [InlineData("ключ-12345678", ApiKeyCharactersError)]
    public void ValidateNewApiKey_is_the_rule(string? apiKey, string expected)
    {
        //Arrange

        //Act
        var error = IntegrationSecretRules.ValidateNewApiKey(apiKey);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData(257, ApiKeyTooLongError)]
    [InlineData(256, null)]
    [InlineData(8, null)]
    public void ValidateNewApiKey_of_each_length_is_the_rule(int length, string? expected)
    {
        //Arrange
        var apiKey = new string('k', length);

        //Act
        var error = IntegrationSecretRules.ValidateNewApiKey(apiKey);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("  0123456789abcdef0123456789abcdef\r\n")]
    [InlineData("!#$%&'()*+,-./:;<=>?@[]^_`{|}~")]
    public void ValidateNewApiKey_accepts_a_32_character_hex_key(string apiKey)
    {
        //Arrange

        //Act
        var error = IntegrationSecretRules.ValidateNewApiKey(apiKey);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null, PasswordBlankError)]
    [InlineData("", PasswordBlankError)]
    [InlineData("pass\nword", PasswordControlCharacterError)]
    [InlineData("password\r", PasswordControlCharacterError)]
    [InlineData("pass\u0000word", PasswordControlCharacterError)]
    public void ValidateNewPassword_is_the_rule(string? password, string expected)
    {
        //Arrange

        //Act
        var error = IntegrationSecretRules.ValidateNewPassword(password);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData(257, PasswordTooLongError)]
    [InlineData(256, null)]
    [InlineData(1, null)]
    public void ValidateNewPassword_of_each_length_is_the_rule(int length, string? expected)
    {
        //Arrange
        var password = new string('p', length);

        //Act
        var error = IntegrationSecretRules.ValidateNewPassword(password);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Fact]
    public void ValidateNewPassword_never_trims()
    {
        //Arrange
        var spacesOnly = "   ";
        var paddedToTheLimit = " " + new string('p', 254) + " ";
        var paddedPastTheLimit = " " + new string('p', 255) + " ";

        //Act
        var spacesOnlyError = IntegrationSecretRules.ValidateNewPassword(spacesOnly);
        var paddedToTheLimitError = IntegrationSecretRules.ValidateNewPassword(paddedToTheLimit);
        var paddedPastTheLimitError = IntegrationSecretRules.ValidateNewPassword(paddedPastTheLimit);

        //Assert
        Assert.Null(spacesOnlyError);
        Assert.Null(paddedToTheLimitError);
        Assert.Equal(PasswordTooLongError, TestTexts.En(paddedPastTheLimitError));
    }
}
