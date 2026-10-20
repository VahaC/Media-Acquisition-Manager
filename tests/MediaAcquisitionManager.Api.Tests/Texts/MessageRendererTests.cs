using System.Globalization;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the renderer over an in-memory catalogue that has Ukrainian files (the embedded one gets its
// first in WP13/WP14): every hint in both languages, nesting, lists, the pseudo-language's wrapping, and the fallbacks —
// strict in tests (the test that reaches a broken sentence fails there), lenient in production (never throws, says
// what it worked around).
public sealed class MessageRendererTests
{
    private const string Nbsp = " ";

    private static readonly TemplateCatalog Catalog = TemplateCatalog.From(
        ("en/common.json", EnglishCommon),
        ("uk/common.json", UkrainianCommon),
        ("en/sample.json", EnglishSample),
        ("uk/sample.json", UkrainianSample),
        ("en/draft.json", """{ "note": "Not translated yet, {{name}}." }"""));

    private const string EnglishCommon = """
        {
          "quoted": "“{{text}}”",
          "list": { "separator": ", ", "and": " and ", "or": " or " },
          "size": {
            "mib_one": "{{count, number}} MiB", "mib_other": "{{count, number}} MiB",
            "kib_one": "{{count, number}} KiB", "kib_other": "{{count, number}} KiB",
            "bytes_one": "{{count, number}} byte", "bytes_other": "{{count, number}} bytes"
          },
          "legacy-text": "{{text}}",
          "message-unavailable": "This message was written by another version of the app and can't be shown here ({{key}})."
        }
        """;

    private const string UkrainianCommon = """
        {
          "quoted": "«{{text}}»",
          "list": { "separator": ", ", "and": " і ", "or": " або " },
          "size": {
            "mib_one": "{{count, number}} МіБ", "mib_few": "{{count, number}} МіБ", "mib_many": "{{count, number}} МіБ", "mib_other": "{{count, number}} МіБ",
            "kib_one": "{{count, number}} КіБ", "kib_few": "{{count, number}} КіБ", "kib_many": "{{count, number}} КіБ", "kib_other": "{{count, number}} КіБ",
            "bytes_one": "{{count, number}} Б", "bytes_few": "{{count, number}} Б", "bytes_many": "{{count, number}} Б", "bytes_other": "{{count, number}} Б"
          },
          "legacy-text": "{{text}}",
          "message-unavailable": "Це повідомлення записала інша версія застосунку, і тут його не показати ({{key}})."
        }
        """;

    private const string EnglishSample = """
        {
          "stopped": "The app stopped while moving the old copies.",
          "no-file": "There is no file at “{{path}}”.",
          "said": "The service said “{{said, vendor}}”.",
          "moved_one": "{{count, number}} file was moved.",
          "moved_other": "{{count, number}} files were moved.",
          "left_one": "One minute is left.",
          "left_other": "{{count, number}} minutes are left.",
          "bytes": "It is {{size, number:grouped}} bytes on the disk.",
          "new-files_one": "{{count, number:word}} new file is here.",
          "new-files_other": "{{count, number:word}} new files are here.",
          "limit": "The answer is larger than {{limit, size}}.",
          "runs": "It runs {{time, duration}}.",
          "failed": "The move failed: {{reason, message}}",
          "nothing-sent": "{{reason, message:clause}}, so nothing was sent.",
          "names": "Missing: {{names, list:and}}.",
          "either": "Pick {{names, list:or:quoted}}.",
          "ids": "Ids: {{ids, list:comma}}.",
          "why": "{{reasons, list:space:message}}",
          "twice": "{{name}} and {{name}} again.",
          "untitled": "a film with no title",
          "untitled-year": "a film with no title ({{year}})",
          "no-new-file": "No new file is {{work, name}}.",
          "fits": "It fits {{works, list:or:name}}.",
          "only-en": "Only English has this."
        }
        """;

    private const string UkrainianSample = """
        {
          "stopped": "Застосунок зупинився під час переміщення старих копій.",
          "no-file": "Немає файлу «{{path}}».",
          "said": "Сервіс відповів «{{said, vendor}}».",
          "moved_one": "{{count, number}} файл переміщено.",
          "moved_few": "{{count, number}} файли переміщено.",
          "moved_many": "{{count, number}} файлів переміщено.",
          "moved_other": "{{count, number}} файлу переміщено.",
          "left_one": "Лишилася {{count, number}} хвилина.",
          "left_few": "Лишилося {{count, number}} хвилини.",
          "left_many": "Лишилося {{count, number}} хвилин.",
          "left_other": "Лишилося {{count, number}} хвилини.",
          "bytes": "На диску {{size, number:grouped}} байтів.",
          "new-files_one": "{{count, number:word}} новий файл тут.",
          "new-files_few": "{{count, number:word}} нові файли тут.",
          "new-files_many": "{{count, number:word}} нових файлів тут.",
          "new-files_other": "{{count, number:word}} нового файлу тут.",
          "limit": "Відповідь більша за {{limit, size}}.",
          "runs": "Триває {{time, duration}}.",
          "failed": "Не вдалося перемістити: {{reason, message}}",
          "nothing-sent": "{{reason, message:clause}}, тому нічого не надіслано.",
          "names": "Бракує: {{names, list:and}}.",
          "either": "Виберіть {{names, list:or:quoted}}.",
          "ids": "Ідентифікатори: {{ids, list:comma}}.",
          "why": "{{reasons, list:space:message}}",
          "twice": "{{name}} і {{name}} знову.",
          "untitled": "без назви",
          "untitled-year": "без назви ({{year}})",
          "no-new-file": "Жоден новий файл не є фільмом {{work, name}}.",
          "fits": "Підходить фільм {{works, list:or:name}}."
        }
        """;

    private static readonly Message NoFile = Message.Of("sample.no-file", ("path", "/a"));

    private static readonly Message Stopped = Message.Of("sample.stopped");

    // ── words and values ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TextLanguage.English, "The app stopped while moving the old copies.")]
    [InlineData(TextLanguage.Ukrainian, "Застосунок зупинився під час переміщення старих копій.")]
    [InlineData(TextLanguage.Pseudo, "⟦The app stopped while moving the old copies.⟧")]
    public void Render_writes_a_sentence_in_the_language_asked_for(TextLanguage language, string expected)
    {
        //Arrange

        //Act
        var written = Render(Stopped, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "There is no file at “/media/Фільми/a.mkv”.")]
    [InlineData(TextLanguage.Ukrainian, "Немає файлу «/media/Фільми/a.mkv».")]
    public void Render_writes_a_text_value_inside_the_languages_own_quotes(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.no-file", ("path", "/media/Фільми/a.mkv"));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Render_inserts_a_value_as_it_is_without_reading_it_as_a_template()
    {
        //Arrange
        var message = Message.Of("sample.no-file", ("path", "{{count}} {0} {{path, number}} ⟦x⟧"));

        //Act
        var written = Render(message, TextLanguage.English);

        //Assert
        Assert.Equal("There is no file at “{{count}} {0} {{path, number}} ⟦x⟧”.", written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "The service said “Device or resource busy (errno 16)”.")]
    [InlineData(TextLanguage.Ukrainian, "Сервіс відповів «Device or resource busy (errno 16)».")]
    public void Render_writes_a_services_words_verbatim(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.said", ("said", VendorText.Clean("Device or resource busy (errno 16)", 300)));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Render_writes_a_placeholder_named_twice_twice()
    {
        //Arrange
        var message = Message.Of("sample.twice", ("name", "Alien"));

        //Act
        var written = Render(message, TextLanguage.English);

        //Assert
        Assert.Equal("Alien and Alien again.", written);
    }

    // ── counts and formats ──────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TextLanguage.English, "1", "1 file was moved.")]
    [InlineData(TextLanguage.English, "0", "0 files were moved.")]
    [InlineData(TextLanguage.English, "2", "2 files were moved.")]
    [InlineData(TextLanguage.English, "1.5", "1.5 files were moved.")]
    [InlineData(TextLanguage.English, "1234", "1234 files were moved.")]
    [InlineData(TextLanguage.Ukrainian, "1", "1 файл переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "2", "2 файли переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "3", "3 файли переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "5", "5 файлів переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "11", "11 файлів переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "21", "21 файл переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "22", "22 файли переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "25", "25 файлів переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "1.5", "1,5 файлу переміщено.")]
    [InlineData(TextLanguage.Ukrainian, "1234", "1" + Nbsp + "234 файли переміщено.")]
    public void Render_picks_the_plural_form_of_the_count(TextLanguage language, string count, string expected)
    {
        //Arrange
        var message = Message.Of("sample.moved", ("count", decimal.Parse(count, CultureInfo.InvariantCulture)));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Render_takes_a_count_that_only_picks_the_form_as_used()
    {
        //Arrange
        var message = Message.Of("sample.left", ("count", 1));

        //Act
        var written = Render(message, TextLanguage.English);

        //Assert
        Assert.Equal("One minute is left.", written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "It is 1,234,567 bytes on the disk.")]
    [InlineData(TextLanguage.Ukrainian, "На диску 1" + Nbsp + "234" + Nbsp + "567 байтів.")]
    public void Render_writes_a_grouped_number_as_the_language_groups_it(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.bytes", ("size", 1_234_567));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, 2, "Two new files are here.")]
    [InlineData(TextLanguage.English, 10, "Ten new files are here.")]
    [InlineData(TextLanguage.English, 11, "11 new files are here.")]
    [InlineData(TextLanguage.English, 1, "1 new file is here.")]
    [InlineData(TextLanguage.Ukrainian, 2, "2 нові файли тут.")]
    public void Render_spells_a_count_at_a_sentences_start_in_english_only(TextLanguage language, int count, string expected)
    {
        //Arrange
        var message = Message.Of("sample.new-files", ("count", count));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, 8_388_608L, "The answer is larger than 8 MiB.")]
    [InlineData(TextLanguage.English, 1_572_864L, "The answer is larger than 1536 KiB.")]
    [InlineData(TextLanguage.English, 1_000L, "The answer is larger than 1000 bytes.")]
    [InlineData(TextLanguage.English, 1L, "The answer is larger than 1 byte.")]
    [InlineData(TextLanguage.Ukrainian, 8_388_608L, "Відповідь більша за 8 МіБ.")]
    [InlineData(TextLanguage.Ukrainian, 1_572_864L, "Відповідь більша за 1" + Nbsp + "536 КіБ.")]
    [InlineData(TextLanguage.Ukrainian, 1_000L, "Відповідь більша за 1" + Nbsp + "000 Б.")]
    [InlineData(TextLanguage.Pseudo, 4_194_304L, "⟦The answer is larger than 4 MiB.⟧")]
    public void Render_writes_a_size_in_its_exact_unit(TextLanguage language, long bytes, string expected)
    {
        //Arrange
        var message = Message.Of("sample.limit", ("limit", new SizeValue(bytes)));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "It runs 1:56:30.")]
    [InlineData(TextLanguage.Ukrainian, "Триває 1:56:30.")]
    public void Render_writes_a_running_time_as_the_servers_clock(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.runs", ("time", new DurationValue(6_990_000)));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    // ── nesting and lists ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TextLanguage.English, "The move failed: There is no file at “/a”.")]
    [InlineData(TextLanguage.Ukrainian, "Не вдалося перемістити: Немає файлу «/a».")]
    [InlineData(TextLanguage.Pseudo, "⟦The move failed: ⟦There is no file at “/a”.⟧⟧")]
    public void Render_writes_a_nested_message_in_the_same_language(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.failed", ("reason", NoFile));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "There is no file at “/a”, so nothing was sent.")]
    [InlineData(TextLanguage.Ukrainian, "Немає файлу «/a», тому нічого не надіслано.")]
    [InlineData(TextLanguage.Pseudo, "⟦⟦There is no file at “/a”⟧, so nothing was sent.⟧")]
    public void Render_writes_a_clause_without_its_final_full_stop(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.nothing-sent", ("reason", NoFile));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, 0, "Missing: .")]
    [InlineData(TextLanguage.English, 1, "Missing: a.")]
    [InlineData(TextLanguage.English, 2, "Missing: a and b.")]
    [InlineData(TextLanguage.English, 3, "Missing: a, b and c.")]
    [InlineData(TextLanguage.English, 4, "Missing: a, b, c and d.")]
    [InlineData(TextLanguage.Ukrainian, 2, "Бракує: a і b.")]
    [InlineData(TextLanguage.Ukrainian, 3, "Бракує: a, b і c.")]
    public void Render_joins_a_list_the_languages_way_without_an_oxford_comma(TextLanguage language, int count, string expected)
    {
        //Arrange
        var names = new ListValue(new[] { "a", "b", "c", "d" }.Take(count).Select(name => (MessageValue)new TextValue(name)));
        var message = Message.Of("sample.names", ("names", names));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, 1, "Pick “a”.")]
    [InlineData(TextLanguage.English, 2, "Pick “a” or “b”.")]
    [InlineData(TextLanguage.English, 3, "Pick “a”, “b” or “c”.")]
    [InlineData(TextLanguage.Ukrainian, 3, "Виберіть «a», «b» або «c».")]
    [InlineData(TextLanguage.Pseudo, 2, "⟦Pick “a” or “b”.⟧")]
    public void Render_quotes_each_item_of_a_quoted_list_in_the_languages_quotes(TextLanguage language, int count, string expected)
    {
        //Arrange
        var names = new ListValue(new MessageValue[] { new TextValue("a"), VendorText.Stored("b"), new TextValue("c") }.Take(count));
        var message = Message.Of("sample.either", ("names", names));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "Ids: 1234, 7, tt0816692.")]
    [InlineData(TextLanguage.Ukrainian, "Ідентифікатори: 1" + Nbsp + "234, 7, tt0816692.")]
    public void Render_joins_a_comma_list_and_writes_its_numbers_in_the_language(TextLanguage language, string expected)
    {
        //Arrange
        var ids = new ListValue([new NumberValue(1234), new NumberValue(7), new TextValue("tt0816692")]);
        var message = Message.Of("sample.ids", ("ids", ids));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "There is no file at “/a”. The app stopped while moving the old copies.")]
    [InlineData(TextLanguage.Ukrainian, "Немає файлу «/a». Застосунок зупинився під час переміщення старих копій.")]
    [InlineData(TextLanguage.Pseudo, "⟦⟦There is no file at “/a”.⟧ ⟦The app stopped while moving the old copies.⟧⟧")]
    public void Render_writes_a_message_list_as_sentences_one_after_another(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.why", ("reasons", new ListValue([NoFile, Stopped])));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    // MAM-47 WP12 — a name slot takes a name as it is, or the label of a thing with no name, in the reader's language:
    // the pairing names a work with no title so (ReplacementPairingWork.NameOf), never by English words passed as data.
    [Theory]
    [InlineData(TextLanguage.English, "No new file is Alien (1979).")]
    [InlineData(TextLanguage.Ukrainian, "Жоден новий файл не є фільмом Alien (1979).")]
    [InlineData(TextLanguage.Pseudo, "⟦No new file is Alien (1979).⟧")]
    public void Render_writes_a_name_as_it_is(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.no-new-file", ("work", "Alien (1979)"));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "No new file is a film with no title (2005).")]
    [InlineData(TextLanguage.Ukrainian, "Жоден новий файл не є фільмом без назви (2005).")]
    [InlineData(TextLanguage.Pseudo, "⟦No new file is ⟦a film with no title (2005)⟧.⟧")]
    public void Render_writes_the_label_of_a_thing_with_no_name_in_the_same_language(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Of("sample.no-new-file", ("work", Message.Of("sample.untitled-year", ("year", "2005"))));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "It fits Alien (1979), Aliens (1986) or a film with no title.")]
    [InlineData(TextLanguage.Ukrainian, "Підходить фільм Alien (1979), Aliens (1986) або без назви.")]
    public void Render_joins_names_and_labels_of_a_name_list_the_languages_way(TextLanguage language, string expected)
    {
        //Arrange
        var works = new ListValue([new TextValue("Alien (1979)"), new TextValue("Aliens (1986)"), Message.Of("sample.untitled")]);
        var message = Message.Of("sample.fits", ("works", works));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    // MAM-47 — MessageFit ends a list it had to shorten with a "…" item; every kind of list writes it as it is, after the
    // separator, and the strict renderer takes it (a quoted or a sentence "…" would be wrong, or refused).
    [Theory]
    [InlineData("sample.names", TextLanguage.English, "Missing: a, b, ….")]
    [InlineData("sample.names", TextLanguage.Ukrainian, "Бракує: a, b, ….")]
    [InlineData("sample.either", TextLanguage.English, "Pick “a”, “b”, ….")]
    [InlineData("sample.either", TextLanguage.Ukrainian, "Виберіть «a», «b», ….")]
    [InlineData("sample.ids", TextLanguage.English, "Ids: a, b, ….")]
    [InlineData("sample.names", TextLanguage.Pseudo, "⟦Missing: a, b, ….⟧")]
    public void Render_writes_the_ellipsis_a_shortened_list_ends_with_as_it_is(string key, TextLanguage language, string expected)
    {
        //Arrange
        var name = key == "sample.ids" ? "ids" : "names";
        var message = Message.Of(key, (name, new ListValue([new TextValue("a"), new TextValue("b"), new TextValue("…")])));

        //Act
        var written = Render(message, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(TextLanguage.English, "There is no file at “/a”. There is no file at “/a”. ", "There is no file at “/a”. …")]
    [InlineData(TextLanguage.Pseudo, "⟦⟦There is no file at “/a”.⟧ ⟦There is no file at “/a”.⟧ ", "⟦There is no file at “/a”.⟧ …⟧")]
    public void Render_writes_a_message_list_the_fit_shortened(TextLanguage language, string start, string end)
    {
        //Arrange
        var reasons = new ListValue(Enumerable.Repeat<MessageValue>(NoFile, 50));
        var fitted = MessageFit.Fit(Message.Of("sample.why", ("reasons", reasons)), 300);

        //Act
        var written = Render(fitted, language);

        //Assert
        Assert.True(MessageFit.IsShortenedListEnd(Assert.IsType<ListValue>(fitted.Values["reasons"]).Items[^1]));
        Assert.StartsWith(start, written);
        Assert.EndsWith(end, written);
    }

    // ── writing with the wordings a parse matched (the startup converter's check, design §6.4) ──────────────────────

    [Fact]
    public void RenderWith_writes_each_message_with_the_wording_it_is_given()
    {
        //Arrange
        var older = MessageTemplate.Parse("Old: {{path}} was not found.");
        var nested = Message.Of("sample.no-file", ("path", "/a"));
        var message = Message.Of("sample.failed", ("reason", nested));

        //Act
        var written = MessageRenderer.RenderWith(Catalog, message, candidate => ReferenceEquals(candidate, nested) ? older : null);

        //Assert
        Assert.Equal("The move failed: Old: /a was not found.", written);
    }

    [Fact]
    public void RenderWith_writes_a_plural_form_the_count_would_not_pick()
    {
        //Arrange
        Catalog.TryFind(TextLanguage.English, "sample.moved_other", out var other);
        var message = Message.Of("sample.moved", ("count", 1));

        //Act
        var written = MessageRenderer.RenderWith(Catalog, message, candidate => ReferenceEquals(candidate, message) ? other : null);

        //Assert
        Assert.Equal("1 files were moved.", written);
    }

    [Fact]
    public void RenderWith_is_strict_about_a_value_the_wording_names()
    {
        //Arrange
        var older = MessageTemplate.Parse("Old: {{file}} was not found.");
        var message = Message.Of("sample.no-file", ("path", "/a"));

        //Act
        var exception = Record.Exception(() => MessageRenderer.RenderWith(Catalog, message, _ => older));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    [Fact]
    public void Render_of_null_is_null()
    {
        //Arrange

        //Act
        var written = MessageRenderer.Render(Catalog, null, TextLanguage.Ukrainian, strict: true);

        //Assert
        Assert.Null(written);
    }

    // ── fallbacks: strict throws, lenient writes the nearest readable thing and says so ────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_writes_english_while_the_ukrainian_file_has_not_landed(bool strict)
    {
        //Arrange
        var problems = new List<TextProblem>();
        var message = Message.Of("draft.note", ("name", "Ann"));

        //Act
        var written = MessageRenderer.Render(Catalog, message, TextLanguage.Ukrainian, strict, problems.Add);

        //Assert
        Assert.Equal("Not translated yet, Ann.", written);
        Assert.Empty(problems);
    }

    [Fact]
    public void Render_strict_throws_for_a_key_missing_from_a_ukrainian_file_that_exists()
    {
        //Arrange
        var message = Message.Of("sample.only-en");

        //Act
        var exception = Record.Exception(() => Render(message, TextLanguage.Ukrainian));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    [Fact]
    public void Render_lenient_writes_english_for_a_key_missing_from_a_ukrainian_file_that_exists_and_says_so()
    {
        //Arrange
        var problems = new List<TextProblem>();
        var message = Message.Of("sample.only-en");

        //Act
        var written = MessageRenderer.Render(Catalog, message, TextLanguage.Ukrainian, strict: false, problems.Add);

        //Assert
        Assert.Equal("Only English has this.", written);
        var problem = Assert.Single(problems);
        Assert.Equal(("sample.only-en", TextLanguage.Ukrainian), (problem.Key, problem.Language));
    }

    [Fact]
    public void Render_strict_throws_for_a_key_english_has_not()
    {
        //Arrange
        var message = Message.Of("sample.gone");

        //Act
        var exception = Record.Exception(() => Render(message, TextLanguage.English));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    [Theory]
    [InlineData(TextLanguage.English, "This message was written by another version of the app and can't be shown here (sample.gone).")]
    [InlineData(TextLanguage.Ukrainian, "Це повідомлення записала інша версія застосунку, і тут його не показати (sample.gone).")]
    [InlineData(TextLanguage.Pseudo, "⟦This message was written by another version of the app and can't be shown here (sample.gone).⟧")]
    public void Render_lenient_writes_that_another_version_wrote_a_key_this_one_has_not(TextLanguage language, string expected)
    {
        //Arrange
        var problems = new List<TextProblem>();
        var message = Message.Of("sample.gone", ("path", "/a"));

        //Act
        var written = MessageRenderer.Render(Catalog, message, language, strict: false, problems.Add);

        //Assert
        Assert.Equal(expected, written);
        Assert.Equal("sample.gone", Assert.Single(problems).Key);
    }

    [Fact]
    public void Render_lenient_writes_the_key_when_even_that_sentence_is_missing()
    {
        //Arrange
        var bare = TemplateCatalog.From(("en/sample.json", """{ "stopped": "Stopped." }"""));
        var message = Message.Of("sample.gone");

        //Act
        var written = MessageRenderer.Render(bare, message, TextLanguage.Ukrainian, strict: false);

        //Assert
        Assert.Equal("sample.gone", written);
    }

    [Fact]
    public void Render_strict_throws_for_a_missing_value()
    {
        //Arrange
        var message = Message.Of("sample.no-file");

        //Act
        var exception = Record.Exception(() => Render(message, TextLanguage.English));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    [Fact]
    public void Render_lenient_writes_an_ellipsis_for_a_missing_value_and_nothing_for_a_missing_sentence()
    {
        //Arrange
        var problems = new List<TextProblem>();

        //Act
        var path = MessageRenderer.Render(Catalog, Message.Of("sample.no-file"), TextLanguage.English, strict: false, problems.Add);
        var reason = MessageRenderer.Render(Catalog, Message.Of("sample.failed"), TextLanguage.English, strict: false, problems.Add);

        //Assert
        Assert.Equal("There is no file at “…”.", path);
        Assert.Equal("The move failed: ", reason);
        Assert.Equal(["sample.no-file", "sample.failed"], problems.Select(problem => problem.Key));
    }

    [Fact]
    public void Render_strict_throws_for_a_value_no_template_of_the_key_names()
    {
        //Arrange
        var message = Message.Of("sample.no-file", ("path", "/a"), ("pth", "/a"));

        //Act
        var exception = Record.Exception(() => Render(message, TextLanguage.English));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    public static TheoryData<Message> Misfits => new()
    {
        Message.Of("sample.said", ("said", "raw words, not a vendor's")),
        Message.Of("sample.no-file", ("path", 7)),
        Message.Of("sample.bytes", ("size", "1234567")),
        Message.Of("sample.new-files", ("count", 2), ("unused", 1)),
        Message.Of("sample.limit", ("limit", 8_388_608)),
        Message.Of("sample.runs", ("time", 6_990_000)),
        Message.Of("sample.failed", ("reason", "There is no file.")),
        Message.Of("sample.nothing-sent", ("reason", new DurationValue(1))),
        Message.Of("sample.names", ("names", "a and b")),
        Message.Of("sample.names", ("names", new ListValue([NoFile]))),
        Message.Of("sample.either", ("names", new ListValue([new NumberValue(1)]))),
        Message.Of("sample.why", ("reasons", new ListValue([new TextValue("There is no file.")]))),
        Message.Of("sample.moved", ("count", "1")),
        Message.Of("sample.no-file", ("path", Stopped)),
        Message.Of("sample.no-new-file", ("work", 7)),
        Message.Of("sample.fits", ("works", new ListValue([new NumberValue(7)]))),
    };

    [Theory]
    [MemberData(nameof(Misfits))]
    public void Render_strict_throws_for_a_value_that_doesnt_fit_its_hint(Message message)
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => Render(message, TextLanguage.English));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    [Fact]
    public void Render_lenient_writes_a_misfit_value_as_it_reads_and_says_so()
    {
        //Arrange
        var problems = new List<TextProblem>();
        var message = Message.Of("sample.limit", ("limit", 8_388_608));

        //Act
        var written = MessageRenderer.Render(Catalog, message, TextLanguage.English, strict: false, problems.Add);

        //Assert
        Assert.Equal("The answer is larger than 8388608.", written);
        Assert.Equal("sample.limit", Assert.Single(problems).Key);
    }

    // ── the embedded catalogue ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TextLanguage.English, "Old words “{{x}}” of an older build.")]
    [InlineData(TextLanguage.Ukrainian, "Old words “{{x}}” of an older build.")]
    [InlineData(TextLanguage.Pseudo, "⟦Old words “{{x}}” of an older build.⟧")]
    public void Render_shows_words_an_older_build_stored_as_they_are(TextLanguage language, string expected)
    {
        //Arrange
        var message = Message.Legacy("Old words “{{x}}” of an older build.");

        //Act
        var written = MessageRenderer.Render(message, language, strict: true);

        //Assert
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Render_of_the_unknown_endpoint_sentence_is_todays_english()
    {
        //Arrange
        var message = Message.Of("common.request.unknown-endpoint", ("method", "GET"), ("path", "/api/nope"));

        //Act
        var written = MessageRenderer.Render(message, TextLanguage.English, strict: true);

        //Assert
        Assert.Equal("No API endpoint matches GET /api/nope.", written);
    }

    private static string Render(Message message, TextLanguage language) =>
        MessageRenderer.Render(Catalog, message, language, strict: true);
}
