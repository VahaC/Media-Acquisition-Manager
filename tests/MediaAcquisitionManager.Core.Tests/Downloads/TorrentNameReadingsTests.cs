using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-30 — a torrent's name, or one element of a file's path, as the folder names Transmission writes, read from its
// source: 4.0 cleans every name for any operating system (Portable), 4.1 on Windows cleans it the same way and replaces
// control characters too (WindowsSafe), 4.1 on Linux keeps it but for . and .. (Verbatim). All split on / first, cut at
// NUL, and never answer an empty name, . or .. — a target built from one would name another place than the client
// writes, and the overwrite check would miss the file there.
public sealed class TorrentNameReadingsTests
{
    [Theory]
    // A / is a folder boundary in both versions, never a character to replace.
    [InlineData("a/b", "a|b")]
    [InlineData("x/y.mkv", "x|y.mkv")]
    [InlineData("Film (2020)/Film (2020).mkv", "Film (2020)|Film (2020).mkv")]
    // Leading and trailing whitespace goes (C's isspace), then trailing dots — in that order, so a space before the
    // dots stays.
    [InlineData("  name  ", "name")]
    [InlineData("\tname\r\n", "name")]
    [InlineData("name.", "name")]
    [InlineData("name...", "name")]
    [InlineData("name. ", "name")]
    [InlineData("name .", "name ")]
    // A non-breaking space isn't C's whitespace.
    [InlineData(" name", " name")]
    // A Windows device name, alone or before a dot, in any letter case, gets an underscore before it.
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("Lpt9.log", "_Lpt9.log")]
    [InlineData("COM1", "_COM1")]
    [InlineData("CON.", "_CON")]
    [InlineData("COM10", "COM10")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("CON .txt", "CON .txt")]
    // The characters a Windows name can't hold.
    [InlineData("A: B?", "A_ B_")]
    [InlineData("\"*<>|", "_____")]
    [InlineData(@"a\b", "a_b")]
    [InlineData("Фільм: частина 1", "Фільм_ частина 1")]
    public void Portable_cleans_each_piece_as_transmission_4_0_does(string text, string expected)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.Portable(text);

        //Assert
        Assert.Equal(expected.Split('|'), names);
    }

    [Theory]
    // A piece that cleans to nothing is no folder: a//b is a/b on disk.
    [InlineData("...", "")]
    [InlineData(".", "")]
    [InlineData("..", "")]
    [InlineData("   ", "")]
    [InlineData(" . ", "")]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("a/../b", "a|b")]
    [InlineData("/a//b/", "a|b")]
    [InlineData("a/ .. /b", "a|b")]
    public void Portable_drops_every_piece_that_cleans_to_nothing(string text, string expected)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.Portable(text);

        //Assert
        Assert.Equal(expected.Length == 0 ? [] : expected.Split('|'), names);
    }

    [Theory]
    [InlineData("a\u0001b", "a\u0001b")]
    [InlineData("Film\u001F(2020)", "Film\u001F(2020)")]
    [InlineData("a\tb", "a\tb")]
    public void Portable_keeps_a_control_character_inside_a_piece_as_transmission_4_0_does(string text, string expected)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.Portable(text);

        //Assert
        Assert.Equal([expected], names);
    }

    [Theory]
    // A control character inside a piece becomes an underscore: a Windows name can't hold one.
    [InlineData("a\u0001b", "a_b")]
    [InlineData("Film\u001F(2020)", "Film_(2020)")]
    [InlineData("a\tb", "a_b")]
    [InlineData("CON\u0001", "CON_")]
    [InlineData("x\u0002/y\u0003.mkv", "x_|y_.mkv")]
    // DEL is above U+001F, so it stays.
    [InlineData("a\u007Fb", "a\u007Fb")]
    // The whitespace at either end goes first, control characters among it.
    [InlineData("\tname\r\n", "name")]
    // The rest as Portable.
    [InlineData("a/b", "a|b")]
    [InlineData("  name  ", "name")]
    [InlineData("name.", "name")]
    [InlineData("name .", "name ")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("A: B?", "A_ B_")]
    [InlineData(@"a\b", "a_b")]
    public void WindowsSafe_cleans_each_piece_as_transmission_4_1_on_windows_does(string text, string expected)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.WindowsSafe(text);

        //Assert
        Assert.Equal(expected.Split('|'), names);
    }

    [Theory]
    [InlineData("...", "")]
    [InlineData(".", "")]
    [InlineData("..", "")]
    [InlineData(" \t ", "")]
    [InlineData("", "")]
    [InlineData("a/../b", "a|b")]
    [InlineData("/a//b/", "a|b")]
    public void WindowsSafe_drops_every_piece_that_cleans_to_nothing(string text, string expected)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.WindowsSafe(text);

        //Assert
        Assert.Equal(expected.Length == 0 ? [] : expected.Split('|'), names);
    }

    [Theory]
    [InlineData("a/b", "a|b")]
    [InlineData("x/y.mkv", "x|y.mkv")]
    // Kept as they are: spaces, dots, colons, device names, backslashes.
    [InlineData("  name  ", "  name  ")]
    [InlineData("name.", "name.")]
    [InlineData("...", "...")]
    [InlineData("CON", "CON")]
    [InlineData("con.txt", "con.txt")]
    [InlineData("A: B?", "A: B?")]
    [InlineData(@"a\b", @"a\b")]
    // . and .. become names of their own, never the folder itself or its parent.
    [InlineData(".", "_.")]
    [InlineData("..", "_..")]
    [InlineData("a/../b", "a|_..|b")]
    [InlineData("/a//b/", "a|b")]
    public void Verbatim_keeps_each_piece_as_transmission_4_1_on_linux_does(string text, string expected)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.Verbatim(text);

        //Assert
        Assert.Equal(expected.Split('|'), names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    public void Verbatim_of_a_text_with_no_piece_names_no_place(string text)
    {
        //Arrange

        //Act
        var names = TorrentNameReadings.Verbatim(text);

        //Assert
        // As a file's path, a torrent Transmission 4.1 refuses; as a torrent's name, no folder of its own.
        Assert.Empty(names);
    }

    [Fact]
    public void Every_reading_cuts_the_text_at_its_first_nul()
    {
        //Arrange
        const string text = "Film\0hidden/elsewhere.mkv";

        //Act
        var portable = TorrentNameReadings.Portable(text);
        var windowsSafe = TorrentNameReadings.WindowsSafe(text);
        var verbatim = TorrentNameReadings.Verbatim(text);
        var nothing = (
            TorrentNameReadings.Portable("\0Film"), TorrentNameReadings.WindowsSafe("\0Film"), TorrentNameReadings.Verbatim("\0Film"));

        //Assert
        Assert.Equal(["Film"], portable);
        Assert.Equal(["Film"], windowsSafe);
        Assert.Equal(["Film"], verbatim);
        Assert.Equal((0, 0, 0), (nothing.Item1.Count, nothing.Item2.Count, nothing.Item3.Count));
    }

    [Fact]
    public void Every_reading_reads_a_lone_surrogate_as_the_replacement_character()
    {
        //Arrange
        // No UTF-8 decodes to one; a pair stays whole.
        const string text = "a\uD800b/\uDC00/🎬";

        //Act
        var portable = TorrentNameReadings.Portable(text);
        var windowsSafe = TorrentNameReadings.WindowsSafe(text);
        var verbatim = TorrentNameReadings.Verbatim(text);

        //Assert
        Assert.Equal(["a�b", "�", "🎬"], portable);
        Assert.Equal(["a�b", "�", "🎬"], windowsSafe);
        Assert.Equal(["a�b", "�", "🎬"], verbatim);
    }

    [Fact]
    public void No_reading_ever_answers_a_name_that_would_name_another_place()
    {
        //Arrange
        string[] texts = ["", ".", "..", "...", "/", "a/./b", "a/../b", " .. ", "x/", "/y", "a\0/b", "\\", "\\..", "./.", ". /. "];

        //Act
        var names = texts
            .SelectMany(text => TorrentNameReadings.Portable(text)
                .Concat(TorrentNameReadings.WindowsSafe(text))
                .Concat(TorrentNameReadings.Verbatim(text)))
            .ToList();

        //Assert
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => name is "" or "." or ".." || name.Contains('/', StringComparison.Ordinal));
    }
}
