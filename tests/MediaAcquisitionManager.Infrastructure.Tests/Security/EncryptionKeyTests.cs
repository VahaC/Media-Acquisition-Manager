using System.Security.Cryptography;
using MediaAcquisitionManager.Infrastructure.Security;

namespace MediaAcquisitionManager.Infrastructure.Tests.Security;

public sealed class EncryptionKeyTests
{
    private const string BlankKeyMessage =
        "Encryption:Key has no value, so integration credentials can't be encrypted or read. Leave MAM_Encryption__Key unset to let the app generate one, or set it to base64 of 32 random bytes (openssl rand -base64 32).";

    private const string NotBase64Message =
        "Encryption:Key is not valid base64, so integration credentials can't be encrypted or read. Set MAM_Encryption__Key to base64 of exactly 32 random bytes (openssl rand -base64 32), or restore .secrets/encryption-key in the data directory from a backup.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_of_a_blank_key_throws_naming_the_variable(string? configured)
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => EncryptionKey.Parse(configured));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(BlankKeyMessage, exception.Message);
    }

    [Fact]
    public void Parse_of_a_key_that_is_not_base64_throws_naming_the_variable()
    {
        //Arrange
        const string configured = "not base64 at all!";

        //Act
        var exception = Record.Exception(() => EncryptionKey.Parse(configured));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(NotBase64Message, exception.Message);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Parse_of_a_key_of_the_wrong_length_says_how_long_it_is(int length)
    {
        //Arrange
        var configured = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));

        //Act
        var exception = Record.Exception(() => EncryptionKey.Parse(configured));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            $"Encryption:Key is {length} bytes long, but AES-256 needs exactly 32. Set MAM_Encryption__Key to base64 of exactly 32 random bytes (openssl rand -base64 32), or restore .secrets/encryption-key in the data directory from a backup.",
            exception.Message);
    }

    // The message reaches the error log, which ships off the box: a key that is merely mistyped or
    // cut short is still most of the real key.
    [Theory]
    [InlineData("sDLu5T0Xx0NDyCw3yC2kTfJ1uJpD8Q0y1hO3i7k2Zx==!")]
    [InlineData("sDLu5T0Xx0NDyCw3yC2kTQ==")]
    public void Parse_never_puts_the_key_in_its_message(string configured)
    {
        //Arrange
        var keyText = configured.TrimEnd('=', '!');

        //Act
        var exception = Record.Exception(() => EncryptionKey.Parse(configured));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain(keyText, exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Parse_of_32_bytes_returns_them()
    {
        //Arrange
        var key = RandomNumberGenerator.GetBytes(32);

        //Act
        var parsed = EncryptionKey.Parse(Convert.ToBase64String(key));

        //Assert
        Assert.Equal(key, parsed);
    }
}
