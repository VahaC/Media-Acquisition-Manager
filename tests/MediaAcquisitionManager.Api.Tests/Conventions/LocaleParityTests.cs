using System.Text.Json;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — the server's locale files hold to the web app's parity rules (<see cref="LocaleParity"/>, a port
/// of <c>frontend/src/locales/parity.ts</c>, checked here against that file's own cases) and to the server's: every
/// template parses, a count is a number, English writes every counted noun with <c>_one</c> and <c>_other</c>, no two
/// placeholders touch, a whole sentence ends like one, a Ukrainian file is complete once it exists, and the startup
/// converter's older wordings name real keys (design §2.4).
/// </summary>
public sealed class LocaleParityTests
{
    /// <summary>English keys allowed two placeholders side by side: a template that is one value (design §2.2).</summary>
    private static readonly HashSet<string> MayTouch = new(StringComparer.Ordinal)
    {
        "common.legacy-text", "common.quoted", "common.message-unavailable",
    };

    [Theory]
    // parity.ts: passes a translation with the same keys, placeholders, tags and whitespace
    [InlineData(
        """{ "title": "Jobs", "page": { "status": "Page {{page}} of {{pages}}", "separator": " · " }, "link": "See <page>Removed copies</page>." }""",
        """{ "title": "Zadannia", "page": { "status": "Storinka {{page}} z {{pages}}", "separator": " · " }, "link": "Dyvit <page>Vyluchene</page>." }""",
        new string[0])]
    // parity.ts: names a key the translation is missing, and one the source does not have
    [InlineData(
        """{ "title": "Jobs", "empty": "No jobs yet." }""",
        """{ "title": "Zadannia", "emptyy": "Nemaie." }""",
        new[] { "empty: missing", "emptyy: not in the source" })]
    // parity.ts: wants exactly the four Ukrainian plural forms for every English plural
    [InlineData(
        """{ "copies_one": "{{value}} copy", "copies_other": "{{value}} copies" }""",
        """{ "copies_one": "{{value}} kopiia", "copies_few": "{{value}} kopii", "copies_other": "{{value}} kopii", "copies_two": "{{value}} kopii" }""",
        new[] { "copies_many: missing", "copies_two: not in the source" })]
    // parity.ts: takes a plural the source writes with _other alone
    [InlineData(
        """{ "heldBy_other": "Held ({{value}} of these copies)" }""",
        """{ "heldBy_one": "A {{value}}", "heldBy_few": "B {{value}}", "heldBy_many": "C {{value}}", "heldBy_other": "D {{value}}" }""",
        new string[0])]
    // parity.ts: names an empty value and one that is not a string
    [InlineData(
        """{ "title": "Jobs", "lead": "Your downloads." }""",
        """{ "title": "  ", "lead": 7 }""",
        new[] { "title: empty", "lead: not a string" })]
    // parity.ts: wants the same leading and trailing whitespace as the source
    [InlineData(
        """{ "told": " Told {{client}}.", "separator": ", " }""",
        """{ "told": "Povidomleno {{client}}.", "separator": "," }""",
        new[] { "told: starts or ends with other whitespace than the source", "separator: starts or ends with other whitespace than the source" })]
    // parity.ts: wants a plain value to use exactly the source placeholders, formats included
    [InlineData(
        """{ "title": "Edit {{name}}", "folders": "In {{names, list}}" }""",
        """{ "title": "Redahuvaty {{nmae}}", "folders": "U {{names}}" }""",
        new[] { "title: placeholders [nmae], the source has [name]", "folders: placeholders [names], the source has [names,list]" })]
    // parity.ts: lets each plural form use any of the source forms placeholders, as long as together they use all
    [InlineData(
        """{ "restore_one": "Restore the old copy", "restore_other": "Restore {{value}} old copies" }""",
        """{ "restore_one": "Vidnovyty {{value}} staru kopiiu", "restore_few": "{{value}} kopii", "restore_many": "{{value}} kopii", "restore_other": "{{value}} kopii" }""",
        new string[0])]
    [InlineData(
        """{ "restore_one": "Restore the old copy", "restore_other": "Restore {{value}} old copies" }""",
        """{ "restore_one": "A", "restore_few": "B", "restore_many": "C", "restore_other": "D" }""",
        new[] { "restore: no form uses [value]" })]
    [InlineData(
        """{ "restore_one": "Restore the old copy", "restore_other": "Restore {{value}} old copies" }""",
        """{ "restore_one": "A", "restore_few": "{{valeu}}", "restore_many": "{{value}}", "restore_other": "{{value}}" }""",
        new[] { "restore_few: placeholders [valeu] are in none of the source's forms" })]
    // parity.ts: wants the source <Trans> tags
    [InlineData(
        """{ "searched": "Nothing for <strong>“<text/>”</strong>.", "link": "See <page>Removed</page>." }""",
        """{ "searched": "Nichoho dlia <strong>«<text/>»</strong>.", "link": "Dyvit <pg>Vyluchene</pg>." }""",
        new[] { "link: tags [</pg>, <pg>], the source has [</page>, <page>]" })]
    // parity.ts: refuses English quotes always, and an ASCII apostrophe or quote unless the word is the source own
    [InlineData(
        """{ "name": "Remove “{{name}}”?", "note": "It can't be undone.", "command": "curl -H \"Authorization: Bearer {{token}}\" {{origin}}/api" }""",
        """{ "name": "Vydalyty “{{name}}”?", "note": "Tse ne mozhna skasuvaty, im'ia \"tse\".", "command": "curl -H \"Authorization: Bearer {{token}}\" {{origin}}/api" }""",
        new[] { "name: “…” quotes in “{{name}}”?", "note: an ASCII apostrophe or quote in im'ia \"tse\"." })]
    public void Problems_answers_the_web_apps_own_parity_cases(string source, string translated, string[] expected)
    {
        //Arrange
        using var sourceDocument = JsonDocument.Parse(source);
        using var translatedDocument = JsonDocument.Parse(translated);

        //Act
        var problems = LocaleParity.Problems(sourceDocument.RootElement, translatedDocument.RootElement, LocaleParity.UkrainianPluralForms);

        //Assert
        Assert.Equal(expected, problems);
    }

    [Fact]
    public void Every_english_template_follows_the_server_rules()
    {
        //Arrange
        var english = LocaleFiles.Language("en");

        //Act
        var problems = english
            .SelectMany(file => LocaleParity.ServerProblems(LocaleFiles.Leaves(file.Key, file.Value), isEnglish: true, MayTouch.Contains, IsClause))
            .ToList();

        //Assert
        Assert.True(english.ContainsKey("common"), "No en/common.json is embedded.");
        Assert.True(problems.Count == 0, $"English templates break the server's rules (design §2.2, §2.4):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    public void Every_ukrainian_file_holds_to_its_english_file()
    {
        //Arrange
        var english = LocaleFiles.Language("en");
        var ukrainian = LocaleFiles.Language("uk");

        //Act
        var problems = new List<string>();
        foreach (var (file, translated) in ukrainian)
        {
            if (!english.TryGetValue(file, out var source))
            {
                problems.Add($"uk/{file}.json: there is no en/{file}.json");
                continue;
            }

            var sourceLeaves = LocaleFiles.Leaves(file, source);
            var translatedLeaves = LocaleFiles.Leaves(file, translated);
            problems.AddRange(LocaleParity.Problems(source, translated, LocaleParity.UkrainianPluralForms).Select(problem => $"uk/{file}.json {problem}"));
            problems.AddRange(LocaleParity.PunctuationProblems(sourceLeaves, translatedLeaves));
            problems.AddRange(LocaleParity.ServerProblems(translatedLeaves, isEnglish: false, MayTouch.Contains, IsClause));
        }

        //Assert
        Assert.True(problems.Count == 0, $"Ukrainian files depart from their English files:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    public void Older_wordings_name_english_keys_with_their_placeholders()
    {
        //Arrange
        var english = LocaleFiles.Language("en").SelectMany(file => LocaleFiles.Leaves(file.Key, file.Value)).ToList();
        var older = LocaleFiles.Embedded.TryGetValue("older-en.json", out var json) ? json : "{}";
        using var document = JsonDocument.Parse(older);

        //Act
        var problems = LocaleParity.OlderWordingProblems(document.RootElement, english);

        //Assert
        Assert.True(problems.Count == 0, $"older-en.json departs from the English keys:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // MAM-47 WP14 — every English file has its reviewed Ukrainian file now, so a missing one is a defect, not a file
    // still in translation: without it a Ukrainian reader would get that area's sentences in English, unseen.
    [Fact]
    public void Every_english_file_has_its_ukrainian_file()
    {
        //Arrange
        var english = LocaleFiles.Language("en").Keys.Order(StringComparer.Ordinal);

        //Act
        var ukrainian = LocaleFiles.Language("uk").Keys.Order(StringComparer.Ordinal);

        //Assert
        Assert.Equal(english, ukrainian);
    }

    [Theory]
    [InlineData("common.legacy-text", """{ "legacy-text": "{{text}}" }""", new string[0])]
    [InlineData("common.x", """{ "a": "{{one}}{{two}}." }""", new[] { "common.a: {{one}} and {{two}} touch; the startup converter needs words between two placeholders" })]
    [InlineData("common.x", """{ "b": "Copied {{count}} files." }""", new[] { "common.b: {{count}} has the hint \"\"; a count is a number, number:grouped or number:word" })]
    [InlineData("common.x", """{ "c_other": "{{count, number}} files" }""", new[] { "common.c: no _one form; English writes every counted noun with _one and _other", "common.c_other: a whole sentence ends with . ? ! … or a nested sentence; a clause's key ends with -clause or sits under .tally." })]
    [InlineData("common.x", """{ "d": "It failed: {{error, message}}", "e-clause": "it failed", "tally": { "f": "two left" } }""", new string[0])]
    [InlineData("common.x", """{ "g": "Broken {{x, nonsense}}." }""", new[] { "common.g: doesn't parse" })]
    public void ServerProblems_names_each_rule_an_english_file_breaks(string mayTouch, string json, string[] expected)
    {
        //Arrange
        using var document = JsonDocument.Parse(json);
        var leaves = LocaleFiles.Leaves("common", document.RootElement);

        //Act
        var problems = LocaleParity.ServerProblems(leaves, isEnglish: true, key => key == mayTouch, IsClause);

        //Assert
        // A parse failure carries the parser's own words after its key: compared up to them.
        Assert.Equal(expected, problems.Select(problem => problem.Contains("doesn't parse", StringComparison.Ordinal) ? problem[..problem.IndexOf(" (", StringComparison.Ordinal)] : problem));
    }

    // The guards of the two rules no file breaks today (every Ukrainian file holds and older-en.json is empty): each
    // shows its rule still names a file that breaks it.

    [Theory]
    [InlineData("""{ "a": "It failed.", "b": "the client" }""", """{ "a": "Не вдалося.", "b": "клієнт" }""", new string[0])]
    [InlineData(
        """{ "a": "It failed.", "b": "the client" }""",
        """{ "a": "Не вдалося", "b": "клієнт." }""",
        new[] { "x.a: ends with terminal punctuation where the English doesn't, or the other way round", "x.b: ends with terminal punctuation where the English doesn't, or the other way round" })]
    [InlineData(
        """{ "c_one": "{{count, number}} file was moved.", "c_other": "{{count, number}} files were moved." }""",
        """{ "c_one": "{{count, number}} файл переміщено.", "c_few": "{{count, number}} файли переміщено.", "c_many": "{{count, number}} файлів переміщено", "c_other": "{{count, number}} файлу переміщено." }""",
        new[] { "x.c_many: ends with terminal punctuation where the English doesn't, or the other way round" })]
    public void PunctuationProblems_names_a_ukrainian_value_that_ends_unlike_its_english(string english, string ukrainian, string[] expected)
    {
        //Arrange
        using var englishDocument = JsonDocument.Parse(english);
        using var ukrainianDocument = JsonDocument.Parse(ukrainian);

        //Act
        var problems = LocaleParity.PunctuationProblems(
            LocaleFiles.Leaves("x", englishDocument.RootElement), LocaleFiles.Leaves("x", ukrainianDocument.RootElement));

        //Assert
        Assert.Equal(expected, problems);
    }

    [Theory]
    [InlineData("""{ "x.a": ["There was no file at “{{path}}”."] }""", new string[0])]
    [InlineData("""{ "x.gone": ["Old words."] }""", new[] { "older-en x.gone: no such English key" })]
    [InlineData("""{ "x.a": "Old words." }""", new[] { "older-en x.a: not an array of wordings" })]
    [InlineData("""{ "x.a": [7, "Old {{path"] }""", new[] { "older-en x.a[0]: not a string", "older-en x.a[1]: doesn't parse" })]
    [InlineData(
        """{ "x.a": ["Old {{file}}.", "Old “{{path, vendor}}”."] }""",
        new[] { "older-en x.a[0]: {{file}} isn't a placeholder of the key", "older-en x.a[1]: {{path}} has the hint \"vendor\", the key's has \"\"" })]
    public void OlderWordingProblems_names_each_rule_an_older_wording_breaks(string older, string[] expected)
    {
        //Arrange
        using var englishDocument = JsonDocument.Parse("""{ "a": "There is no file at “{{path}}”." }""");
        using var olderDocument = JsonDocument.Parse(older);

        //Act
        var problems = LocaleParity.OlderWordingProblems(olderDocument.RootElement, LocaleFiles.Leaves("x", englishDocument.RootElement));

        //Assert
        // A parse failure carries the parser's own words after its key: compared up to them.
        Assert.Equal(expected, problems.Select(problem => problem.Contains("doesn't parse", StringComparison.Ordinal) ? problem[..problem.IndexOf(" (", StringComparison.Ordinal)] : problem));
    }

    /// <summary>A key whose template is a clause or one of the engine's pieces, not a whole sentence (design §2.2).</summary>
    private static bool IsClause(string key) =>
        key.Contains(".tally.", StringComparison.Ordinal)
        || key.EndsWith("-clause", StringComparison.Ordinal)
        || key is "common.quoted" or "common.legacy-text"
        || key.StartsWith("common.list.", StringComparison.Ordinal)
        || key.StartsWith("common.size.", StringComparison.Ordinal)
        // MAM-47 WP2 — the edition labels, nested under list:*:message as the web app's own labels (design §1.2, §2.2).
        || key.StartsWith("common.edition.", StringComparison.Ordinal)
        // MAM-47 WP9 — the consent words, which end with no full stop: the dialog's checkbox label (design §5.5).
        || key.StartsWith("replacements.confirm.", StringComparison.Ordinal)
        // MAM-47 WP12 — a work's label where it has no title, put in a {{work, name}} slot as a title would be.
        || key.StartsWith("replacements.pairing.work.", StringComparison.Ordinal);
}
