using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>
/// MAM-47 (ADR 0042, design §6.3–§6.4) — reads a sentence an older build stored in English back into the key and values
/// it was written from, for the startup converter. The matchers are the renderer's own catalogue (Flyweight): every
/// English template — each plural form on its own — and the retired wordings of <c>Locales/older-en.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// A text is matched by backtracking recursive descent over a template's words and placeholders: a placeholder ends at
/// each place the words after it occur, nearest first, and the piece between must read as the placeholder's hint — a
/// number, a size, a running time, a list, a nested sentence — so a name holding <c>”</c>, <c>: </c> or <c>. </c> still
/// finds its end. A regex can't: it neither backtracks across nested sentences nor gives sizes and running times back as
/// values.
/// </para>
/// <para>
/// A reading counts only if writing it back with the very wordings that matched — an older one, or the plural form that
/// matched where the count would pick another ("1 files") — gives the stored text byte for byte, and if today's templates
/// can write it too; so an English reader sees exactly the words that were stored, and a converted row never turns into
/// a sentence that can't be shown. Among the readings that count, those whose wordings matched the most characters of
/// their own words win (the most specific: "…failed to hand over the release (…)." over "…failed (…)."); at the same
/// count, one that reads the text with today's wordings alone wins over one that needs an older build's words nested in
/// it, the last resort; two of them that still differ — in key or in values — are an ambiguous text, which the converter
/// keeps as its words and reports.
/// </para>
/// <para>
/// A text exactly as long as its column that ends with <c>…</c> may have been cut by today's helpers (the first
/// <c>maximumLength − 1</c> characters and <c>…</c>). Such a text may also be read with its last value — the last
/// placeholder on the right spine, recursively into a nested sentence — holding the rest of the text, <c>…</c>
/// included; the words after it aren't required, and writing it back must start with the stored text. A cut that falls
/// in the template's words, a number, a running time or a list is no reading.
/// </para>
/// </remarks>
public sealed partial class MessageParser
{
    /// <summary>What today's cut helpers end a cut text with, and what a value taking the cut keeps.</summary>
    private const string Ellipsis = "…";

    /// <summary>What ends a whole sentence — the locale files' own rule (LocaleParityTests).</summary>
    private const string TerminalPunctuation = ".?!…";

    /// <summary>The full stop a clause's sentence ends with and the clause leaves out (<c>message:clause</c>).</summary>
    private const string FullStop = ".";

    private const string Space = " ";

    private const string OlderWordingsResource = "Locales/older-en.json";

    private const string QuotedKey = "common.quoted";
    private const string QuotedTextName = "text";
    private const string ListSeparatorKey = "common.list.separator";
    private const string ListAndKey = "common.list.and";
    private const string ListOrKey = "common.list.or";

    /// <summary>
    /// The labels a <c>name</c> slot holds for a work with no title (<c>ReplacementPairingWork.NameOf</c>): the only
    /// messages a name stands for, so the only ones a name is read back as.
    /// </summary>
    private const string NameLabelPrefix = "replacements.pairing.work.";

    /// <summary>
    /// The steps one text may take before the parser gives up and the converter keeps the text as its words. A real
    /// sentence takes a few hundred to a few thousand; this many is about a third of a second for a 1000-character text
    /// and a second for the longest one read, so a pathological row can't hold up the start.
    /// </summary>
    /// <remarks>MAM-47 — a step is a unit of work of about the same cost wherever it is spent: a template part matched, a
    /// place a value may end tried, <see cref="UnitsPerStep"/> wordings scanned or nested wordings carried along, as many
    /// characters written per message to verify a reading. Counting only the parts matched let the uncounted work grow
    /// with the text: a 1000-character text of short sentences took 50 to 100 seconds inside the budget (WP4 review).
    /// </remarks>
    private const int StepBudget = 250_000;

    /// <summary>How many of the small units of work — a wording scanned for a piece, a nested wording carried into a
    /// frame, a character written back per message of a reading — make one step.</summary>
    private const int UnitsPerStep = 16;

    /// <summary>
    /// The most readings of one piece of the text — a nested sentence, a list — kept apart when they match as many words.
    /// Two already make the text around them ambiguous; past this many, more that match as many words are left out, and
    /// one that matches more still replaces them all.
    /// </summary>
    /// <remarks>MAM-47 — a cap on the readings kept, not on the matches tried: a value ends at each later place the words
    /// after it occur, nearest first, and a value that reads as an older build's words matches at every one of them, so
    /// capping the matches (64 per wording) stopped a long sentence — a nested sentence with more than 64 spaces — before
    /// its real reading was reached. And a small cap: every reading kept of a piece is tried in every frame around it
    /// (WP4 review).</remarks>
    private const int MaximumReadings = 16;

    /// <summary>
    /// The longest text read: the columns keep 1000 characters (the audit name 200) and the writers cut to them, so a
    /// longer text is no sentence of the app's. Read no way, since the time a parse takes grows with the text's length.
    /// </summary>
    private const int MaximumTextLength = 4 * MessageJson.ColumnLength;

    /// <summary>
    /// The most sentences read one inside another: a piece deeper than this reads as no sentence (and so, inside a frame,
    /// as an older build's words). The server's deepest is a handful (a tally inside a quarantine sentence inside a
    /// replacement's reason).
    /// </summary>
    /// <remarks>MAM-47 — each level is a handful of nested calls on the stack, and a wording that ends with a nested
    /// sentence after a word ("{{name}} {{reason, message}}") nests once per word: a 4000-character text of short words
    /// overflowed the test host's stack, which no catch survives — at start it would stop the boot (WP4 review).</remarks>
    private const int MaximumNesting = 32;

