using System.Text.Json;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042, design §6.8) — the startup converter's parser is the renderer's inverse: every English wording,
// written with sample values, reads back as its own key and the very values. That proves each wording parses — so the
// converter can read the rows an older build wrote in it — and that no two wordings collide: one wording = one key,
// unless round-trip-allowlist.json names them and why. The sweep iterates the catalogue, so each package's new wordings
// are checked by the next full run with no line here.
public sealed class MessageRoundTripTests
{
    private const string Ellipsis = "…";

    private static readonly Lazy<RoundTrip> EmbeddedRoundTrip = new(() =>
    {
        var older = OlderWordings(LocaleFiles.Embedded.TryGetValue("older-en.json", out var json) ? json : "{}");
        return RoundTrip.Of(
            TemplateCatalog.Embedded, MessageParser.Embedded, EnglishWordings(TemplateCatalog.Embedded), older, ReadAllowlist(File.ReadAllText(AllowlistFile)));
    });

    private static readonly TemplateCatalog Sample = TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/sample.json", EnglishSample));

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

    // A wording of every kind of value, in the shapes the server's sentences take.
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
          "done": "It is {{percent, number}}% done.",
          "failed": "The move failed: {{reason, message}}",
          "nothing-sent": "{{reason, message:clause}}, so nothing was sent.",
          "names": "Missing: {{names, list:and}}.",
          "either": "Pick {{names, list:or:quoted}}.",
          "ids": "Ids: {{ids, list:comma}}.",
          "why": "Why: {{reasons, list:space:message}}",
          "twice": "{{name}} and {{name}} again.",
          "no-new-file": "No new file is {{work, name}}.",
          "fits": "It fits {{works, list:or:name}}.",
          "tally": {
            "in-quarantine_one": "{{count, number}} of {{names, message}} is in the quarantine",
            "in-quarantine_other": "{{count, number}} of {{names, message}} are in the quarantine",
            "names_one": "{{count, number}} name",
            "names_other": "{{count, number}} names"
          },
          "not-moved": "“{{file}}” couldn't be moved into the quarantine: {{error, message}} {{tally, message}}."
        }
        """;

    private static string AllowlistFile =>
        Path.Combine(RepositoryPaths.Root, "tests", "MediaAcquisitionManager.Api.Tests", "Texts", "round-trip-allowlist.json");

    // ── the server's own wordings ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_english_wording_reads_back_as_its_key_and_values()
    {
        //Arrange
        var roundTrip = EmbeddedRoundTrip.Value;

        //Act
        var problems = roundTrip.EnglishProblems;

        //Assert
        // The guard of the guard: the sweep saw the engine's own wordings at least.
        Assert.True(roundTrip.EnglishCount >= 12, $"Only {roundTrip.EnglishCount} English wordings were swept.");
        Assert.True(
            problems.Count == 0,
            $"English wordings that don't read back as their key and values — give each wording one key and words between its values, or name a deliberate pair in round-trip-allowlist.json with why:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    public void Every_older_wording_reads_back_as_its_key_and_values()
    {
        //Arrange
        var roundTrip = EmbeddedRoundTrip.Value;

        //Act
        var problems = roundTrip.OlderProblems;

        //Assert
        Assert.True(
            problems.Count == 0,
            $"Older wordings (older-en.json) that don't read back as their key — each must carry every value today's templates of the key need, a counted key's its count:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    public void Every_allowlist_entry_names_english_keys_with_a_reason_and_is_still_needed()
    {
        //Arrange
        var roundTrip = EmbeddedRoundTrip.Value;
        var english = TemplateCatalog.Embedded.Keys(TextLanguage.English).Select(TemplateCatalog.BaseKeyOf).ToHashSet(StringComparer.Ordinal);

        //Act
        var problems = AllowlistProblems(roundTrip, english);

        //Assert
        Assert.True(problems.Count == 0, $"round-trip-allowlist.json:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // ── each kind of value, over a catalogue of every shape ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sample.stopped")]
    [InlineData("sample.no-file")]
    [InlineData("sample.said")]
    [InlineData("sample.moved_one")]
    [InlineData("sample.moved_other")]
    [InlineData("sample.left_one")]
    [InlineData("sample.left_other")]
    [InlineData("sample.bytes")]
    [InlineData("sample.new-files_one")]
    [InlineData("sample.new-files_other")]
    [InlineData("sample.limit")]
    [InlineData("sample.runs")]
    [InlineData("sample.done")]
    [InlineData("sample.failed")]
    [InlineData("sample.nothing-sent")]
    [InlineData("sample.names")]
    [InlineData("sample.either")]
    [InlineData("sample.ids")]
    [InlineData("sample.why")]
    [InlineData("sample.twice")]
    [InlineData("sample.no-new-file")]
    [InlineData("sample.fits")]
    [InlineData("sample.tally.in-quarantine_one")]
    [InlineData("sample.tally.in-quarantine_other")]
    [InlineData("sample.tally.names_one")]
    [InlineData("sample.tally.names_other")]
    [InlineData("sample.not-moved")]
    [InlineData("common.quoted")]
    [InlineData("common.list.and")]
    [InlineData("common.size.mib_other")]
    [InlineData("common.size.bytes_one")]
    [InlineData("common.size.bytes_other")]
    [InlineData("common.message-unavailable")]
    public void Each_kind_of_value_reads_back_as_it_was_written(string key)
    {
        //Arrange
        Sample.TryFind(TextLanguage.English, key, out var template);
        var message = SampleMessage(key, template!, NestedSample(Sample));
        var written = MessageRenderer.RenderWith(Sample, message, candidate => ReferenceEquals(candidate, message) ? template : null);
        var parser = new MessageParser(Sample, "{}");

        //Act
        var candidates = parser.Parse(written, written.Length + 1, keyPrefix: "");

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(message, candidate.Message);
        Assert.False(candidate.CutValue);
    }

    [Fact]
    public void Every_sample_wording_reads_back_as_its_key_and_values()
    {
        //Arrange
        var parser = new MessageParser(Sample, "{}");

        //Act
        var roundTrip = RoundTrip.Of(Sample, parser, EnglishWordings(Sample), [], []);

        //Assert
        Assert.Empty(roundTrip.EnglishProblems);
        Assert.Equal(EnglishWordings(Sample).Count, roundTrip.EnglishCount);
    }

    // ── the round trip finds what breaks it ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_round_trip_names_both_keys_of_one_wording()
    {
        //Arrange
        var catalog = WithSample("""{ "gone": "The copy is gone.", "lost": "The copy is gone." }""");

        //Act
        var roundTrip = RoundTrip.Of(catalog, new MessageParser(catalog, "{}"), EnglishWordings(catalog), [], []);

        //Assert
        Assert.Equal(2, roundTrip.EnglishProblems.Count);
        Assert.StartsWith("x.gone: “The copy is gone.” reads back as ", roundTrip.EnglishProblems[0]);
        Assert.StartsWith("x.lost: “The copy is gone.” reads back as ", roundTrip.EnglishProblems[1]);
    }

    [Fact]
    public void The_round_trip_spares_a_pair_the_allowlist_names()
    {
        //Arrange
        var catalog = WithSample("""{ "gone": "The copy is gone.", "lost": "The copy is gone." }""");
        var allowlist = ReadAllowlist("""[ { "keys": ["x.gone", "x.lost"], "preferred": "x.gone", "reason": "Ukrainian tells them apart." } ]""");

        //Act
        var roundTrip = RoundTrip.Of(catalog, new MessageParser(catalog, "{}"), EnglishWordings(catalog), [], allowlist);

        //Assert
        Assert.Empty(roundTrip.EnglishProblems);
        Assert.Equal([0], roundTrip.UsedEntries);
    }

    [Fact]
    public void The_round_trip_names_a_wording_whose_values_cant_be_told_apart()
    {
        //Arrange
        // "Alpha 1 Alpha 2" splits three ways: two names with one space between them can't be read back.
        var catalog = WithSample("""{ "pair": "{{first}} {{second}} were swapped." }""");

        //Act
        var roundTrip = RoundTrip.Of(catalog, new MessageParser(catalog, "{}"), EnglishWordings(catalog), [], []);

        //Assert
        var problem = Assert.Single(roundTrip.EnglishProblems);
        Assert.StartsWith("x.pair: “Alpha 1 Alpha 2 were swapped.” reads back as ", problem);
    }

    [Fact]
    public void The_round_trip_names_an_older_wording_without_a_value_todays_wording_needs()
    {
        //Arrange
        var catalog = WithSample("""{ "no-file": "There is no file at “{{path}}” in “{{folder}}”." }""");
        const string olderEnglish = """{ "x.no-file": ["No file at “{{path}}”."] }""";

        //Act
        var roundTrip = RoundTrip.Of(catalog, new MessageParser(catalog, olderEnglish), EnglishWordings(catalog), OlderWordings(olderEnglish), []);

        //Assert
        Assert.Empty(roundTrip.EnglishProblems);
        Assert.Equal(["x.no-file (older): “No file at “Alpha 1”.” reads back as nothing"], roundTrip.OlderProblems);
    }

    [Fact]
    public void The_allowlist_check_names_an_entry_no_longer_needed_and_one_that_breaks_the_rules()
    {
        //Arrange
        var catalog = WithSample("""{ "gone": "The copy is gone.", "lost": "The copy went." }""");
        var allowlist = ReadAllowlist(
            """
            [
              { "keys": ["x.gone", "x.lost"], "preferred": "x.gone", "reason": "Once the same words." },
              { "keys": ["x.gone", "x.missing"], "reason": "" }
            ]
            """);
        var roundTrip = RoundTrip.Of(catalog, new MessageParser(catalog, "{}"), EnglishWordings(catalog), [], allowlist);
        var english = catalog.Keys(TextLanguage.English).Select(TemplateCatalog.BaseKeyOf).ToHashSet(StringComparer.Ordinal);

        //Act
        var problems = AllowlistProblems(roundTrip, english);

        //Assert
        Assert.Equal(
            [
                "entry 0 (x.gone, x.lost): no longer needed — every key of it reads back alone",
                "entry 1 (x.gone, x.missing): x.missing is no English key",
                "entry 1 (x.gone, x.missing): has no reason",
                "entry 1 (x.gone, x.missing): two or more keys need a preferred one among them",
                "entry 1 (x.gone, x.missing): no longer needed — every key of it reads back alone",
            ],
            problems);
    }

    // ── the sweep ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A message of <paramref name="key"/> (a plural form's, or an older wording's base key) with a value for
    /// every placeholder of <paramref name="template"/>, each told apart from the others.</summary>
    private static Message SampleMessage(string key, MessageTemplate template, Message nested)
    {
        var values = new Dictionary<string, MessageValue>(StringComparer.Ordinal);
        var index = 0;
        foreach (var slot in template.Slots)
        {
            if (values.ContainsKey(slot.Name))
                continue;
            index++;
            values[slot.Name] = slot.Name == MessageRenderer.CountName ? CountOf(key) : slot.Hint.Kind switch
            {
                // A name reads back as text (MessageParser): its label is a message of its own, written where a name goes.
                SlotKind.Text or SlotKind.Name => new TextValue($"Alpha {index}"),
                SlotKind.Vendor => VendorText.Clean($"Busy (errno {15 + index})", 100),
                SlotKind.Number => new NumberValue(6 + index),
                SlotKind.GroupedNumber => new NumberValue(1_234_567),
                SlotKind.WordNumber => new NumberValue(3),
                SlotKind.Size => new SizeValue(8 * 1024 * 1024),
                SlotKind.Duration => DurationValue.From(new TimeSpan(1, 56, 30)),
                SlotKind.Message or SlotKind.Clause => nested,
                SlotKind.MessageList => new ListValue([nested, nested]),
                _ => new ListValue([new TextValue($"Item{index}a"), new TextValue($"Item{index}b")]),
            };
        }

        // A counted key's form that doesn't write its count ("One minute is left.") carries it all the same.
        var baseKey = TemplateCatalog.BaseKeyOf(key);
        if (baseKey != key && !values.ContainsKey(MessageRenderer.CountName))
            values[MessageRenderer.CountName] = CountOf(key);
        return Message.Of(baseKey, [.. values.Select(value => (value.Key, value.Value))]);
    }

    /// <summary>The count that picks <paramref name="key"/>'s form: 1 for <c>_one</c>, 7 otherwise.</summary>
    private static NumberValue CountOf(string key) => new(key.EndsWith("_one", StringComparison.Ordinal) ? 1 : 7);

    /// <summary>A whole sentence of no values to nest: the first such English key, else words of an older build.</summary>
    private static Message NestedSample(TemplateCatalog catalog)
    {
        foreach (var key in catalog.Keys(TextLanguage.English).Order(StringComparer.Ordinal))
        {
            if (TemplateCatalog.BaseKeyOf(key) == key && key != Message.LegacyKey && catalog.TryFind(TextLanguage.English, key, out var template)
                && template.Slots.Count == 0 && template.Source.TrimEnd() is { Length: > 0 } words && ".?!".Contains(words[^1], StringComparison.Ordinal))
                return Message.Of(key);
        }

        return Message.Legacy("Gamma went wrong.");
    }

    private static List<(string Key, MessageTemplate Template)> EnglishWordings(TemplateCatalog catalog) =>
    [
        .. catalog.Keys(TextLanguage.English)
            .Where(key => TemplateCatalog.BaseKeyOf(key) != Message.LegacyKey)
            .Order(StringComparer.Ordinal)
            .Select(key => catalog.TryFind(TextLanguage.English, key, out var template) ? (key, template) : throw new InvalidOperationException(key)),
    ];

    private static List<(string Key, MessageTemplate Template)> OlderWordings(string olderEnglish)
    {
        using var document = JsonDocument.Parse(olderEnglish);
        return
        [
            .. document.RootElement.EnumerateObject()
                .SelectMany(entry => entry.Value.EnumerateArray().Select(wording => (entry.Name, MessageTemplate.Parse(wording.GetString()!)))),
        ];
    }

    private static List<AllowedWords> ReadAllowlist(string json)
    {
        using var document = JsonDocument.Parse(json);
        return
        [
            .. document.RootElement.EnumerateArray().Select(entry => new AllowedWords(
                [.. entry.GetProperty("keys").EnumerateArray().Select(key => key.GetString()!)],
                entry.TryGetProperty("preferred", out var preferred) ? preferred.GetString() : null,
                entry.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "")),
        ];
    }

    private static List<string> AllowlistProblems(RoundTrip roundTrip, HashSet<string> english)
    {
        var problems = new List<string>();
        foreach (var (entry, index) in roundTrip.Allowlist.Select((entry, index) => (entry, index)))
        {
            var where = $"entry {index} ({string.Join(", ", entry.Keys)})";
            problems.AddRange(entry.Keys.Where(key => !english.Contains(key)).Select(key => $"{where}: {key} is no English key"));
            if (string.IsNullOrWhiteSpace(entry.Reason))
                problems.Add($"{where}: has no reason");
            if (entry.Keys.Count >= 2 && (entry.Preferred is null || !entry.Keys.Contains(entry.Preferred)))
                problems.Add($"{where}: two or more keys need a preferred one among them");
            if (!roundTrip.UsedEntries.Contains(index))
                problems.Add($"{where}: no longer needed — every key of it reads back alone");
        }

        return problems;
    }

    private static TemplateCatalog WithSample(string json) => TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/x.json", json));

    /// <summary>A deliberate pair of keys with the same English words (a single key: a wording whose values can't be
    /// told apart), the one new code uses, and why.</summary>
    private sealed record AllowedWords(IReadOnlyList<string> Keys, string? Preferred, string Reason);

    /// <summary>What one sweep found: each wording's problem, and the allowlist entries that spared one.</summary>
    private sealed class RoundTrip
    {
        private RoundTrip(IReadOnlyList<AllowedWords> allowlist) => Allowlist = allowlist;

        public IReadOnlyList<AllowedWords> Allowlist { get; }

        public List<string> EnglishProblems { get; } = [];

        public List<string> OlderProblems { get; } = [];

        public SortedSet<int> UsedEntries { get; } = [];

        public int EnglishCount { get; private set; }

        public static RoundTrip Of(
            TemplateCatalog catalog,
            MessageParser parser,
            IReadOnlyList<(string Key, MessageTemplate Template)> english,
            IReadOnlyList<(string Key, MessageTemplate Template)> older,
            IReadOnlyList<AllowedWords> allowlist)
        {
            var roundTrip = new RoundTrip(allowlist);
            var nested = NestedSample(catalog);
            foreach (var (key, template) in english)
            {
                roundTrip.EnglishCount++;
                roundTrip.Check(catalog, parser, key, key, template, nested, roundTrip.EnglishProblems);
            }

            foreach (var (key, template) in older)
                roundTrip.Check(catalog, parser, key, $"{key} (older)", template, nested, roundTrip.OlderProblems);
            return roundTrip;
        }

        private void Check(
            TemplateCatalog catalog, MessageParser parser, string key, string name, MessageTemplate template, Message nested, List<string> problems)
        {
            var message = SampleMessage(key, template, nested);
            string written;
            try
            {
                written = MessageRenderer.RenderWith(catalog, message, candidate => ReferenceEquals(candidate, message) ? template : null);
            }
            catch (MissingTextException exception)
            {
                problems.Add($"{name}: can't be written with sample values ({exception.Message})");
                return;
            }

            // Any key may read it back (keyPrefix ""), fragments too: a wording collides with every other, nested or not.
            var candidates = parser.Parse(written, written.Length + Ellipsis.Length, keyPrefix: "");
            if (candidates is [var only] && only.Message.Equals(message))
                return;

            var allowed = Allowlist
                .Select((entry, index) => (entry, index))
                .Where(pair => pair.entry.Keys.Contains(message.Key)
                    && candidates.Any(candidate => candidate.Message.Equals(message))
                    && candidates.All(candidate => pair.entry.Keys.Contains(candidate.Message.Key)))
                .Select(pair => (int?)pair.index)
                .FirstOrDefault();
            if (allowed is { } entry)
            {
                UsedEntries.Add(entry);
                return;
            }

            var read = candidates.Count == 0 ? "nothing" : string.Join(" or ", candidates.Select(candidate => MessageJson.Write(candidate.Message)));
            problems.Add($"{name}: “{written}” reads back as {read}");
        }
    }
}
