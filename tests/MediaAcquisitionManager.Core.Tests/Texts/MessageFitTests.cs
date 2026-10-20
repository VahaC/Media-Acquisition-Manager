using System.Text;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Tests.Texts;

// MAM-47 (ADR 0042) — a message is fitted into its column by cutting its values, never by widening the column (a table
// rebuild on SQLite, ADR 0009). Deterministic and idempotent, so a fitted message equals what the column reads back and
// the poller doesn't rewrite a long reason every two seconds.
public sealed class MessageFitTests
{
    private const int Column = MessageJson.ColumnLength;

    [Fact]
    public void Fit_returns_a_message_that_fits_as_it_is()
    {
        //Arrange
        var message = Message.Of("files.sample", ("path", "/media/Фільми/Alien (1979).mkv"));

        //Act
        var fitted = MessageFit.Fit(message, Column);

        //Assert
        Assert.Same(message, fitted);
    }

    [Fact]
    public void Fit_cuts_the_longest_value_by_the_overflow_plus_one_and_ends_it_with_an_ellipsis()
    {
        //Arrange
        var path = "/media/" + new string('a', 1_200);
        var message = Message.Of("files.sample", ("path", path), ("name", "Alien (1979).mkv"));

        //Act
        var fitted = MessageFit.Fit(message, Column);

        //Assert
        var json = MessageJson.Write(fitted);
        Assert.Equal(Column, json.Length);
        var cut = Assert.IsType<TextValue>(fitted.Values["path"]).Text;
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        Assert.StartsWith(cut[..^1], path, StringComparison.Ordinal);
        Assert.Equal(new TextValue("Alien (1979).mkv"), fitted.Values["name"]);
    }

    [Fact]
    public void Fit_keeps_a_vendors_words_a_vendors_after_the_cut()
    {
        //Arrange
        var message = Message.Of("files.sample", ("reason", VendorText.Stored(new string('e', 1_500))));

        //Act
        var fitted = MessageFit.Fit(message, Column);

        //Assert
        var cut = Assert.IsType<VendorText>(fitted.Values["reason"]);
        Assert.EndsWith("…", cut.Text, StringComparison.Ordinal);
        Assert.True(MessageJson.Write(fitted).Length <= Column);
    }

    [Fact]
    public void Fit_cuts_a_value_deep_inside_a_nested_message()
    {
        //Arrange
        var inner = Message.Of("files.inner", ("path", new string('p', 2_000)));
        var message = Message.Of("files.sample", ("error", inner), ("file", "a.mkv"));

        //Act
        var fitted = MessageFit.Fit(message, Column);

        //Assert
        var nested = Assert.IsType<Message>(fitted.Values["error"]);
        Assert.EndsWith("…", Assert.IsType<TextValue>(nested.Values["path"]).Text, StringComparison.Ordinal);
        Assert.Equal(Column, MessageJson.Write(fitted).Length);
    }

    [Fact]
    public void Fit_never_ends_a_cut_value_on_half_of_a_surrogate_pair()
    {
        //Arrange
        var message = Message.Of("files.sample", ("name", string.Concat(Enumerable.Repeat("😀", 600))));

        //Act
        var fitted = MessageFit.Fit(message, Column);

        //Assert
        var cut = Assert.IsType<TextValue>(fitted.Values["name"]).Text;
        Assert.False(char.IsHighSurrogate(cut[^2]));
        Assert.True(MessageJson.Write(fitted).Length <= Column);
    }

    [Fact]
    public void Fit_cuts_the_first_of_two_equally_long_values_to_the_floor_and_then_the_next()
    {
        //Arrange
        var message = Message.Of("a.b", ("y", new string('y', 200)), ("x", new string('x', 200)));

        //Act
        var fitted = MessageFit.Fit(message, 100);

        //Assert
        Assert.Equal(new string('x', MessageFit.MinimumCutLength - 1) + "…", Assert.IsType<TextValue>(fitted.Values["x"]).Text);
        Assert.Equal(new string('y', 44) + "…", Assert.IsType<TextValue>(fitted.Values["y"]).Text);
        Assert.Equal(100, MessageJson.Write(fitted).Length);
    }

    [Fact]
    public void Fit_drops_trailing_list_items_when_no_value_is_long_enough_to_cut()
    {
        //Arrange
        var items = Enumerable.Range(0, 100).Select(index => (MessageValue)new TextValue($"item {index:000} of a list"));
        var message = Message.Of("a.b", ("names", new ListValue(items)));

        //Act
        var fitted = MessageFit.Fit(message, 300);

        //Assert
        var list = Assert.IsType<ListValue>(fitted.Values["names"]);
        Assert.Equal(new TextValue("…"), list.Items[^1]);
        Assert.Equal(new TextValue("item 000 of a list"), list.Items[0]);
        Assert.True(MessageJson.Write(fitted).Length <= 300);
        // As many items as fit: the next one would not.
        var next = new TextValue($"item {list.Items.Count - 1:000} of a list");
        var oneMore = new ListValue([.. list.Items.SkipLast(1), next, new TextValue("…")]);
        Assert.True(MessageJson.Write(Message.Of("a.b", ("names", oneMore))).Length > 300);
    }

