using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

public sealed class ReleasePayloadLocatorPurposeTests
{
    // Fixed vectors on purpose: the purpose is the associated data every stored locator was sealed with,
    // so a change to this text makes every stored release unreadable to the submission (MAM-13).
    [Theory]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301", "release/3f2504e04f8941d39a0c0305e82c3301/payload-locator")]
    [InlineData("00000000-0000-0000-0000-000000000001", "release/00000000000000000000000000000001/payload-locator")]
    public void For_is_stable(string releaseId, string expected)
    {
        //Arrange
        var id = Guid.Parse(releaseId);

        //Act
        var purpose = ReleasePayloadLocatorPurpose.For(id);

        //Assert
        Assert.Equal(expected, purpose);
    }
}