    /// <summary>The most hours a running time is read with: more is no running time, and past about 256 million a
    /// <see cref="TimeSpan"/> — what the renderer writes it through — overflows.</summary>
    private const long MaximumDurationHours = 1_000_000;

    private static readonly Lazy<MessageParser> EmbeddedParser = new(LoadEmbedded);

    private readonly TemplateCatalog _catalog;
    private readonly Dictionary<char, Wording[]> _wordingsByFirstCharacter;
    private readonly Wording[] _wordingsOpeningWithAValue;
    private readonly Wording? _quoted;
    private readonly Wording[] _nameLabels;
    private readonly (Wording Wording, long BytesPerUnit)[] _sizes;
    private readonly Dictionary<string, int> _countWords;
    private readonly string? _listSeparator;
    private readonly string? _listAnd;
    private readonly string? _listOr;

    /// <summary>A parser over <paramref name="catalog"/>'s English templates and the older wordings
    /// <paramref name="olderEnglish"/> — <c>older-en.json</c>'s shape: a base key → its retired English templates.</summary>
    /// <exception cref="InvalidDataException">The older wordings aren't that shape, or one doesn't parse: a defect of the
    /// build, which LocaleParityTests refuses before it ships.</exception>
    internal MessageParser(TemplateCatalog catalog, string olderEnglish)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(olderEnglish);
        _catalog = catalog;

        var wordings = new List<Wording>();
        foreach (var key in catalog.Keys(TextLanguage.English).Order(StringComparer.Ordinal))
        {
            var baseKey = TemplateCatalog.BaseKeyOf(key);
            // The words an older build stored are a reading only as a nested sentence's last resort (NestedMessage): as a
            // matcher of its own, "{{text}}" would read any text at all.
            if (string.Equals(baseKey, Message.LegacyKey, StringComparison.Ordinal) || !catalog.TryFind(TextLanguage.English, key, out var template))
                continue;
            wordings.Add(new Wording(baseKey, template, CountIsOne(key, template)));
        }

        wordings.AddRange(OlderWordings(olderEnglish));

        _wordingsByFirstCharacter = wordings
            .Where(wording => wording.Opening.Length > 0)
            .GroupBy(wording => wording.Opening[0])
            .ToDictionary(group => group.Key, group => group.ToArray());
        _wordingsOpeningWithAValue = [.. wordings.Where(wording => wording.Opening.Length == 0)];

