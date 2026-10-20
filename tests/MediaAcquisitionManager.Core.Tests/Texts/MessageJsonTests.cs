using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Tests.Texts;

// MAM-47 (ADR 0042) — the canonical JSON a column keeps a message in: one form per message (EF compares a constant with
// a column through it), Cyrillic unescaped (a Ukrainian path must not overflow the column five times sooner), every
// value kind round-tripping, and a read that never throws on what an older build or a stranger left in the column.
public sealed class MessageJsonTests
{
    [Fact]
    public void Write_gives_the_designs_example_byte_for_byte()
    {
        //Arrange
        var message = Message.Of(
            "replacements.move.quarantine-failed-tally",
            ("tally", Message.Of("replacements.tally.in-quarantine", ("names", Message.Of("replacements.tally.names", ("count", 3))), ("count", 1))),
            ("file", "Alien (1979).mkv"),
            ("error", Message.Of(
                "files.quarantine.os-failed",
                ("reason", VendorText.Clean("Device or resource busy", 300)),
                ("path", "/media/Фільми/Alien (1979).mkv"))));

        //Act
        var json = MessageJson.Write(message);

        //Assert
        Assert.Equal(
            "{\"k\":\"replacements.move.quarantine-failed-tally\",\"v\":{\"error\":{\"k\":\"files.quarantine.os-failed\",\"v\":{\"path\":\"/media/Фільми/Alien (1979).mkv\",\"reason\":{\"vendor\":\"Device or resource busy\"}}},\"file\":\"Alien (1979).mkv\",\"tally\":{\"k\":\"replacements.tally.in-quarantine\",\"v\":{\"count\":1,\"names\":{\"k\":\"replacements.tally.names\",\"v\":{\"count\":3}}}}}}",
            json);
    }

    [Fact]
    public void Write_leaves_out_the_values_of_a_message_without_any()
    {
        //Arrange
        var message = Message.Of("library-links.write.restarted");

        //Act
        var json = MessageJson.Write(message);

        //Assert
        Assert.Equal("{\"k\":\"library-links.write.restarted\"}", json);
    }

    [Fact]
    public void Write_keeps_letters_and_typographic_quotes_and_escapes_only_what_json_must()
    {
        //Arrange
        var message = Message.Of("common.sample", ("path", "«Фільми» “a” <b> & 'c' \"d\" \\e\n"));

        //Act
        var json = MessageJson.Write(message);

        //Assert
        Assert.Equal("{\"k\":\"common.sample\",\"v\":{\"path\":\"«Фільми» “a” <b> & 'c' \\\"d\\\" \\\\e\\n\"}}", json);
    }

    [Fact]
    public void Write_writes_each_value_kind_in_its_own_shape()
    {
        //Arrange
        var message = Message.Of(
            "common.sample",
            ("a-text", "x"),
            ("b-vendor", VendorText.Stored("y")),
            ("c-number", 0.0017m),
            ("d-size", new SizeValue(8_388_608)),
            ("e-duration", new DurationValue(6_990_000)),
            ("f-list", new ListValue([new TextValue("p"), new NumberValue(2)])),
            ("g-message", Message.Of("common.inner")));

        //Act
        var json = MessageJson.Write(message);

        //Assert
        Assert.Equal(
            "{\"k\":\"common.sample\",\"v\":{\"a-text\":\"x\",\"b-vendor\":{\"vendor\":\"y\"},\"c-number\":0.0017,\"d-size\":{\"bytes\":8388608},\"e-duration\":{\"ms\":6990000},\"f-list\":{\"list\":[\"p\",2]},\"g-message\":{\"k\":\"common.inner\"}}}",
            json);
    }

