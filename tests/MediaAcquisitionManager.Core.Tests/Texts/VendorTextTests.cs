using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Tests.Texts;

// MAM-7 — remote text is masked before it is cleaned and cut. The adapters' tests cover a key cut by the length cap and
// one split by a zero-width space; the first test's secrets are ones the cleaning itself changes, so they are whole only
// as the service sent them. MAM-47 — RemoteText.Clean's rules and these cases moved here with them.
public sealed class VendorTextTests
{
    [Theory]
    // A run of spaces, which the cleaning collapses.
    [InlineData("pass  word 1")]
    // A tab, which the cleaning turns into a space.
    [InlineData("pass\tword 1")]
    // A format character, which the cleaning drops.
    [InlineData("pass​word 1")]
    public void Clean_masks_a_secret_the_cleaning_would_change(string secret)
    {
        //Arrange
        var value = $"Refused {secret} here";

        //Act
        var cleaned = VendorText.Clean(value, 100, secret);

        //Assert
        Assert.Equal("Refused *** here", cleaned.Text);
    }

    [Fact]
    public void Clean_drops_control_and_format_characters_and_folds_whitespace()
    {
        //Arrange
        const string value = "  Device‮ or\r\n\t resource​ busy  ";

        //Act
        var cleaned = VendorText.Clean(value, 100);

        //Assert
        Assert.Equal("Device or resource busy", cleaned.Text);
    }

    [Fact]
    public void Clean_cuts_a_long_text_with_an_ellipsis()
    {
        //Arrange
        const string value = "abcdefghij";

        //Act
        var cleaned = VendorText.Clean(value, 5);

        //Assert
        Assert.Equal("abcd…", cleaned.Text);
    }

    [Fact]
    public void Clean_never_ends_on_half_of_a_surrogate_pair()
    {
        //Arrange
        const string value = "abc😀def";

        //Act
        var cleaned = VendorText.Clean(value, 5);

        //Assert
        Assert.Equal("abc…", cleaned.Text);
    }

    [Fact]
    public void Clean_of_null_is_empty()
    {
        //Arrange

        //Act
        var cleaned = VendorText.Clean(null, 100);

        //Assert
        Assert.Equal("", cleaned.Text);
    }

    [Fact]
    public void Redacted_masks_the_words_again()
    {
        //Arrange
        var said = VendorText.Clean("Bad key s3cret-api-key-0123 for user", 100);

        //Act
        var redacted = said.Redacted("s3cret-api-key-0123");

        //Assert
        Assert.Equal("Bad key *** for user", redacted.Text);
    }

    [Fact]
    public void Stored_keeps_the_words_as_they_were_stored()
    {
        //Arrange
        const string stored = "  two  spaces\tand a tab, as stored  ";

        //Act
        var said = VendorText.Stored(stored);

        //Assert
        Assert.Equal(stored, said.Text);
        Assert.Equal(VendorText.Stored(stored), said);
    }
}
