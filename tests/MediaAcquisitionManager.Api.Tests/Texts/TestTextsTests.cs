using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the one-word change that lets the Core, Infrastructure and stored-row tests keep their English
// asserts: TestTexts.En renders a message strictly over the embedded locale files, so a test that reaches a broken
// sentence fails there.
public sealed class TestTextsTests
{
    [Fact]
    public void En_writes_a_message_as_the_server_does_in_english()
    {
        //Arrange
        var message = Message.Of("common.request.unknown-endpoint", ("method", "GET"), ("path", "/api/nope"));

        //Act
        var english = TestTexts.En(message);

        //Assert
        Assert.Equal("No API endpoint matches GET /api/nope.", english);
    }

    [Fact]
    public void En_of_null_is_null()
    {
        //Arrange
        Message? message = null;

        //Act
        var english = TestTexts.En(message);

        //Assert
        Assert.Null(english);
    }

    [Fact]
    public void En_throws_for_a_key_english_has_not()
    {
        //Arrange
        var message = Message.Of("common.no-such-sentence");

        //Act
        var exception = Record.Exception(() => TestTexts.En(message));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }
}
