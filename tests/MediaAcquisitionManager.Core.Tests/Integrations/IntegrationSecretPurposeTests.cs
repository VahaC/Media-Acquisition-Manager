using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Integrations;

public sealed class IntegrationSecretPurposeTests
{
    // Fixed vectors on purpose: the purpose is the associated data every stored secret was sealed
    // with, so a change to this text makes every stored credential unreadable (ADR 0007).
    [Theory]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301", IntegrationSecretField.Password, "integration-connection/3f2504e04f8941d39a0c0305e82c3301/password")]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301", IntegrationSecretField.ApiKey, "integration-connection/3f2504e04f8941d39a0c0305e82c3301/api-key")]
    [InlineData("00000000-0000-0000-0000-000000000001", IntegrationSecretField.ApiKey, "integration-connection/00000000000000000000000000000001/api-key")]
    public void For_is_stable(string connectionId, IntegrationSecretField field, string expected)
    {
        //Arrange
        var id = Guid.Parse(connectionId);

        //Act
        var purpose = IntegrationSecretPurpose.For(id, field);

        //Assert
        Assert.Equal(expected, purpose);
    }

    [Fact]
    public void For_of_an_undefined_field_throws()
    {
        //Arrange
        var field = (IntegrationSecretField)99;

        //Act
        var exception = Record.Exception(() => IntegrationSecretPurpose.For(Guid.NewGuid(), field));

        //Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }
}
