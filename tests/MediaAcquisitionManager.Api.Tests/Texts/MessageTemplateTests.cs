using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — templates use i18next's placeholder syntax plus a hint on every non-text placeholder; a malformed
// one is a defect of the locale file, refused when the catalogue loads it.
public sealed class MessageTemplateTests
{
    [Fact]
    public void Parse_splits_words_and_placeholders_in_their_order()
    {
        //Arrange
        const string source = "“{{file}}” couldn't be moved: {{ error , message }} {{count, number}}.";

        //Act
        var template = MessageTemplate.Parse(source);

        //Assert
        Assert.Equal(
            [
                new TemplateLiteral("“"),
                new TemplateSlot("file", SlotHint.Text),
                new TemplateLiteral("” couldn't be moved: "),
                new TemplateSlot("error", new SlotHint(SlotKind.Message)),
                new TemplateLiteral(" "),
                new TemplateSlot("count", new SlotHint(SlotKind.Number)),
                new TemplateLiteral("."),
            ],
            template.Parts);
        Assert.Equal(["file", "error", "count"], template.Slots.Select(slot => slot.Name));
        Assert.Equal(source, template.Source);
    }

    [Fact]
    public void Parse_allows_a_placeholder_twice_and_a_template_of_one_placeholder()
    {
        //Arrange

        //Act
        var twice = MessageTemplate.Parse("{{name}} and {{name}}");
        var single = MessageTemplate.Parse("{{text}}");

        //Assert
        Assert.Equal(["name", "name"], twice.Slots.Select(slot => slot.Name));
        Assert.Equal([new TemplateSlot("text", SlotHint.Text)], single.Parts);
    }

    [Theory]
    [InlineData("", SlotKind.Text, ListJoin.None)]
    [InlineData("vendor", SlotKind.Vendor, ListJoin.None)]
    [InlineData("number", SlotKind.Number, ListJoin.None)]
    [InlineData("number:grouped", SlotKind.GroupedNumber, ListJoin.None)]
    [InlineData("number:word", SlotKind.WordNumber, ListJoin.None)]
    [InlineData("size", SlotKind.Size, ListJoin.None)]
    [InlineData("duration", SlotKind.Duration, ListJoin.None)]
    [InlineData("message", SlotKind.Message, ListJoin.None)]
    [InlineData("message:clause", SlotKind.Clause, ListJoin.None)]
    [InlineData("name", SlotKind.Name, ListJoin.None)]
    [InlineData("list:and", SlotKind.List, ListJoin.And)]
    [InlineData("list:or", SlotKind.List, ListJoin.Or)]
    [InlineData("list:comma", SlotKind.List, ListJoin.Comma)]
    [InlineData("list:space", SlotKind.List, ListJoin.Space)]
    [InlineData("list:and:quoted", SlotKind.QuotedList, ListJoin.And)]
    [InlineData("list:or:quoted", SlotKind.QuotedList, ListJoin.Or)]
    [InlineData("list:space:message", SlotKind.MessageList, ListJoin.Space)]
    [InlineData("list:comma:message", SlotKind.MessageList, ListJoin.Comma)]
    [InlineData("list:or:name", SlotKind.NameList, ListJoin.Or)]
    [InlineData("list:and:name", SlotKind.NameList, ListJoin.And)]
    public void SlotHint_parses_every_hint_of_the_grammar_and_spells_it_back(string hint, SlotKind kind, ListJoin join)
    {
        //Arrange

        //Act
        var parsed = SlotHint.Parse(hint);

        //Assert
        Assert.Equal(new SlotHint(kind, join), parsed);
        Assert.Equal(hint, parsed.ToString());
    }

    [Theory]
    [InlineData("Unclosed {{name")]
    [InlineData("Unclosed {{")]
    [InlineData("A stray }} closing")]
    [InlineData("No name {{ }}")]
    [InlineData("No name {{, number}}")]
    [InlineData("A digit first {{1st}}")]
    [InlineData("Two words {{two words}}")]
    [InlineData("Unknown hint {{x, bogus}}")]
    [InlineData("Unknown join {{x, list:nope}}")]
    [InlineData("Unknown item kind {{x, list:and:bogus}}")]
    [InlineData("Too long a hint {{x, list:and:quoted:more}}")]
    [InlineData("A list without a join {{x, list}}")]
    [InlineData("Number with a format {{x, number:0.##}}")]
    public void Parse_refuses_a_malformed_template(string source)
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => MessageTemplate.Parse(source));

        //Assert
        Assert.IsType<FormatException>(exception);
    }
}
