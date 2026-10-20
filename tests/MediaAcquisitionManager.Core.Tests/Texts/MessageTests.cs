using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Tests.Texts;

// MAM-47 (ADR 0042) — the value model every server sentence is built from: its guards (a malformed key or name is a
// programmer error, caught where the message is made), its structural equality (the poller's "unchanged?", the
// replacement twins and EF's change tracking compare messages built apart) and its deep redaction.
public sealed class MessageTests
{
    private const string Secret = "s3cret-api-key-0123";

    [Fact]
    public void Of_keeps_the_values_in_ordinal_order_of_their_names()
    {
        //Arrange
        var values = new (string, MessageValue)[] { ("path", "/a"), ("Count", 2), ("client", "Transmission") };

        //Act
        var message = Message.Of("downloads.sample", values);

        //Assert
        Assert.Equal(["Count", "client", "path"], message.Values.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("common")]
    [InlineData("Common.quoted")]
    [InlineData("common..quoted")]
    [InlineData(".quoted")]
    [InlineData("common.")]
    [InlineData("common.size.mib_one")]
    [InlineData("common.quoted ")]
    [InlineData("common.request.unknown endpoint")]
    public void Of_refuses_a_malformed_key(string key)
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => Message.Of(key));

        //Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1st")]
    [InlineData("two words")]
    [InlineData("name,number")]
    [InlineData("{{path}}")]
    public void Of_refuses_a_malformed_value_name(string name)
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => Message.Of("common.sample", (name, "value")));

        //Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void Of_refuses_a_name_given_twice()
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => Message.Of("common.sample", ("path", "/a"), ("path", "/b")));

        //Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void Of_refuses_a_null_value()
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => Message.Of("common.sample", ("path", (MessageValue)null!)));

        //Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void A_null_string_is_no_text_value()
    {
        //Arrange
        string? missing = null;

        //Act
        var exception = Record.Exception(() => Message.Of("common.sample", ("path", missing!)));

        //Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void Implicit_values_are_text_for_a_string_and_a_number_for_a_number()
    {
        //Arrange

        //Act
        var message = Message.Of("common.sample", ("name", "Alien"), ("count", 3), ("bytes", 4_194_304L), ("seconds", 2.5m));

        //Assert
        Assert.Equal(new TextValue("Alien"), message.Values["name"]);
        Assert.Equal(new NumberValue(3), message.Values["count"]);
        Assert.Equal(new NumberValue(4_194_304), message.Values["bytes"]);
        Assert.Equal(new NumberValue(2.5m), message.Values["seconds"]);
    }

    [Fact]
    public void Equals_compares_two_messages_built_apart_by_key_and_values()
    {
        //Arrange
        var first = Build("/media/Фільми/Alien (1979).mkv");
        var second = Build("/media/Фільми/Alien (1979).mkv");

        //Act
        var equal = first.Equals(second);

        //Assert
        Assert.True(equal);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_tells_apart_a_different_value_deep_inside()
    {
        //Arrange
        var first = Build("/media/a.mkv");
        var second = Build("/media/b.mkv");

        //Act
        var equal = first.Equals(second);

        //Assert
        Assert.False(equal);
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_tells_apart_another_key_and_a_missing_value()
    {
        //Arrange
        var message = Message.Of("common.sample", ("path", "/a"), ("count", 1));

        //Act
        var otherKey = message.Equals(Message.Of("common.other", ("path", "/a"), ("count", 1)));
        var fewerValues = message.Equals(Message.Of("common.sample", ("path", "/a")));

        //Assert
        Assert.False(otherKey);
        Assert.False(fewerValues);
    }

    [Fact]
    public void Equals_tells_a_text_from_the_same_words_of_a_vendor()
    {
        //Arrange
        var text = Message.Of("common.sample", ("said", "Busy"));
        var vendor = Message.Of("common.sample", ("said", VendorText.Clean("Busy", 100)));

        //Act
        var equal = text.Equals(vendor);

        //Assert
        Assert.False(equal);
    }

    [Fact]
    public void Legacy_keeps_the_words_under_the_legacy_key()
    {
        //Arrange
        const string words = "Couldn't reach “Transmission”.";

        //Act
        var message = Message.Legacy(words);

        //Assert
        Assert.Equal(Message.LegacyKey, message.Key);
        Assert.Equal("common.legacy-text", message.Key);
        Assert.Equal(new TextValue(words), Assert.Single(message.Values).Value);
        Assert.Equal("text", Assert.Single(message.Values).Key);
    }

    [Fact]
    public void With_adds_or_replaces_a_value_and_leaves_the_original_as_it_was()
    {
        //Arrange
        var original = Message.Of("common.sample", ("path", "/a"));

        //Act
        var added = original.With("count", 2);
        var replaced = original.With("path", "/b");

        //Assert
        Assert.Equal(Message.Of("common.sample", ("path", "/a"), ("count", 2)), added);
        Assert.Equal(Message.Of("common.sample", ("path", "/b")), replaced);
        Assert.Equal(Message.Of("common.sample", ("path", "/a")), original);
    }

    [Fact]
    public void Redacted_masks_every_text_and_vendor_value_nested_messages_and_lists_included()
    {
        //Arrange
        var message = Message.Of(
            "common.sample",
            ("url", $"http://host/?apikey={Secret}"),
            ("said", VendorText.Stored($"Bad key {Secret}")),
            ("names", new ListValue([new TextValue(Secret), VendorText.Stored($"x{Secret}")])),
            ("reason", Message.Of("common.inner", ("detail", $"{Secret}!"))),
            ("count", 7));

        //Act
        var redacted = message.Redacted(Secret);

        //Assert
        var expected = Message.Of(
            "common.sample",
            ("url", "http://host/?apikey=***"),
            ("said", VendorText.Stored("Bad key ***")),
            ("names", new ListValue([new TextValue("***"), VendorText.Stored("x***")])),
            ("reason", Message.Of("common.inner", ("detail", "***!"))),
            ("count", 7));
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain(Secret, redacted.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_is_the_canonical_json_never_a_sentence()
    {
        //Arrange
        var message = Message.Of("common.request.unknown-endpoint", ("path", "/api/nope"), ("method", "GET"));

        //Act
        var text = message.ToString();

        //Assert
        Assert.Equal("{\"k\":\"common.request.unknown-endpoint\",\"v\":{\"method\":\"GET\",\"path\":\"/api/nope\"}}", text);
    }

    [Fact]
    public void A_text_value_refuses_null()
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => new TextValue(null!));

        //Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Theory]
    [InlineData("1.0", "1", 0)]
    [InlineData("2.50", "2.5", 1)]
    [InlineData("0.00170", "0.0017", 4)]
    [InlineData("100", "100", 0)]
    [InlineData("0.0", "0", 0)]
    [InlineData("-3.200", "-3.2", 1)]
    public void A_number_drops_its_trailing_fractional_zeros(string written, string shown, int scale)
    {
        //Arrange
        var number = decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture);

        //Act
        var value = new NumberValue(number);

        //Assert
        Assert.Equal(shown, value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(scale, value.Number.Scale);
    }

    [Fact]
    public void A_list_copies_its_items_and_compares_them_in_order()
    {
        //Arrange
        var items = new List<MessageValue> { new TextValue("a"), new TextValue("b") };
        var list = new ListValue(items);

        //Act
        items.Add(new TextValue("c"));

        //Assert
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(new ListValue([new TextValue("a"), new TextValue("b")]), list);
        Assert.Equal(new ListValue([new TextValue("a"), new TextValue("b")]).GetHashCode(), list.GetHashCode());
        Assert.NotEqual(new ListValue([new TextValue("b"), new TextValue("a")]), list);
    }

    [Fact]
    public void A_list_refuses_a_null_item()
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => new ListValue([new TextValue("a"), null!]));

        //Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void A_duration_from_a_time_keeps_whole_milliseconds()
    {
        //Arrange
        var time = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * 6_990_000 + 9_999);

        //Act
        var duration = DurationValue.From(time);

        //Assert
        Assert.Equal(6_990_000, duration.Milliseconds);
    }

    private static Message Build(string path) => Message.Of(
        "replacements.move.quarantine-failed-tally",
        ("file", "Alien (1979).mkv"),
        ("error", Message.Of("files.quarantine.os-failed", ("path", path), ("reason", VendorText.Clean("Device or resource busy", 300)))),
        ("names", new ListValue([new TextValue("a"), new NumberValue(2), new SizeValue(8), new DurationValue(9)])));
}
