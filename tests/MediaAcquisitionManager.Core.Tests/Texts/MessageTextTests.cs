using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Tests.Texts;

// MAM-47 (ADR 0042) — a quoted name or path, cleaned as ReplacementText.Quote did, without the quotes the template now
// owns: a sentence that switches to it keeps today's English byte for byte.
public sealed class MessageTextTests
{
    [Fact]
    public void Clean_turns_each_line_break_and_tab_into_a_space_and_keeps_runs_of_spaces()
    {
        //Arrange
        const string name = "Alien\n\t(1979)  Director's Cut.mkv";

        //Act
        var cleaned = MessageText.Clean(name);

        //Assert
        Assert.Equal("Alien  (1979)  Director's Cut.mkv", cleaned);
    }

    [Fact]
    public void Clean_drops_control_and_format_characters()
    {
        //Arrange
        const string name = "a‮b​c\u0007d";

        //Act
        var cleaned = MessageText.Clean(name);

        //Assert
        Assert.Equal("abcd", cleaned);
    }

    [Fact]
    public void Clean_cuts_a_long_path_to_240_characters_with_an_ellipsis()
    {
        //Arrange
        var path = new string('a', 300);

        //Act
        var cleaned = MessageText.Clean(path);

        //Assert
        Assert.Equal(new string('a', 239) + "…", cleaned);
    }

    [Fact]
    public void Clean_never_ends_on_half_of_a_surrogate_pair()
    {
        //Arrange
        var name = new string('a', 238) + "😀😀";

        //Act
        var cleaned = MessageText.Clean(name);

        //Assert
        Assert.Equal(new string('a', 238) + "…", cleaned);
    }

    [Fact]
    public void Clean_of_null_is_empty()
    {
        //Arrange

        //Act
        var cleaned = MessageText.Clean(null);

        //Assert
        Assert.Equal("", cleaned);
    }

    // MAM-47 — ReplacementText.Quote went with its last caller (WP9): what it wrote is spelled here, so the quotes the
    // templates hold around a cleaned name still read as it did.
    [Theory]
    [InlineData("/media/Фільми/Alien (1979).mkv", "“/media/Фільми/Alien (1979).mkv”")]
    [InlineData("  leading and trailing  ", "“  leading and trailing  ”")]
    [InlineData("tab\there\r\nand‮there", "“tab here  andthere”")]
    [InlineData("", "“”")]
    [InlineData(null, "“”")]
    public void Clean_inside_quotes_is_what_ReplacementText_Quote_wrote(string? text, string expected)
    {
        //Arrange

        //Act
        var quoted = $"“{MessageText.Clean(text)}”";

        //Assert
        Assert.Equal(expected, quoted);
    }

    [Fact]
    public void Clean_of_a_long_name_inside_quotes_is_what_ReplacementText_Quote_wrote()
    {
        //Arrange
        var text = string.Concat(Enumerable.Repeat("Довга назва 😀 ", 40));

        //Act
        var quoted = $"“{MessageText.Clean(text)}”";

        //Assert
        // 239 characters and an ellipsis: fifteen whole repeats and the sixteenth up to its emoji, never half of one.
        Assert.Equal($"“{string.Concat(Enumerable.Repeat("Довга назва 😀 ", 15))}Довга назва 😀…”", quoted);
    }
}
