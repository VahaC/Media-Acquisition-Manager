using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the pseudo pass's reading of a sentence: one wrapped message reads as its English; anything else —
// unwrapped English, two messages glued side by side, a message glued to text — is a sentence that bypassed the renderer.
public sealed class PseudoTextTests
{
    [Theory]
    [InlineData("⟦The job is gone.⟧", true)]
    [InlineData("⟦The move failed: ⟦There is no file at “/a”.⟧⟧", true)]
    [InlineData("⟦⟦There is no file at “/a”⟧, so nothing was sent.⟧", true)]
    [InlineData("The job is gone.", false)]
    [InlineData("⟦The job⟧ is gone.", false)]
    [InlineData("⟦One.⟧ ⟦Two.⟧", false)]
    [InlineData("⟦Unclosed ⟦nested.⟧", false)]
    [InlineData("", false)]
    public void IsWrapped_is_true_for_one_rendered_message_only(string text, bool expected)
    {
        //Arrange

        //Act
        var wrapped = PseudoText.IsWrapped(text);

        //Assert
        Assert.Equal(expected, wrapped);
    }

    [Fact]
    public void Unwrap_gives_the_english_of_a_wrapped_message_nested_ones_included()
    {
        //Arrange
        const string text = "⟦The move failed: ⟦There is no file at “/a”.⟧⟧";

        //Act
        var english = PseudoText.Unwrap(text, "the error body");

        //Assert
        Assert.Equal("The move failed: There is no file at “/a”.", english);
    }

    [Fact]
    public void Unwrap_refuses_a_sentence_that_bypassed_the_renderer()
    {
        //Arrange
        const string text = "The job is gone.";

        //Act
        var exception = Record.Exception(() => PseudoText.Unwrap(text, "the error body"));

        //Assert
        var refused = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("the error body \"The job is gone.\"", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("⟦a⟧", true)]
    [InlineData("a ⟧", true)]
    [InlineData("plain", false)]
    [InlineData(null, false)]
    public void HasMark_sees_either_mark_anywhere(string? text, bool expected)
    {
        //Arrange

        //Act
        var marked = PseudoText.HasMark(text);

        //Assert
        Assert.Equal(expected, marked);
    }
}
