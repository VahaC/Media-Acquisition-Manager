using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the templates come from the Api assembly's embedded Locales/<language>/<file>.json, flattened to
// dotted keys under the file's name; a malformed file is a build defect refused at load, before the app answers.
public sealed class TemplateCatalogTests
{
    [Theory]
    [InlineData("common.quoted", "“{{text}}”")]
    [InlineData("common.list.separator", ", ")]
    [InlineData("common.list.and", " and ")]
    [InlineData("common.list.or", " or ")]
    [InlineData("common.size.mib_one", "{{count, number}} MiB")]
    [InlineData("common.size.mib_other", "{{count, number}} MiB")]
    [InlineData("common.size.kib_one", "{{count, number}} KiB")]
    [InlineData("common.size.kib_other", "{{count, number}} KiB")]
    [InlineData("common.size.bytes_one", "{{count, number}} byte")]
    [InlineData("common.size.bytes_other", "{{count, number}} bytes")]
    [InlineData("common.legacy-text", "{{text}}")]
    [InlineData("common.message-unavailable", "This message was written by another version of the app and can't be shown here ({{key}}).")]
    [InlineData("common.request.unknown-endpoint", "No API endpoint matches {{method}} {{path}}.")]
    public void Embedded_holds_the_engines_english_templates(string key, string source)
    {
        //Arrange
        var catalog = TemplateCatalog.Embedded;

        //Act
        var found = catalog.TryFind(TextLanguage.English, key, out var template);

        //Assert
        Assert.True(found, $"{key} is not in en.");
        Assert.Equal(source, template!.Source);
        Assert.True(catalog.HasFile(TextLanguage.English, "common"));
    }

    [Fact]
    public void Embedded_gives_the_pseudo_language_the_english_templates()
    {
        //Arrange
        var catalog = TemplateCatalog.Embedded;

        //Act
        var found = catalog.TryFind(TextLanguage.Pseudo, "common.quoted", out var template);

        //Assert
        Assert.True(found);
        Assert.Equal("“{{text}}”", template!.Source);
        Assert.Equal(catalog.Keys(TextLanguage.English), catalog.Keys(TextLanguage.Pseudo));
    }

    [Fact]
    public void From_flattens_nested_objects_under_the_files_name()
    {
        //Arrange
        const string json = """{ "move": { "stopped": "The app stopped.", "deep": { "er": "Deeper." } }, "top": "Top." }""";

        //Act
        var catalog = TemplateCatalog.From(("en/replacements.json", json));

        //Assert
        Assert.Equal(
            ["replacements.move.deep.er", "replacements.move.stopped", "replacements.top"],
            catalog.Keys(TextLanguage.English).Order(StringComparer.Ordinal));
        Assert.True(catalog.HasFile(TextLanguage.English, "replacements"));
        Assert.False(catalog.HasFile(TextLanguage.Ukrainian, "replacements"));
        Assert.Empty(catalog.Keys(TextLanguage.Ukrainian));
    }

    [Fact]
    public void ValueNamesOf_gathers_every_plural_forms_names_and_the_count()
    {
        //Arrange
        const string json = """{ "left_one": "A minute is left for {{name}}.", "left_other": "{{count, number}} minutes are left, {{other}}.", "plain": "{{path}}" }""";
        var catalog = TemplateCatalog.From(("en/sample.json", json));

        //Act
        var counted = catalog.ValueNamesOf("sample.left");
        var plain = catalog.ValueNamesOf("sample.plain");
        var unknown = catalog.ValueNamesOf("sample.none");

        //Assert
        Assert.Equal(["count", "name", "other"], counted.Order(StringComparer.Ordinal));
        Assert.Equal(["path"], plain);
        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData("files.copy_one", "files.copy")]
    [InlineData("files.copy_other", "files.copy")]
    [InlineData("files.copy_few", "files.copy")]
    [InlineData("files.copy", "files.copy")]
    [InlineData("files.one", "files.one")]
    public void BaseKeyOf_drops_a_plural_suffix(string key, string baseKey)
    {
        //Arrange

        //Act
        var found = TemplateCatalog.BaseKeyOf(key);

        //Assert
        Assert.Equal(baseKey, found);
    }

    [Theory]
    [InlineData("en/common.json", "[\"x\"]")]
    [InlineData("en/common.json", "{ \"a\": 1 }")]
    [InlineData("en/common.json", "{ \"a\": [\"x\"] }")]
    [InlineData("en/common.json", "{ \"Bad Key\": \"x\" }")]
    [InlineData("en/common.json", "{ \"a\": \"{{unclosed\" }")]
    [InlineData("en/common.json", "{ \"a\": \"x\", \"a\": \"y\" }")]
    [InlineData("en/common.json", "{ \"a\": ")]
    [InlineData("de/common.json", "{ \"a\": \"x\" }")]
    [InlineData("common.json", "{ \"a\": \"x\" }")]
    [InlineData("en/deep/common.json", "{ \"a\": \"x\" }")]
    [InlineData("en/Common.json", "{ \"a\": \"x\" }")]
    [InlineData("en/common.txt", "{ \"a\": \"x\" }")]
    public void From_refuses_a_malformed_locale_file(string path, string json)
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => TemplateCatalog.From((path, json)));

        //Assert
        Assert.NotNull(exception);
        Assert.True(exception is InvalidDataException or System.Text.Json.JsonException, exception.GetType().Name);
    }

    [Fact]
    public void From_refuses_the_same_file_twice()
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => TemplateCatalog.From(("en/common.json", "{}"), ("en/common.json", "{}")));

        //Assert
        Assert.IsType<InvalidDataException>(exception);
    }
}
