using MediaAcquisitionManager.Infrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-7 — remote text is masked before it is cleaned and cut. The adapters' tests cover a key cut by
// the length cap and one split by a zero-width space; these are secrets the cleaning itself changes,
// so they are whole only as the service sent them.
public sealed class RemoteTextTests
{
    [Theory]
    // A run of spaces, which the cleaning collapses.
    [InlineData("pass  word 1")]
    // A tab, which the cleaning turns into a space.
    [InlineData("pass\tword 1")]
    // A format character, which the cleaning drops.
    [InlineData("pass\u200Bword 1")]
    public void Clean_masks_a_secret_the_cleaning_would_change(string secret)
    {
        //Arrange
        var value = $"Refused {secret} here";

        //Act
        var cleaned = RemoteText.Clean(value, 100, [secret]);

        //Assert
        Assert.Equal("Refused *** here", cleaned);
    }
}