        _quoted = catalog.TryFind(TextLanguage.English, QuotedKey, out var quoted) ? new Wording(QuotedKey, quoted, countIsOne: false) : null;
        _nameLabels = [.. wordings.Where(wording => wording.Key.StartsWith(NameLabelPrefix, StringComparison.Ordinal))];
        // The unit keys come from the renderer's own rule, so the parser reads exactly the units it writes.
        long[] unitSizes = [1L << 20, 1L << 10, 1L];
        _sizes =
        [
            .. from bytesPerUnit in unitSizes
               let key = LanguageFormats.SizeUnit(bytesPerUnit).Key
               from wording in wordings
               where string.Equals(wording.Key, key, StringComparison.Ordinal)
               select (wording, bytesPerUnit),
        ];
        _countWords = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var count = 0; count < 100; count++)
        {
            var word = LanguageFormats.WordNumber(count, TextLanguage.English);
            if (!string.Equals(word, count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                _countWords[word] = count;
        }

        _listSeparator = WordsOf(catalog, ListSeparatorKey);
        _listAnd = WordsOf(catalog, ListAndKey);
        _listOr = WordsOf(catalog, ListOrKey);
    }

    /// <summary>The parser over the Api's embedded catalogue and its embedded <c>Locales/older-en.json</c>, built on first
    /// use.</summary>
    public static MessageParser Embedded => EmbeddedParser.Value;

    /// <summary>
    /// Every most specific verified reading of <paramref name="text"/>, distinct, in a stable order: none — the converter
    /// keeps the text as its words (so too a text longer than <see cref="MaximumTextLength"/>, and one whose parse spent
    /// <see cref="StepBudget"/>); one — converted; several — ambiguous, kept and reported (design §6.4). Never throws for
    /// any text.
    /// </summary>
    /// <param name="text">The stored text.</param>
    /// <param name="columnLength">The most characters its column keeps (1000; 200 for an audit row's name): a text that
    /// long ending with <c>…</c> may have been cut inside its last value, and is read that way too.</param>
    /// <param name="keyPrefix"><c>null</c>: only wordings that end as a whole sentence — with <c>.</c>, <c>?</c>,
    /// <c>!</c>, <c>…</c> or a nested sentence — can read the whole text, as every sentence a column holds does, so a
    /// service's words never read as a fragment such as "{{title}} ({{year}})". Otherwise only the keys starting with
    /// it, whatever their ending: <c>"replacements.confirm."</c> for the consent words, which end with no full stop;
    /// <c>""</c> for every key.</param>
    public IReadOnlyList<ParseCandidate> Parse(string text, int columnLength, string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Length > MaximumTextLength)
            return [];

        Func<Wording, bool> eligible = keyPrefix is null
            ? wording => wording.EndsAsASentence
            : wording => wording.Key.StartsWith(keyPrefix, StringComparison.Ordinal);
        var session = new Session(this);
        var readings = session.Messages(text, cut: null, eligible).Select(reading => (Reading: reading, Cut: false)).ToList();
        // A text as long as its column ending with "…" may have been cut: its cut readings compete with its whole ones.
        // A whole reading that only took the cut text as an older nested sentence's words matches fewer words of its
        // own than a cut reading that reads that nested sentence too; the same message read both ways is a whole one.
        if (text.Length == columnLength && text.EndsWith(Ellipsis, StringComparison.Ordinal))
            readings.AddRange(session.Messages(text, new CutTail("", Clause: false), eligible).Select(reading => (Reading: reading, Cut: true)));
        if (session.GaveUp || readings.Count == 0)
            return [];

        var top = readings.Max(reading => reading.Reading.Score);
        var fewestOlder = readings.Where(reading => reading.Reading.Score == top).Min(reading => OlderWordsIn(reading.Reading.Value));
        var candidates = new List<ParseCandidate>();
        foreach (var (reading, cut) in readings.Where(reading => reading.Reading.Score == top && OlderWordsIn(reading.Reading.Value) == fewestOlder))
        {
            if (!candidates.Any(candidate => candidate.Message.Equals(reading.Value)))
                candidates.Add(new ParseCandidate((Message)reading.Value, reading.Score, cut));
        }

        return [.. candidates.OrderBy(candidate => MessageJson.Write(candidate.Message), StringComparer.Ordinal)];
    }

    // ── what a text can match ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The wordings whose own first and last words <paramref name="text"/> has (the last ones only when it wasn't
    /// cut): a text is tried against a handful of templates, not every one; and how many wordings that took a look at.</summary>
    private (List<Wording> Wordings, int Scanned) CandidatesFor(string text, bool cut)
    {
        var opening = _wordingsByFirstCharacter.TryGetValue(text[0], out var some) ? some : [];
        var wordings = new List<Wording>();
        foreach (var wording in opening.Concat(_wordingsOpeningWithAValue))
        {
            if (text.StartsWith(wording.Opening, StringComparison.Ordinal) && (cut || text.EndsWith(wording.Closing, StringComparison.Ordinal)))
                wordings.Add(wording);
        }

        return (wordings, opening.Length + _wordingsOpeningWithAValue.Length);
    }

    /// <summary>
    /// Whether <paramref name="message"/>, written with the wording each message in it matched, gives
    /// <paramref name="text"/> (starts with it, for a cut text), and today's templates can write it too.
    /// </summary>
    private bool WritesBack(Message message, ImmutableList<KeyValuePair<Message, MessageTemplate>> matched, string text, bool cut)
    {
        var templates = new Dictionary<Message, MessageTemplate>(ReferenceEqualityComparer.Instance);
        foreach (var (written, template) in matched)
            templates.TryAdd(written, template);

        try
        {
            var written = MessageRenderer.RenderWith(_catalog, message, candidate => templates.GetValueOrDefault(candidate));
            var same = cut ? written.StartsWith(text, StringComparison.Ordinal) : string.Equals(written, text, StringComparison.Ordinal);
            if (!same)
                return false;

            // A row is shown with today's templates from now on: an older wording that lacks a value today's needs, or a
            // plural form today's other forms can't write, would make a row that can't be shown — kept as words instead.
            _ = MessageRenderer.Render(_catalog, message, TextLanguage.English, strict: true);
            return true;
        }
        catch (Exception exception) when (exception is MissingTextException or ArgumentException or OverflowException or FormatException)
        {
            // A value that fits no hint, or a number past what a format writes: not this reading. Never thrown on: the
            // converter runs at start, and one odd row must not stop the others.
            return false;
        }
    }

    // ── building the parser ─────────────────────────────────────────────────────────────────────────────────────────

    private static MessageParser LoadEmbedded()
    {
        var assembly = typeof(MessageParser).Assembly;
        var olderEnglish = "{}";
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            // The csproj names each file Locales/%(RecursiveDir)%(Filename)%(Extension); a Windows build keeps its "\".
            if (!string.Equals(resource.Replace('\\', '/'), OlderWordingsResource, StringComparison.Ordinal))
                continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            olderEnglish = reader.ReadToEnd();
        }

        return new MessageParser(TemplateCatalog.Embedded, olderEnglish);
    }

    private static List<Wording> OlderWordings(string olderEnglish)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(olderEnglish);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("older-en.json is not JSON.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("older-en.json is not an object of a key and its older wordings.");

            var wordings = new List<Wording>();
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!Message.IsKey(entry.Name) || entry.Value.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"older-en.json has {entry.Name}, which is not a key with an array of older wordings.");

                foreach (var wording in entry.Value.EnumerateArray())
                {
                    if (wording.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException($"older-en.json has a wording of {entry.Name} that is not a string.");
                    try
                    {
                        wordings.Add(new Wording(entry.Name, MessageTemplate.Parse(wording.GetString()!), countIsOne: false));
                    }
                    catch (FormatException exception)
                    {
                        throw new InvalidDataException($"older-en.json has a wording of {entry.Name} that doesn't parse: {exception.Message}", exception);
                    }
                }
            }

            return wordings;
        }
    }

    /// <summary>An English <c>_one</c> form that doesn't write its count ("One minute is left.") was written for a count
    /// of 1, which the message must carry so today's templates pick the same form.</summary>
    private static bool CountIsOne(string key, MessageTemplate template) =>
        key.EndsWith(PluralRules.SuffixOf(PluralCategory.One), StringComparison.Ordinal)
        && !template.Slots.Any(slot => string.Equals(slot.Name, MessageRenderer.CountName, StringComparison.Ordinal));

    /// <summary>The words of a template with no placeholder — a list's joining words.</summary>
    private static string? WordsOf(TemplateCatalog catalog, string key) =>
        catalog.TryFind(TextLanguage.English, key, out var template) && template.Slots.Count == 0
            ? string.Concat(template.Parts.OfType<TemplateLiteral>().Select(literal => literal.Text))
            : null;

    /// <summary>
    /// How many nested sentences a reading holds as an older build's words (no wording read them). Of two readings that
    /// match as many words of their own, the one with fewer wins.
    /// </summary>
    /// <remarks>MAM-47 (design §6.3: a nested sentence's words are its last resort) — "The Torznab feed can't hand over
    /// this release any more (410 Gone). Search again." reads as the status wording with "410 Gone" as its
    /// value, and as the error wording with those words as an unread nested sentence; both match the same words, and a
    /// tie keeps the row as words. Today's producer wrote the status wording, and an older nested sentence is a guess:
    /// the wording that reads the text with today's values alone is the reading (merge of WP4).</remarks>
    private static int OlderWordsIn(MessageValue value) => value switch
    {
        Message { Key: Message.LegacyKey } => 1,
        Message message => message.Values.Values.Sum(OlderWordsIn),
        ListValue list => list.Items.Sum(OlderWordsIn),
        _ => 0,
    };

    private static bool EndsAsASentence(MessageTemplate template) => template.Parts.Count > 0 && template.Parts[^1] switch
    {
        TemplateLiteral literal => literal.Text.TrimEnd() is { Length: > 0 } words && TerminalPunctuation.Contains(words[^1], StringComparison.Ordinal),
        TemplateSlot slot => slot.Hint.Kind is SlotKind.Message or SlotKind.MessageList,
        _ => false,
    };

    /// <summary>A number as the invariant culture writes it, every significant digit and no grouping.</summary>
    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumber();

    /// <summary>A number grouped by commas from 1,000 up, as <c>N0</c> wrote byte counts.</summary>
    [GeneratedRegex(@"^-?[0-9]{1,3}(,[0-9]{3})*(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupedNumber();

    /// <summary>The server's clock: hours unbounded, minutes and seconds in two digits.</summary>
    [GeneratedRegex(@"^(?<hours>[0-9]+):(?<minutes>[0-5][0-9]):(?<seconds>[0-5][0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex Clock();

    // ── the pieces ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One template a text can match — an English plural form or an older wording — with what the index and the
    /// top-level rule need.</summary>
    private sealed class Wording(string key, MessageTemplate template, bool countIsOne)
    {
        /// <summary>The key the reading is a message of: a base key, never a plural form's.</summary>
        public string Key { get; } = key;

        public MessageTemplate Template { get; } = template;

        /// <summary>An English <c>_one</c> form that doesn't write its count.</summary>
        public bool CountIsOne { get; } = countIsOne;

        /// <summary>The words the template starts with; empty when it starts with a value.</summary>
        public string Opening { get; } = template.Parts is [TemplateLiteral first, ..] ? first.Text : "";

        /// <summary>The words the template ends with; empty when it ends with a value.</summary>
        public string Closing { get; } = template.Parts is [.., TemplateLiteral last] ? last.Text : "";

        public bool EndsAsASentence { get; } = MessageParser.EndsAsASentence(template);
    }

    /// <summary>A value read from a piece of the text, how many characters of template words it matched, and the wording
    /// each message in it matched (by reference, for <see cref="MessageRenderer.RenderWith"/>).</summary>
    private sealed class Reading(MessageValue value, int score, ImmutableList<KeyValuePair<Message, MessageTemplate>> matched)
    {
        public MessageValue Value { get; } = value;

        public int Score { get; } = score;

        public ImmutableList<KeyValuePair<Message, MessageTemplate>> Matched { get; } = matched;

        public static Reading Plain(MessageValue value) => new(value, 0, []);
    }

    /// <summary>A template matched part of the way: the values read so far, the words matched — its own and its nested
    /// sentences' — and the nested wordings.</summary>
    private sealed class Partial(
        ImmutableDictionary<string, MessageValue> values,
        int score,
        int ownWords,
        ImmutableList<KeyValuePair<Message, MessageTemplate>> matched)
    {
        public static Partial Empty { get; } = new(ImmutableDictionary.Create<string, MessageValue>(StringComparer.Ordinal), 0, 0, []);

        public ImmutableDictionary<string, MessageValue> Values { get; } = values;

        public int Score { get; } = score;

        /// <summary>The characters of this template's own words matched.</summary>
        public int OwnWords { get; } = ownWords;

        public ImmutableList<KeyValuePair<Message, MessageTemplate>> Matched { get; } = matched;

        public Partial Plus(int words) => new(Values, Score + words, OwnWords + words, Matched);

        /// <summary>With the value <paramref name="name"/> read as <paramref name="reading"/>; <c>null</c> when a
        /// placeholder the template names twice read differently the other time.</summary>
        public Partial? With(string name, Reading reading)
        {
            if (Values.TryGetValue(name, out var earlier))
                return earlier.Equals(reading.Value) ? new Partial(Values, Score + reading.Score, OwnWords, Matched.AddRange(reading.Matched)) : null;
            return new Partial(Values.Add(name, reading.Value), Score + reading.Score, OwnWords, Matched.AddRange(reading.Matched));
        }
    }

    /// <summary>
    /// For a text cut at its column's limit (design §6.4): the frames' words that would have followed the sentence being
    /// read, and whether it is written as a clause — its final full stop left out.
    /// </summary>
    private readonly record struct CutTail(string Words, bool Clause);

    /// <summary>A reading of a list: its items' readings, and how many characters of joining words it matched.</summary>
    private sealed record ListReading(ImmutableList<Reading> Items, int Joins)
    {
        /// <summary>The characters of template words matched: the joining words and every item's own.</summary>
        public int Score { get; } = Joins + Items.Sum(reading => reading.Score);
    }

    /// <summary>
    /// One text's parse: the readings of its pieces as nested sentences, remembered, since the same piece is asked for
    /// by every template and every split that reaches it; and the step budget.
    /// </summary>
    private sealed class Session(MessageParser parser)
    {
        private readonly Dictionary<(string Text, CutTail? Cut), List<Reading>> _sentences = [];
        private readonly HashSet<(string Text, bool Cut)> _open = [];
        private long _steps;
        private int _nesting;

        /// <summary>The text took more steps than <see cref="StepBudget"/>: the parse gave up, and it is kept as its
        /// words.</summary>
        public bool GaveUp => _steps > StepBudget;

        /// <summary>
        /// The most specific verified readings of <paramref name="text"/> as a whole message of an
        /// <paramref name="eligible"/> wording, distinct, in a stable order. A reading must match at least one character of
        /// template words: one of placeholders alone around older words proves nothing.
        /// </summary>
        /// <param name="cut"><c>null</c> for a whole text; for a text cut at its column's limit, what would have followed
        /// the cut value.</param>
        public List<Reading> Messages(string text, CutTail? cut, Func<Wording, bool> eligible)
        {
            var (wordings, scanned) = parser.CandidatesFor(text, cut is not null);
            if (!Spend(1 + scanned / UnitsPerStep))
                return [];

            var best = new List<Reading>();
            // Kept apart by value with structural equality: a list scan would compare every pair of readings.
            var distinct = new HashSet<MessageValue>();
            var bestScore = 1;
            var bestOlder = int.MaxValue;
            foreach (var wording in wordings)
            {
                if (!eligible(wording))
                    continue;

                foreach (var partial in Step(wording.Template.Parts, 0, 0, Partial.Empty, text, cut))
                {
                    if (GaveUp)
                        return [];
                    if (partial.Score < bestScore || Verified(wording, partial, text, cut is not null) is not { } reading)
                        continue;

                    // At the same score, fewer older build's words first (OlderWordsIn).
                    var older = OlderWordsIn(reading.Value);
                    if (reading.Score > bestScore || older < bestOlder)
                    {
                        best.Clear();
                        distinct.Clear();
                        bestScore = reading.Score;
                        bestOlder = older;
                    }
                    else if (older > bestOlder)
                    {
                        continue;
                    }

                    if (best.Count < MaximumReadings && distinct.Add(reading.Value))
                        best.Add(reading);
                }
            }

            // By the canonical JSON (each written once, and counted like a reading written back): the same readings in the
            // same order on every run, whatever the catalogue's order.
            if (!Spend(best.Count * (1 + text.Length / UnitsPerStep)))
                return [];
            return [.. best.OrderBy(reading => MessageJson.Write((Message)reading.Value), StringComparer.Ordinal)];
        }

        private Reading? Verified(Wording wording, Partial partial, string text, bool cut)
        {
            var values = partial.Values;
            if (wording.CountIsOne && !values.ContainsKey(MessageRenderer.CountName))
                values = values.Add(MessageRenderer.CountName, new NumberValue(1));

            Message message;
            try
            {
                message = Message.Of(wording.Key, [.. values.Select(value => (value.Key, value.Value))]);
            }
            catch (ArgumentException)
            {
                // An older wording under a key this build can't name: not a reading.
                return null;
            }

            // A frame that starts with the value taking the cut matched none of its own words — the ones after the value
            // were cut away: it would wrap any cut reading of the text and tie with it.
            if (cut && partial.OwnWords == 0)
                return null;

            // An older nested wording is a last resort for one sentence inside a frame that reads. Two of them would be
            // a guess at where one ends and the next begins — "{{error, message}} {{tally, message}}." splits any words
            // at any space —, and would outscore the frame that reads the same words as one.
            if (OlderWordsIn(message) > 1)
                return null;

            // Writing a reading back is the costliest step, counted by the characters it writes per message: a nested
            // message is written into its frame's text, and that into its own frame's.
            var matched = partial.Matched.Add(new KeyValuePair<Message, MessageTemplate>(message, wording.Template));
            if (!Spend(1 + (long)text.Length * matched.Count / UnitsPerStep))
                return null;

            return parser.WritesBack(message, matched, text, cut) ? new Reading(message, partial.Score, matched) : null;
        }

        /// <summary>Every way the template's parts from <paramref name="index"/> on match <paramref name="text"/> from
        /// <paramref name="position"/> to its end.</summary>
        private IEnumerable<Partial> Step(
            IReadOnlyList<TemplatePart> parts, int index, int position, Partial partial, string text, CutTail? cut)
        {
            if (!Spend(1))
                yield break;

            if (index == parts.Count)
            {
                // A cut text is read only through a value that took its cut (Absorb): a template that ended before the cut
                // didn't write the text.
                if (cut is null && position == text.Length)
                    yield return partial;
                yield break;
            }

            if (parts[index] is TemplateLiteral literal)
            {
                if (text.AsSpan(position).StartsWith(literal.Text, StringComparison.Ordinal))
                {
                    foreach (var next in Step(parts, index + 1, position + literal.Text.Length, partial.Plus(literal.Text.Length), text, cut))
                        yield return next;
                }

                yield break;
            }

            var slot = (TemplateSlot)parts[index];
            var after = index + 1 < parts.Count ? parts[index + 1] : null;
            // Only the last value — on the right spine, recursively into a nested sentence — takes a cut (design §6.4): a
            // value after it would have no words to read it from.
            if (cut is { } tail && !parts.Skip(index + 1).Any(part => part is TemplateSlot))
            {
                if (position >= text.Length)
                    yield break;

                // What would have followed the cut: the template's last words — a clause's final full stop left out —, then
                // the frame's. A nested sentence that ends its frame ends where the frame does: a clause too, if it is one.
                var last = after is TemplateLiteral next ? next.Text : "";
                if (tail.Clause && last.EndsWith(FullStop, StringComparison.Ordinal))
                    last = last[..^FullStop.Length];
                foreach (var reading in Absorb(slot, text[position..], new CutTail(last + tail.Words, slot.Hint.Kind == SlotKind.Clause || (tail.Clause && after is null))))
                {
                    if (partial.With(slot.Name, reading) is { } taken)
                        yield return taken;
                }

                // A cut text is read only through the value that took its cut: one ending before the cut writes no text.
                yield break;
            }

            foreach (var end in Ends(text, position, after))
            {
                // Each place a value may end costs its piece and its reading, whether or not that reads: counted, so the
                // budget bounds the time and not only the matches.
                if (!Spend(1))
                    yield break;

                foreach (var reading in Values(slot, text[position..end]))
                {
                    // A nested reading's wordings are carried into the frame's: counted by their number.
                    if (!Spend(reading.Matched.Count / UnitsPerStep))
                        yield break;
                    if (partial.With(slot.Name, reading) is not { } taken)
                        continue;
                    foreach (var next in Step(parts, index + 1, end, taken, text, cut))
                        yield return next;
                }
            }
        }

        /// <summary>Where a value starting at <paramref name="position"/> can end: before each place the words after it
        /// occur, nearest first — a name holding those words ends at a later one —, or at the text's end.</summary>
        private static IEnumerable<int> Ends(string text, int position, TemplatePart? after)
        {
            if (position >= text.Length)
                yield break;

            switch (after)
            {
                case null:
                    // The last value takes the rest (in a cut text, it took the cut: Absorb).
                    yield return text.Length;
                    break;
                case TemplateLiteral next:
                    for (var at = text.IndexOf(next.Text, position + 1, StringComparison.Ordinal); at >= 0;
                         at = at + 1 < text.Length ? text.IndexOf(next.Text, at + 1, StringComparison.Ordinal) : -1)
                        yield return at;
                    break;
                default:
                    // Two values with no words between, which LocaleParityTests refuses in English: every split.
                    for (var at = position + 1; at < text.Length; at++)
                        yield return at;
                    break;
            }
        }

        /// <summary>What a whole piece of the text reads as, by the placeholder's hint.</summary>
        private IEnumerable<Reading> Values(TemplateSlot slot, string piece) => slot.Hint.Kind switch
        {
            SlotKind.Text => [Reading.Plain(new TextValue(piece))],
            SlotKind.Name => Name(piece),
            SlotKind.Vendor => [Reading.Plain(VendorText.Stored(piece))],
            SlotKind.Number => NumberOf(piece) is { } number ? [Reading.Plain(new NumberValue(number))] : [],
            SlotKind.GroupedNumber => GroupedNumber().IsMatch(piece) && NumberOf(piece.Replace(",", "", StringComparison.Ordinal)) is { } grouped
                ? [Reading.Plain(new NumberValue(grouped))]
                : [],
            SlotKind.WordNumber => (parser._countWords.TryGetValue(piece, out var word) ? word : NumberOf(piece)) is { } counted
                ? [Reading.Plain(new NumberValue(counted))]
                : [],
            SlotKind.Size => Sizes(piece),
            SlotKind.Duration => DurationOf(piece) is { } duration ? [Reading.Plain(duration)] : [],
            SlotKind.Message => NestedMessage(piece),
            SlotKind.Clause => NestedClause(piece),
            _ => Lists(slot.Hint, piece),
        };

        /// <summary>
        /// What the rest of a cut text reads as when this placeholder took the cut: a name, a path or a service's words
        /// ending in the <c>…</c>, or a nested sentence cut the same way. A cut inside a number, a size, a running time or
        /// a list can't be told from a broken value: no reading.
        /// </summary>
        /// <param name="following">The words that would have followed the value, and — for a nested sentence — whether
        /// it is written as a clause.</param>
        private IEnumerable<Reading> Absorb(TemplateSlot slot, string piece, CutTail following)
        {
            switch (slot.Hint.Kind)
            {
                case SlotKind.Text or SlotKind.Name:
                    return CutInValue(piece, following.Words) ? [Reading.Plain(new TextValue(piece))] : [];
                case SlotKind.Vendor:
                    return CutInValue(piece, following.Words) ? [Reading.Plain(VendorText.Stored(piece))] : [];
                case SlotKind.Message or SlotKind.Clause:
                    var readings = Sentences(piece, following);
                    return readings.Count > 0 ? readings : CutInValue(piece, following.Words) ? [Legacy(piece)] : [];
                default:
                    return [];
            }
        }

        /// <summary>
        /// Whether the cut can lie inside the value that ends the text as <paramref name="piece"/>. Not when the value
        /// before the <c>…</c> ends with the start of the words that would follow it — the cut fell in those words, and
        /// the "value" would hold them —, nor when those words start with a mark that closes a value (<c>”</c>,
        /// <c>)</c>, <c>.</c>) and the value holds it: the value most likely ended there, and the cut fell in later
        /// words. A value that really holds that mark is kept as words too: a wrong value would be worse.
        /// </summary>
        private static bool CutInValue(string piece, string following)
        {
            var value = piece.AsSpan(0, piece.Length - Ellipsis.Length);
            for (var length = 1; length <= following.Length && length <= value.Length; length++)
            {
                if (value.EndsWith(following.AsSpan(0, length), StringComparison.Ordinal))
                    return false;
            }

            return !(following.Length > 0 && !char.IsLetterOrDigit(following[0]) && !char.IsWhiteSpace(following[0])
                && value.Contains(following[0]));
        }

        /// <summary>A nested sentence: its most specific readings, or — the last resort — the words of an older build as
        /// they are, so an outer sentence that reads keeps its structure around an older nested wording.</summary>
        private List<Reading> NestedMessage(string piece)
        {
            var readings = Sentences(piece, cut: null);
            return readings.Count > 0 ? readings : [Legacy(piece)];
        }

        /// <summary>A clause: its sentence without one final full stop — read with the stop put back, or as it is when
        /// the sentence ends otherwise ("…?").</summary>
        private List<Reading> NestedClause(string piece)
        {
            var readings = Best(
                Sentences(piece + FullStop, cut: null),
                piece.EndsWith(FullStop, StringComparison.Ordinal) ? [] : Sentences(piece, cut: null));
            return readings.Count > 0 ? readings : [Legacy(piece + FullStop)];
        }

        /// <summary><see cref="Messages"/> of every wording, remembered per piece.</summary>
        private List<Reading> Sentences(string text, CutTail? cut)
        {
            var key = (text, cut);
            if (_sentences.TryGetValue(key, out var known))
                return known;
            // A wording that starts with a nested sentence asks for this very text again when it is cut there — with the
            // frame's words after it each time, so by the text alone: such a reading would match no words of its own.
            var open = (text, cut is not null);
            if (_nesting == MaximumNesting || !_open.Add(open))
                return [];

            _nesting++;
            var readings = Messages(text, cut, static _ => true);
            _nesting--;
            _open.Remove(open);
            _sentences[key] = readings;
            return readings;
        }

        private IEnumerable<Reading> Sizes(string piece)
        {
            foreach (var (wording, bytesPerUnit) in parser._sizes)
            {
                foreach (var partial in Step(wording.Template.Parts, 0, 0, Partial.Empty, piece, cut: null))
                {
                    var units = wording.CountIsOne ? 1m
                        : partial.Values.GetValueOrDefault(MessageRenderer.CountName) is NumberValue count ? count.Number
                        : -1m;
                    if (units >= 0 && units == decimal.Truncate(units) && units <= long.MaxValue / bytesPerUnit)
                        yield return new Reading(new SizeValue((long)units * bytesPerUnit), partial.Score, []);
                }
            }
        }

        // ── lists ──

        /// <summary>
        /// A list: its items cut at the joining words and each read by the list's kind. Plain text items are cut at every
        /// separator and before the last joining word only — a name holding ", " or " and " can't be told from two names,
        /// and English reads the same either way; quoted items and sentences must each read, which finds their ends.
        /// </summary>
        private IEnumerable<Reading> Lists(SlotHint hint, string piece)
        {
            if (parser._listSeparator is not { } separator)
                return [];

            Func<string, IReadOnlyList<Reading>> item = hint.Kind switch
            {
                SlotKind.QuotedList => Quoted,
                SlotKind.MessageList => text => Sentences(text, cut: null),
                SlotKind.NameList => Name,
                _ => text => [Reading.Plain(new TextValue(text))],
            };
            var plain = hint.Kind is SlotKind.List or SlotKind.NameList;
            var lists = hint.Join switch
            {
                ListJoin.Comma => Split(piece, separator, item, plain),
                ListJoin.Space => Split(piece, Space, item, plain),
                _ => Joined(piece, separator, hint.Join == ListJoin.Or ? parser._listOr : parser._listAnd, item, plain),
            };
            // Sentences one after another that read no way are the words of an older build, kept as one item.
            if (lists.Count == 0 && hint.Kind == SlotKind.MessageList)
                lists.Add(new ListReading([Legacy(piece)], 0));

            return lists.Select(list => new Reading(
                new ListValue(list.Items.Select(reading => reading.Value)),
                list.Score,
                [.. list.Items.SelectMany(reading => reading.Matched)]));
        }

        private List<ListReading> Joined(
            string piece, string separator, string? word, Func<string, IReadOnlyList<Reading>> item, bool plain)
        {
            var lists = new List<ListReading>();
            if (word is null || word.Length == 0)
                return lists;

            // One item is the item alone.
            foreach (var reading in item(piece))
                KeepIfBest(lists, new ListReading([reading], 0));

            // Two or more: the last after the joining word, the others cut at the separator before it.
            var places = new List<int>();
            for (var at = piece.IndexOf(word, 1, StringComparison.Ordinal); at >= 0;
                 at = at + 1 < piece.Length ? piece.IndexOf(word, at + 1, StringComparison.Ordinal) : -1)
                places.Add(at);
            foreach (var at in Enumerable.Reverse(places).Take(plain ? 1 : places.Count))
            {
                // Each place is a piece read, counted like a place a value may end.
                if (!Spend(1))
                    break;

                var last = piece[(at + word.Length)..];
                var lastItems = last.Length == 0 ? [] : item(last);
                if (lastItems.Count == 0)
                    continue;
                foreach (var others in Split(piece[..at], separator, item, plain))
                {
                    foreach (var lastItem in lastItems)
                        KeepIfBest(lists, new ListReading(others.Items.Add(lastItem), others.Joins + word.Length));
                }
            }

            return lists;
        }

        /// <summary>The most specific ways <paramref name="piece"/> is items joined by <paramref name="separator"/>.</summary>
        private List<ListReading> Split(string piece, string separator, Func<string, IReadOnlyList<Reading>> item, bool plain)
        {
            var lists = new List<ListReading>();
            if (plain)
            {
                var texts = piece.Split(separator);
                if (Spend(texts.Length) && texts.All(text => text.Length > 0))
                    lists.Add(new ListReading([.. texts.Select(text => Reading.Plain(new TextValue(text)))], (texts.Length - 1) * separator.Length));
                return lists;
            }

            // Depth first, the nearest separator first — so the list of the most items, the most specific, is found first
            // — and on a stack of its own: a call per item would nest as deep as the list is long, and a long list of short
            // items overflowed the thread's stack (WP4 review). Each frame is an item's start and the next separator to
            // end it at; after MaximumReadings whole lists the rest are coarser cuts of the same items.
            var found = 0;
            var frames = new Stack<(int Start, ImmutableList<Reading> Items, int Joins, int From)>();
            frames.Push((0, [], 0, 1));
            while (frames.TryPop(out var frame) && Spend(1))
            {
                var (start, items, joins, from) = frame;
                if (start + from > piece.Length)
                    continue;

                var at = piece.IndexOf(separator, start + from, StringComparison.Ordinal);
                if (at < 0)
                {
                    // The last item: each of its readings ends a whole list.
                    foreach (var reading in item(piece[start..]))
                    {
                        KeepIfBest(lists, new ListReading(items.Add(reading), joins));
                        if (++found == MaximumReadings)
                            return lists;
                    }

                    continue;
                }

                // The next separator this item could end at, once the lists that end it here are read; then those lists,
                // the first reading of the item first.
                frames.Push((start, items, joins, at - start + 1));
                var readings = item(piece[start..at]);
                for (var index = readings.Count - 1; index >= 0; index--)
                    frames.Push((at + separator.Length, items.Add(readings[index]), joins + separator.Length, 1));
            }

            return lists;
        }

        /// <summary>
        /// Keeps <paramref name="list"/> among <paramref name="lists"/> when it matches at least as many words as they do,
        /// dropping them when it matches more: only a piece's most specific readings can win the frame around it, and
        /// keeping only those (at most <see cref="MaximumReadings"/>) keeps a list of many items from multiplying.
        /// </summary>
        private static void KeepIfBest(List<ListReading> lists, ListReading list)
        {
            if (lists.Count > 0 && list.Score < lists[0].Score)
                return;
            if (lists.Count > 0 && list.Score > lists[0].Score)
                lists.Clear();
            if (lists.Count < MaximumReadings)
                lists.Add(list);
        }

        /// <summary>Counts <paramref name="steps"/> of work; <c>false</c> once the parse has given up.</summary>
        private bool Spend(long steps)
        {
            _steps += steps;
            return !GaveUp;
        }

        /// <summary>
        /// A name: its text, and — when its words are a label's — that label too, which matches its words and so wins.
        /// </summary>
        /// <remarks>MAM-47 WP14 review — an older build wrote a work with no title as English words where a name goes
        /// ("a work with no title (2005)"). Read back as text they stayed English inside every other language's sentence
        /// («…як фільм a work with no title (2005)…»); read back as the label they are written as today's producer writes
        /// them, «без назви (2005)».</remarks>
        private List<Reading> Name(string text)
        {
            var readings = new List<Reading> { Reading.Plain(new TextValue(text)) };
            foreach (var wording in parser._nameLabels)
            {
                foreach (var partial in Step(wording.Template.Parts, 0, 0, Partial.Empty, text, cut: null))
                {
                    if (Verified(wording, partial, text, cut: false) is { } reading)
                        readings.Add(reading);
                }
            }

            return readings;
        }

        /// <summary>An item in the language's quotes (<c>common.quoted</c>), read as its text.</summary>
        private List<Reading> Quoted(string text)
        {
            var readings = new List<Reading>();
            if (parser._quoted is not { } quoted)
                return readings;
            foreach (var partial in Step(quoted.Template.Parts, 0, 0, Partial.Empty, text, cut: null))
            {
                if (partial.Values.GetValueOrDefault(QuotedTextName) is TextValue value)
                    readings.Add(new Reading(value, partial.Score, []));
            }

            return readings;
        }

        // ── values ──

        private static decimal? NumberOf(string piece) =>
            PlainNumber().IsMatch(piece)
            && decimal.TryParse(piece, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        private static DurationValue? DurationOf(string piece)
        {
            var match = Clock().Match(piece);
            if (!match.Success || !long.TryParse(match.Groups["hours"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                || hours > MaximumDurationHours)
                return null;

            var minutes = int.Parse(match.Groups["minutes"].ValueSpan, CultureInfo.InvariantCulture);
            var seconds = int.Parse(match.Groups["seconds"].ValueSpan, CultureInfo.InvariantCulture);
            return new DurationValue((((hours * 60) + minutes) * 60 + seconds) * 1000);
        }

        private static Reading Legacy(string words) => Reading.Plain(Message.Legacy(words));

        /// <summary>The most specific readings of two lists together.</summary>
        private static List<Reading> Best(List<Reading> first, List<Reading> second)
        {
            var all = first.Concat(second).ToList();
            if (all.Count == 0)
                return all;
            var top = all.Max(reading => reading.Score);
            var fewestOlder = all.Where(reading => reading.Score == top).Min(reading => OlderWordsIn(reading.Value));
            var best = new List<Reading>();
            foreach (var reading in all.Where(reading => reading.Score == top && OlderWordsIn(reading.Value) == fewestOlder))
            {
                if (!best.Any(kept => kept.Value.Equals(reading.Value)))
                    best.Add(reading);
            }

            return best;
        }
    }
}
