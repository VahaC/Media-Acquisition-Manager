using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — holds the server's locale files to the rules the web app's are held to, and to the server's own.
/// <see cref="Problems"/> is a port of <c>frontend/src/locales/parity.ts</c>' <c>localeParityProblems</c>, rule for rule
/// and message for message (its own test cases run against this port too, so the two can't drift apart); the server's
/// rules follow it.
/// </summary>
/// <remarks>
/// Not a shared node tool: the server's parity must run in the .NET suite, in Jenkins and in both passes.
/// </remarks>
public static partial class LocaleParity
{
    /// <summary>The Ukrainian plural forms (CLDR): 1, 21…; 2–4, 22–24…; 0, 5–20, 25–30…; fractions.</summary>
    public static readonly IReadOnlyList<string> UkrainianPluralForms = ["one", "few", "many", "other"];

    /// <summary>What ends a whole sentence in a template.</summary>
    private const string TerminalPunctuation = ".?!…";

    // ── the port of parity.ts ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every way <paramref name="translated"/> departs from <paramref name="source"/>, one sentence each, prefixed by the
    /// key; empty when it holds. The rules (parity.ts):
    /// the keys are exactly the source's, except that each source plural base becomes exactly the translation's forms;
    /// no value is empty, and none is anything but a string;
    /// a value starts and ends with the same whitespace as the source's;
    /// a value has the source's placeholders; each plural form uses only placeholders of the source's forms, and the
    /// forms together use all of them;
    /// a value has the source's <c>&lt;Trans&gt;</c> tags;
    /// no “…” quotes, and no ASCII apostrophe or "…" quotes unless the same word is in the source.
    /// </summary>
    public static IReadOnlyList<string> Problems(JsonElement source, JsonElement translated, IReadOnlyList<string> pluralForms)
    {
        ArgumentNullException.ThrowIfNull(pluralForms);
        var problems = new List<string>();
        var sourceLeaves = LeavesOf(source);
        var translatedLeaves = LeavesOf(translated);

        // The source's keys, with each plural base's forms gathered under the base.
        var plain = new List<(string Key, string Text)>();
        var plurals = new List<(string Base, List<(string Form, string Text)> Forms)>();
        foreach (var (key, value) in sourceLeaves)
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (PluralSuffix().Match(key) is { Success: true } plural)
            {
                var baseKey = key[..plural.Index];
                var forms = plurals.FirstOrDefault(entry => entry.Base == baseKey).Forms;
                if (forms is null)
                    plurals.Add((baseKey, forms = []));
                forms.Add((plural.Groups[1].Value, text));
            }
            else
            {
                plain.Add((key, text));
            }
        }

        var expected = new List<string>();
        foreach (var (key, _) in plain)
            AddOnce(expected, key);
        foreach (var (baseKey, _) in plurals)
        {
            foreach (var form in pluralForms)
                AddOnce(expected, $"{baseKey}_{form}");
        }

        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        foreach (var key in expected)
        {
            if (!translatedLeaves.ContainsKey(key))
                problems.Add($"{key}: missing");
        }

        foreach (var key in translatedLeaves.Keys)
        {
            if (!expectedSet.Contains(key))
                problems.Add($"{key}: not in the source");
        }

        foreach (var (key, sourceText) in plain)
        {
            if (!translatedLeaves.TryGetValue(key, out var translatedValue))
                continue;
            var value = CheckText(problems, key, translatedValue, [sourceText], sourceText);
            if (value is null)
                continue;
            var want = PlaceholdersOf(sourceText);
            var have = PlaceholdersOf(value);
            if (Show(want) != Show(have))
                problems.Add($"{key}: placeholders {Show(have)}, the source has {Show(want)}");
        }