    [Fact]
    public void TryRead_gives_back_an_equal_message_for_every_value_kind()
    {
        //Arrange
        var message = Message.Of(
            "common.sample",
            ("text", "«Фільми» “a” \"d\" \\e\n\u0001"),
            ("vendor", VendorText.Clean("Device or resource busy", 300)),
            ("number", -1234.5m),
            ("count", 7),
            ("size", new SizeValue(1_572_864)),
            ("duration", new DurationValue(360_000_000)),
            ("list", new ListValue([new TextValue("a"), VendorText.Stored("b"), new ListValue([new NumberValue(1)]), Message.Of("common.inner", ("x", "y"))])),
            ("nested", Message.Of("common.inner", ("reason", Message.Of("common.deeper", ("said", VendorText.Stored("no")))))));

        //Act
        var read = MessageJson.TryRead(MessageJson.Write(message));

        //Assert
        Assert.Equal(message, read);
    }

    [Fact]
    public void TryRead_takes_a_number_in_its_shortest_form()
    {
        //Arrange
        const string json = "{\"k\":\"common.sample\",\"v\":{\"count\":2.50}}";

        //Act
        var read = MessageJson.TryRead(json);

        //Assert
        Assert.Equal(Message.Of("common.sample", ("count", 2.5m)), read);
        Assert.Equal("{\"k\":\"common.sample\",\"v\":{\"count\":2.5}}", MessageJson.Write(read!));
    }

    [Fact]
    public void TryRead_reads_past_the_converters_mark_on_a_kept_row()
    {
        //Arrange
        const string json = "{\"k\":\"common.legacy-text\",\"v\":{\"text\":\"Some words of an older build.\"},\"converter\":1}";

        //Act
        var read = MessageJson.TryRead(json);

        //Assert
        Assert.Equal(Message.Legacy("Some words of an older build."), read);
    }

    [Fact]
    public void WriteKept_writes_the_kept_form_that_reads_back_as_the_words_and_its_version()
    {
        //Arrange
        const string words = "Couldn't reach “Jellyfin”: Фільми \"busy\".";

        //Act
        var json = MessageJson.WriteKept(words, 1);

        //Assert
        Assert.Equal("{\"k\":\"common.legacy-text\",\"v\":{\"text\":\"Couldn't reach “Jellyfin”: Фільми \\\"busy\\\".\"},\"converter\":1}", json);
        Assert.Equal(Message.Legacy(words), MessageJson.TryRead(json));
        Assert.Equal(1, MessageJson.KeptVersionOf(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Couldn't reach “Transmission”: Connection refused.")]
    [InlineData("{\"k\":\"common.legacy-text\",\"v\":{\"text\":\"Old words.\"}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"count\":2}}")]
    [InlineData("{\"k\":\"common.legacy-text\",\"v\":{\"text\":\"Old words.\"},\"converter\":")]
    public void KeptVersionOf_anything_but_a_kept_row_is_null(string? text)
    {
        //Arrange

        //Act
        var version = MessageJson.KeptVersionOf(text);

        //Assert
        Assert.Null(version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Couldn't reach “Transmission”: Connection refused.")]
    [InlineData("{\"k\":")]
    [InlineData("{\"k\":1}")]
    [InlineData("{\"v\":{\"x\":\"y\"}}")]
    [InlineData("{\"k\":\"Not A Key\"}")]
    [InlineData("{\"k\":\"common.sample\",\"other\":1}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":true}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":null}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":{\"unknown\":1}}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":{\"vendor\":\"a\",\"bytes\":1}}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":{\"bytes\":1.5}}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":{\"k\":\"common.inner\",\"converter\":1}}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":\"a\",\"x\":\"b\"}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"bad name\":\"a\"}}")]
    [InlineData("{\"k\":\"common.sample\",\"v\":{\"x\":1e400}}")]
    [InlineData("[\"common.sample\"]")]
    public void TryRead_of_text_that_holds_no_message_is_null(string? text)
    {
        //Arrange

        //Act
        var read = MessageJson.TryRead(text);

        //Assert
        Assert.Null(read);
    }

    [Fact]
    public void TryRead_of_json_nested_deeper_than_the_reader_allows_is_null()
    {
        //Arrange
        var json = "{\"k\":\"common.sample\",\"v\":{\"x\":" + string.Concat(Enumerable.Repeat("{\"list\":[", 80)) +
                   string.Concat(Enumerable.Repeat("]}", 80)) + "}}";

        //Act
        var read = MessageJson.TryRead(json);

        //Assert
        Assert.Null(read);
    }
}