    [Fact]
    public void IsShortenedListEnd_is_the_item_a_shortened_list_ends_with_and_no_other()
    {
        //Arrange
        var items = Enumerable.Range(0, 100).Select(index => (MessageValue)new TextValue($"item {index:000} of a list"));
        var fitted = MessageFit.Fit(Message.Of("a.b", ("names", new ListValue(items))), 300);
        var list = Assert.IsType<ListValue>(fitted.Values["names"]);

        //Act
        var ends = list.Items.Select(MessageFit.IsShortenedListEnd).ToList();
        var others = new MessageValue[] { VendorText.Stored("…"), new TextValue("…."), new NumberValue(1) }.Select(MessageFit.IsShortenedListEnd);

        //Assert
        Assert.True(ends[^1]);
        Assert.All(ends.SkipLast(1), Assert.False);
        Assert.All(others, Assert.False);
    }

    [Fact]
    public void Fit_keeps_only_the_key_when_nothing_else_can_shrink()
    {
        //Arrange
        var values = Enumerable.Range(0, 60).Select(index => ($"v{index}", (MessageValue)new TextValue("twenty characters...."))).ToArray();
        var message = Message.Of("a.b", values);

        //Act
        var fitted = MessageFit.Fit(message, 100);

        //Assert
        Assert.Equal(Message.Of("a.b"), fitted);
    }

    [Fact]
    public void Fit_is_idempotent_and_a_fitted_message_equals_what_the_column_reads_back()
    {
        //Arrange
        var message = Message.Of("files.sample", ("path", "/media/Фільми/" + new string('я', 1_400)), ("count", 3));

        //Act
        var fitted = MessageFit.Fit(message, Column);

        //Assert
        Assert.Same(fitted, MessageFit.Fit(fitted, Column));
        Assert.Equal(fitted, MessageJson.TryRead(MessageJson.Write(fitted)));
    }

    // MAM-47 — "store the key alone" is unreachable with today's caps on values (paths and names 240, a service's or the
    // OS's words up to 300, echoed input 64): generated worst cases — up to four values per message, nested three deep,
    // lists of up to five, at the caps, in letters JSON escapes and in ones it doesn't — always fit by cutting values.
    [Fact]
    public void Fit_of_generated_worst_cases_at_todays_caps_always_fits_without_dropping_to_the_key()
    {
        //Arrange
        var random = new Random(47);
        var messages = Enumerable.Range(0, 500).Select(_ => Generate(random, depth: 0)).ToList();

        //Act
        var fitted = messages.Select(message => MessageFit.Fit(message, Column)).ToList();

        //Assert
        for (var index = 0; index < fitted.Count; index++)
        {
            var json = MessageJson.Write(fitted[index]);
            Assert.True(json.Length <= Column, $"Case {index} is {json.Length} characters.");
            Assert.NotEmpty(fitted[index].Values);
            Assert.Equal(fitted[index], MessageJson.TryRead(json));
            Assert.Same(fitted[index], MessageFit.Fit(fitted[index], Column));
        }
    }

    private static Message Generate(Random random, int depth)
    {
        var values = new List<(string, MessageValue)>();
        var count = random.Next(1, 5);
        for (var index = 0; index < count; index++)
        {
            MessageValue value = random.Next(depth < 3 ? 6 : 4) switch
            {
                0 => new TextValue(Text(random, 240)),
                1 => VendorText.Stored(Text(random, 300)),
                2 => new NumberValue(random.Next()),
                3 => new TextValue(Text(random, 64)),
                4 => new ListValue(Enumerable.Range(0, random.Next(1, 6)).Select(_ => (MessageValue)new TextValue(Text(random, 240)))),
                _ => Generate(random, depth + 1),
            };
            values.Add(($"v{index}", value));
        }

        return Message.Of("a.b", [.. values]);
    }

    /// <summary>Text at <paramref name="cap"/> characters, mixing letters JSON keeps and ones it escapes.</summary>
    private static string Text(Random random, int cap)
    {
        const string alphabet = "abcXYZ019 Фільмиїєґ“”«»\"\\/<>&'…";
        var builder = new StringBuilder(cap);
        while (builder.Length < cap)
            builder.Append(alphabet[random.Next(alphabet.Length)]);
        return builder.ToString();
    }
}