        foreach (var (baseKey, sourceForms) in plurals)
        {
            var sourceTexts = sourceForms.Select(form => form.Text).ToList();
            // The source's `_other` is the reference for whitespace and tags.
            var reference = sourceForms.FirstOrDefault(form => form.Form == "other").Text ?? sourceTexts[^1];
            var allowed = sourceTexts.SelectMany(PlaceholdersOf).ToHashSet(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var complete = true;
            foreach (var form in pluralForms)
            {
                var key = $"{baseKey}_{form}";
                if (!translatedLeaves.TryGetValue(key, out var translatedValue))
                {
                    complete = false;
                    continue;
                }

                var value = CheckText(problems, key, translatedValue, sourceTexts, reference);
                if (value is null)
                {
                    complete = false;
                    continue;
                }

                var have = PlaceholdersOf(value);
                var unknown = have.Where(name => !allowed.Contains(name)).ToList();
                if (unknown.Count > 0)
                    problems.Add($"{key}: placeholders {Show(unknown)} are in none of the source's forms");
                used.UnionWith(have);
            }

            var unused = allowed.Where(name => !used.Contains(name)).ToList();
            if (complete && unused.Count > 0)
                problems.Add($"{baseKey}: no form uses {Show(unused)}");
        }

        return problems;
    }

    /// <summary>Every leaf of a resource tree under <paramref name="prefix"/>, by its dotted key, in the file's order —
    /// parity.ts' <c>leavesOf</c>.</summary>
    public static void Flatten(JsonElement element, string prefix, List<(string Key, JsonElement Value)> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                Flatten(property.Value, prefix.Length > 0 ? $"{prefix}.{property.Name}" : property.Name, into);
        }
        else
        {
            into.Add((prefix, element));
        }
    }

    private static Dictionary<string, JsonElement> LeavesOf(JsonElement tree)
    {
        var leaves = new List<(string Key, JsonElement Value)>();
        Flatten(tree, "", leaves);
        // A key written twice keeps its first place and its last value, as a JavaScript Map does.
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in leaves)
            map[key] = value;
        return map;
    }

    private static string? CheckText(List<string> problems, string key, JsonElement element, IReadOnlyList<string> sourceTexts, string reference)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{key}: not a string");
            return null;
        }

        var value = element.GetString()!;
        if (value.Trim().Length == 0)
        {
            problems.Add($"{key}: empty");
            return null;
        }

        if (LeadingSpace().Match(value).Value != LeadingSpace().Match(reference).Value
            || TrailingSpace().Match(value).Value != TrailingSpace().Match(reference).Value)
            problems.Add($"{key}: starts or ends with other whitespace than the source");

        if (string.Join(',', TagsOf(value)) != string.Join(',', TagsOf(reference)))
            problems.Add($"{key}: tags {Show(TagsOf(value))}, the source has {Show(TagsOf(reference))}");

        var sourceWords = sourceTexts.SelectMany(text => Whitespace().Split(text)).ToHashSet(StringComparer.Ordinal);
        var words = Whitespace().Split(value);
        var englishQuotes = words.Where(word => EnglishQuotes().IsMatch(word)).ToList();
        if (englishQuotes.Count > 0)
            problems.Add($"{key}: “…” quotes in {string.Join(' ', englishQuotes)}");
        var asciiQuotes = words.Where(word => AsciiQuotes().IsMatch(word) && !sourceWords.Contains(word)).ToList();
        if (asciiQuotes.Count > 0)
            problems.Add($"{key}: an ASCII apostrophe or quote in {string.Join(' ', asciiQuotes)}");
        return value;
    }

    /// <summary>The placeholders of a value, each as written without spaces (<c>names,list</c>).</summary>
    private static HashSet<string> PlaceholdersOf(string value) =>
        Placeholder().Matches(value).Select(match => Whitespace().Replace(match.Groups[1].Value, "")).ToHashSet(StringComparer.Ordinal);

    /// <summary>The <c>&lt;Trans&gt;</c> tags of a value, sorted, so two values compare as multisets.</summary>
    private static List<string> TagsOf(string value) =>
        [.. TransTag().Matches(value).Select(match => Whitespace().Replace(match.Value, "")).Order(StringComparer.Ordinal)];

    private static string Show(IEnumerable<string> set) => $"[{string.Join(", ", set.Order(StringComparer.Ordinal))}]";

    private static void AddOnce(List<string> list, string key)
    {
        if (!list.Contains(key, StringComparer.Ordinal))
            list.Add(key);
    }

    // ── the server's rules (design §2.2, §2.4) ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The server's rules over one language's templates (<paramref name="leaves"/>, keys with their file's name):
    /// every template parses; a <c>count</c> placeholder is a number; and for English (<paramref name="isEnglish"/>) every
    /// plural base has <c>_one</c> and an <c>_other</c> that writes the count, no two placeholders touch (the startup
    /// converter needs words between them) unless <paramref name="mayTouch"/> lists the key, and a whole sentence ends
    /// with terminal punctuation or a nested sentence unless <paramref name="isClause"/> says it is a clause.
    /// </summary>
    public static IReadOnlyList<string> ServerProblems(
        IReadOnlyList<(string Key, JsonElement Value)> leaves,
        bool isEnglish,
        Func<string, bool> mayTouch,
        Func<string, bool> isClause)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(mayTouch);
        ArgumentNullException.ThrowIfNull(isClause);
        var problems = new List<string>();
        var templates = new List<(string Key, MessageTemplate Template)>();
        foreach (var (key, value) in leaves)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                problems.Add($"{key}: not a string");
                continue;
            }

            try
            {
                templates.Add((key, MessageTemplate.Parse(value.GetString()!)));
            }
            catch (FormatException exception)
            {
                problems.Add($"{key}: doesn't parse ({exception.Message})");
            }
        }

        foreach (var (key, template) in templates)
        {
            foreach (var slot in template.Slots.Where(slot => slot.Name == MessageRenderer.CountName))
            {
                if (slot.Hint.Kind is not (SlotKind.Number or SlotKind.GroupedNumber or SlotKind.WordNumber))
                    problems.Add($"{key}: {{{{count}}}} has the hint \"{slot.Hint}\"; a count is a number, number:grouped or number:word");
            }
        }

        if (!isEnglish)
            return problems;

        foreach (var group in templates.GroupBy(entry => TemplateCatalog.BaseKeyOf(entry.Key)).Where(group => group.Any(entry => entry.Key != group.Key)))
        {
            var other = group.FirstOrDefault(entry => entry.Key == group.Key + "_other");
            if (!group.Any(entry => entry.Key == group.Key + "_one"))
                problems.Add($"{group.Key}: no _one form; English writes every counted noun with _one and _other");
            if (other.Template is null)
                problems.Add($"{group.Key}: no _other form; English writes every counted noun with _one and _other");
            else if (!other.Template.Slots.Any(slot => slot.Name == MessageRenderer.CountName))
                problems.Add($"{other.Key}: doesn't write {{{{count}}}}");
        }

        foreach (var (key, template) in templates)
        {
            var baseKey = TemplateCatalog.BaseKeyOf(key);
            for (var index = 1; index < template.Parts.Count; index++)
            {
                if (template.Parts[index - 1] is TemplateSlot first && template.Parts[index] is TemplateSlot second && !mayTouch(baseKey))
                    problems.Add($"{key}: {{{{{first.Name}}}}} and {{{{{second.Name}}}}} touch; the startup converter needs words between two placeholders");
            }

            if (!isClause(baseKey) && !EndsAsASentence(template))
                problems.Add($"{key}: a whole sentence ends with . ? ! … or a nested sentence; a clause's key ends with -clause or sits under .tally.");
        }

        return problems;
    }

    /// <summary>
    /// Ukrainian against English, beyond the port: each Ukrainian value ends with terminal punctuation exactly when the
    /// English it translates does (a plural form against the English <c>_other</c>).
    /// </summary>
    public static IReadOnlyList<string> PunctuationProblems(
        IReadOnlyList<(string Key, JsonElement Value)> english, IReadOnlyList<(string Key, JsonElement Value)> ukrainian)
    {
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(ukrainian);
        var englishTexts = english
            .Where(leaf => leaf.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(leaf => leaf.Key, leaf => leaf.Value.GetString()!, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (key, value) in ukrainian)
        {
            if (value.ValueKind != JsonValueKind.String)
                continue;
            var baseKey = TemplateCatalog.BaseKeyOf(key);
            if (!englishTexts.TryGetValue(key, out var source) && !englishTexts.TryGetValue(baseKey + "_other", out source))
                continue;
            if (EndsWithTerminalPunctuation(source) != EndsWithTerminalPunctuation(value.GetString()!))
                problems.Add($"{key}: ends with terminal punctuation where the English doesn't, or the other way round");
        }

        return problems;
    }

    /// <summary>
    /// <c>older-en.json</c> — current key → retired English wordings the startup converter still recognises — against the
    /// English templates: every key is an English key, every wording parses, and each placeholder it writes is one of the
    /// key's with the same hint.
    /// </summary>
    public static IReadOnlyList<string> OlderWordingProblems(JsonElement older, IReadOnlyList<(string Key, JsonElement Value)> english)
    {
        ArgumentNullException.ThrowIfNull(english);
        var problems = new List<string>();
        if (older.ValueKind != JsonValueKind.Object)
            return ["older-en.json: not an object of key → wordings"];

        var slotsByBaseKey = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (key, value) in english)
        {
            if (value.ValueKind != JsonValueKind.String)
                continue;
            var baseKey = TemplateCatalog.BaseKeyOf(key);
            if (!slotsByBaseKey.TryGetValue(baseKey, out var slots))
                slotsByBaseKey[baseKey] = slots = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var slot in MessageTemplate.Parse(value.GetString()!).Slots)
                    slots[slot.Name] = slot.Hint.ToString();
            }
            catch (FormatException)
            {
                // ServerProblems reports it.
            }
        }

        foreach (var entry in older.EnumerateObject())
        {
            if (!slotsByBaseKey.TryGetValue(entry.Name, out var keySlots))
            {
                problems.Add($"older-en {entry.Name}: no such English key");
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"older-en {entry.Name}: not an array of wordings");
                continue;
            }

            var index = 0;
            foreach (var wording in entry.Value.EnumerateArray())
            {
                var where = $"older-en {entry.Name}[{index++}]";
                if (wording.ValueKind != JsonValueKind.String)
                {
                    problems.Add($"{where}: not a string");
                    continue;
                }

                MessageTemplate template;
                try
                {
                    template = MessageTemplate.Parse(wording.GetString()!);
                }
                catch (FormatException exception)
                {
                    problems.Add($"{where}: doesn't parse ({exception.Message})");
                    continue;
                }

                foreach (var slot in template.Slots)
                {
                    if (!keySlots.TryGetValue(slot.Name, out var hint))
                        problems.Add($"{where}: {{{{{slot.Name}}}}} isn't a placeholder of the key");
                    else if (hint != slot.Hint.ToString())
                        problems.Add($"{where}: {{{{{slot.Name}}}}} has the hint \"{slot.Hint}\", the key's has \"{hint}\"");
                }
            }
        }

        return problems;
    }

    private static bool EndsAsASentence(MessageTemplate template) => template.Parts.Count > 0 && (template.Parts[^1] switch
    {
        TemplateLiteral literal => EndsWithTerminalPunctuation(literal.Text),
        TemplateSlot slot => slot.Hint.Kind is SlotKind.Message or SlotKind.MessageList,
        _ => false,
    });

    private static bool EndsWithTerminalPunctuation(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && TerminalPunctuation.Contains(trimmed[^1], StringComparison.Ordinal);
    }

    /// <summary>The plural suffixes i18next picks between (CLDR categories); English uses <c>_one</c> and <c>_other</c>.</summary>
    [GeneratedRegex("_(zero|one|two|few|many|other)$")]
    private static partial Regex PluralSuffix();

    /// <summary><c>{{name}}</c> or <c>{{name, format}}</c>; the content, without spaces, names the placeholder.</summary>
    [GeneratedRegex(@"\{\{([^}]*)\}\}")]
    private static partial Regex Placeholder();

    /// <summary>A <c>&lt;Trans&gt;</c> tag: <c>&lt;strong&gt;</c>, <c>&lt;/strong&gt;</c>, <c>&lt;name/&gt;</c>, <c>&lt;0&gt;</c>.</summary>
    [GeneratedRegex(@"</?[A-Za-z0-9]+\s*/?>")]
    private static partial Regex TransTag();

    /// <summary>English typographic quotes: the glossary's quotes are «…» and „…“, so these never belong.</summary>
    [GeneratedRegex("[“”]")]
    private static partial Regex EnglishQuotes();

    /// <summary>ASCII apostrophe and quote: the glossary's apostrophe is ’, but a command shown as it is keeps them.</summary>
    [GeneratedRegex("['\"]")]
    private static partial Regex AsciiQuotes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s*")]
    private static partial Regex LeadingSpace();

    [GeneratedRegex(@"\s*\z")]
    private static partial Regex TrailingSpace();
}
