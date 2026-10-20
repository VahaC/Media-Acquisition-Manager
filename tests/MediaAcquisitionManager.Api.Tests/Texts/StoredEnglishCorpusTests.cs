using System.Text.Json;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042, design §6.3–§6.4, §6.8) — the startup converter's parser over English an older build stored: the
// corpus of today's sentences (stored-english-corpus.json) reads back as one message each, and written back gives the
// very text — the proof that the templates kept today's English byte for byte;
// and the parser's rules for stored text — a cut at the column's limit, a service's own words, an older wording, a plural
// slip, a tie — over a catalogue of the server's real shapes.
public sealed class StoredEnglishCorpusTests
{
    private const int ColumnLength = 1000;

    private static readonly string[] Outcomes = ["converted", "cut-value", "kept", "ambiguous"];

    private static readonly TemplateCatalog Stored = TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/stored.json", EnglishStored));

    private static readonly MessageParser Parser = new(Stored, OlderEnglish);

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

    // The shapes of the server's stored sentences (design §2.1, §5.5): quoted paths, a service's words, nested sentences,
    // a counted tally, a clause, a fragment and the consent words.
    private const string EnglishStored = """
        {
          "no-file": "There is no file at “{{path}}”.",
          "into-quarantine": "“{{path}}” went into the quarantine.",
          "couldnt-change": "The app couldn't rename or delete “{{path}}”: {{reason, vendor}}.",
          "not-moved": "“{{file}}” couldn't be moved into the quarantine: {{error, message}}",
          "not-moved-tally": "“{{file}}” couldn't be moved into the quarantine: {{error, message}} {{tally, message}}.",
          "tally": {
            "in-quarantine_one": "{{count, number}} of {{names, message}} is in the quarantine",
            "in-quarantine_other": "{{count, number}} of {{names, message}} are in the quarantine",
            "names_one": "{{count, number}} name",
            "names_other": "{{count, number}} names"
          },
          "feed-failed": "The Torznab feed {{what}} the release (error {{code}}: {{said, vendor}}).",
          "handover-failed": "The Torznab feed failed to hand over the release (error {{code}}: {{said, vendor}}).",
          "has-files_one": "“{{name}}” has {{count, number}} file.",
          "has-files_other": "“{{name}}” has {{count, number}} files.",
          "runs": "It runs {{time, duration}}, less than the old copy.",
          "nothing-sent": "{{reason, message:clause}}, so nothing was sent.",
          "moved_one": "{{count, number}} file was moved.",
          "moved_other": "{{count, number}} files were moved.",
          "gone": "The copy is gone.",
          "lost": "The copy is gone.",
          "renamed": "{{from}} became {{to}}.",
          "work": "{{title}} ({{year}})",
          "confirm": {
            "work": "This release is {{work, message}}",
            "work-season": "This release is {{work, message}}, season {{season, number}}"
          }
        }
        """;

    private const string OlderEnglish = """
        {
          "stored.no-file": ["No file at “{{path}}”."],
          "stored.couldnt-change": ["Couldn't change “{{path}}”."]
        }
        """;

    private static string CorpusFile =>
        Path.Combine(RepositoryPaths.Root, "tests", "MediaAcquisitionManager.Api.Tests", "Texts", "stored-english-corpus.json");

    // ── the corpus over the server's own templates ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_corpus_text_reads_as_expected()
    {
        //Arrange
        var corpus = ReadCorpus();

        //Act
        var problems = corpus.SelectMany(entry => ProblemsOf(MessageParser.Embedded, entry)).ToList();

        //Assert
        // The check never runs over nothing (the corpus's size is pinned below).
        Assert.NotEmpty(corpus);
        Assert.True(
            problems.Count == 0,
            $"Stored English that doesn't read back as expected — today's English must come back byte for byte from one key (design §6.8):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    public void The_corpus_holds_valid_entries_for_every_column_that_stores_a_sentence()
    {
        //Arrange
        string[] columns =
        [
            "DownloadJobs.Error", "DownloadJobs.BlockReason", "SearchSources.Error", "ReplacementJobs.Reason",
            "ReplacementJobFiles.Problem", "ReplacementJobNewFiles.Problem", "ReplacementJobNames.Problem", "CopyRemovalNames.Problem",
            "CopyRemovals.Reason", "ReplacementJobWorks.ConfirmationText", "LibraryItemLinks.WriteError", "PhysicalFiles.InspectionError",
            "MediaInspections.Error", "FileInspectionRuns.Error", "FileInspectionRuns.HashProblem", "FolderScanRuns.Error",
            "FolderScanRuns.Problem", "LibrarySyncRuns.Error", "LibraryLinkSuggestionRuns.Error", "RecommendationRuns.Error",
            "AuditEvents.TargetName",
        ];

        //Act
        var corpus = ReadCorpus();
        var invalid = corpus
            .Where(entry => entry.Text.Length == 0 || entry.Source.Length == 0 || !Outcomes.Contains(entry.Expect)
                || (entry.Expect == "cut-value") != (entry.Text.Length == entry.Length && entry.Text.EndsWith('…'))
                || entry.Text.Length > entry.Length)
            .Select(entry => entry.Text)
            .ToList();
        // LibraryItemLinks.LookupError has no writer since 0.2.1 (ADR 0026): no sentence of today's to hold.
        var uncovered = columns.Where(column => !corpus.Any(entry => entry.Column == column)).ToList();

        //Assert
        Assert.Empty(invalid);
        Assert.True(corpus.Count >= 60, $"The corpus holds {corpus.Count} texts.");
        Assert.True(uncovered.Count == 0, $"Columns with no text in the corpus: {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void Embedded_parser_reads_the_engines_own_sentence()
    {
        //Arrange
        const string text = "No API endpoint matches POST /api/nope/deeper.";

        //Act
        var candidates = MessageParser.Embedded.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(Message.Of("common.request.unknown-endpoint", ("method", "POST"), ("path", "/api/nope/deeper")), candidate.Message);
        Assert.False(candidate.CutValue);
    }

    // ── what a stored text reads as ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_reads_a_sentence_quoting_a_services_words()
    {
        //Arrange
        const string text = "The Torznab feed failed to hand over the release (error 500: Download failed).";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        // feed-failed reads it too ("failed to hand over" as its value), with fewer words of its own: the most specific wins.
        var candidate = Assert.Single(candidates);
        Assert.Equal(Message.Of("stored.handover-failed", ("code", "500"), ("said", VendorText.Clean("Download failed", 300))), candidate.Message);
        Assert.Equal(text, Written(candidate.Message));
    }

    [Fact]
    public void Parse_keeps_a_masked_secret_inside_the_services_words()
    {
        //Arrange
        const string text = "The Torznab feed failed to hand over the release (error 500: Indexer request failed for apikey=***).";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(VendorText.Clean("Indexer request failed for apikey=***", 300), candidate.Message.Values["said"]);
    }

    [Fact]
    public void Parse_reads_a_name_that_holds_the_words_after_it()
    {
        //Arrange
        // The path holds "”.", the words its template closes with: the value ends at the later one.
        const string text = "There is no file at “/media/Movies/Alien”. The Director's Cut.mkv”.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(Message.Of("stored.no-file", ("path", "/media/Movies/Alien”. The Director's Cut.mkv")), candidate.Message);
    }

    [Fact]
    public void Parse_reads_a_nested_sentence_with_an_errno_tail_and_a_counted_tally()
    {
        //Arrange
        const string text =
            "“Alien (1979).mkv” couldn't be moved into the quarantine: The app couldn't rename or delete “/media/Alien (1979).mkv”: " +
            "Device or resource busy (errno 16). 1 of 3 names is in the quarantine.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        var expected = Message.Of(
            "stored.not-moved-tally",
            ("file", "Alien (1979).mkv"),
            ("error", Message.Of("stored.couldnt-change", ("path", "/media/Alien (1979).mkv"), ("reason", VendorText.Clean("Device or resource busy (errno 16)", 300)))),
            ("tally", Message.Of("stored.tally.in-quarantine", ("count", 1), ("names", Message.Of("stored.tally.names", ("count", 3))))));
        Assert.Equal(expected, candidate.Message);
        Assert.Equal(text, Written(candidate.Message));
    }

    [Fact]
    public void Parse_keeps_an_older_nested_sentence_as_its_words_inside_a_frame_it_reads()
    {
        //Arrange
        const string text = "“Alien (1979).mkv” couldn't be moved into the quarantine: The disk said no.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(
            Message.Of("stored.not-moved", ("file", "Alien (1979).mkv"), ("error", Message.Legacy("The disk said no."))),
            candidate.Message);
    }

    [Theory]
    [InlineData("The feed can't hand over this release any more (410 Gone). Search again.", "x.gone-status")]
    [InlineData("The feed can't hand over this release any more (The release is gone.). Search again.", "x.gone")]
    public void Parse_reads_todays_value_before_an_older_nested_sentence_that_matches_as_many_words(string text, string key)
    {
        //Arrange
        // MAM-47 (merge of WP4) — Torznab's gone and gone-status read "(410 Gone)" with the same words of their own: the
        // status as a value, or as an unread nested sentence. A tie keeps the row as words, so the nested sentence's last
        // resort loses; a parenthetical that reads as a sentence still makes the error wording the more specific one.
        var catalog = TemplateCatalog.From(
            ("en/common.json", EnglishCommon),
            ("en/x.json", """
                {
                  "gone": "The feed can't hand over this release any more ({{error, message}}). Search again.",
                  "gone-status": "The feed can't hand over this release any more ({{status}}). Search again.",
                  "said": "The release is gone."
                }
                """));
        var parser = new MessageParser(catalog, "{}");

        //Act
        var candidates = parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(key, candidate.Message.Key);
        Assert.DoesNotContain(Message.LegacyKey, MessageJson.Write(candidate.Message), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_reads_nothing_from_a_services_own_words()
    {
        //Arrange
        const string text = "Device or resource busy (errno 16).";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.Empty(candidates);
    }

    [Fact]
    public void Parse_reads_no_fragment_as_a_whole_stored_text()
    {
        //Arrange
        // "{{title}} ({{year}})" fits a service's words — "Device or resource busy" and "errno 16" —, but a column holds
        // whole sentences: only a wording that ends as one reads a whole text, unless a key prefix says otherwise.
        const string text = "Device or resource busy (errno 16)";

        //Act
        var asColumn = Parser.Parse(text, ColumnLength);
        var asAnyKey = Parser.Parse(text, ColumnLength, keyPrefix: "");

        //Assert
        Assert.Empty(asColumn);
        Assert.Equal("stored.work", Assert.Single(asAnyKey).Message.Key);
    }

    [Fact]
    public void Parse_reads_the_consent_words_through_the_keys_a_prefix_names()
    {
        //Arrange
        // The consent words end with no full stop: without the prefix, the wording with the season can't read them whole.
        const string text = "This release is Alien (1979), season 2";

        //Act
        var asConsent = Parser.Parse(text, ColumnLength, keyPrefix: "stored.confirm.");
        var asColumn = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(asConsent);
        Assert.Equal(
            Message.Of("stored.confirm.work-season", ("work", Message.Of("stored.work", ("title", "Alien"), ("year", "1979"))), ("season", 2)),
            candidate.Message);
        Assert.DoesNotContain(asColumn, reading => reading.Message.Key == "stored.confirm.work-season");
    }

    [Fact]
    public void Parse_reads_no_key_outside_the_prefix()
    {
        //Arrange
        const string text = "The copy is gone.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength, keyPrefix: "stored.confirm.");

        //Assert
        Assert.Empty(candidates);
    }

    [Fact]
    public void Parse_reads_an_older_wording_and_today_writes_todays()
    {
        //Arrange
        const string text = "No file at “/media/Alien (1979).mkv”.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(Message.Of("stored.no-file", ("path", "/media/Alien (1979).mkv")), candidate.Message);
        Assert.Equal("There is no file at “/media/Alien (1979).mkv”.", Written(candidate.Message));
    }

    [Fact]
    public void Parse_keeps_an_older_wording_that_lacks_a_value_todays_needs()
    {
        //Arrange
        // A row read this way couldn't be shown: today's wording needs the operating system's words the older one left out.
        const string text = "Couldn't change “/media/Alien (1979).mkv”.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.Empty(candidates);
    }

    [Fact]
    public void Parse_reads_a_plural_slip_with_the_form_it_was_written_in()
    {
        //Arrange
        const string text = "1 files were moved.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.Equal(Message.Of("stored.moved", ("count", 1)), candidate.Message);
        Assert.Equal("1 file was moved.", Written(candidate.Message));
    }

    [Fact]
    public void Parse_gives_both_keys_of_one_wording()
    {
        //Arrange
        const string text = "The copy is gone.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.Equal(["stored.gone", "stored.lost"], candidates.Select(candidate => candidate.Message.Key));
    }

    [Fact]
    public void Parse_gives_every_reading_of_names_that_hold_the_words_between_them()
    {
        //Arrange
        const string text = "A became B became C.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.Equal(
            [
                Message.Of("stored.renamed", ("from", "A became B"), ("to", "C")),
                Message.Of("stored.renamed", ("from", "A"), ("to", "B became C")),
            ],
            candidates.Select(candidate => candidate.Message));
    }

    [Fact]
    public void Parse_reads_a_long_nested_sentence_whose_values_hold_many_spaces()
    {
        //Arrange
        // MAM-47 (WP4 review) — the nested sentence and its tally hold about a hundred places where a value could end,
        // and an older build's words fit at each: the parser once stopped after 64 such matches, before the real one.
        var path = "/media/Movies/" + string.Join("/", Enumerable.Range(1, 20).Select(index => $"Folder {index} of the collection"));
        var reason = string.Join(" ", Enumerable.Range(1, 40).Select(index => $"busy{index}"));
        var text = $"“Alien (1979).mkv” couldn't be moved into the quarantine: The app couldn't rename or delete “{path}”: {reason}. 1 of 3 names is in the quarantine.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.True(text.Count(character => character == ' ') > 64, "The text should hold more than 64 spaces.");
        var candidate = Assert.Single(candidates);
        var expected = Message.Of(
            "stored.not-moved-tally",
            ("file", "Alien (1979).mkv"),
            ("error", Message.Of("stored.couldnt-change", ("path", path), ("reason", VendorText.Clean(reason, 300)))),
            ("tally", Message.Of("stored.tally.in-quarantine", ("count", 1), ("names", Message.Of("stored.tally.names", ("count", 3))))));
        Assert.Equal(expected, candidate.Message);
        Assert.Equal(text, Written(candidate.Message));
    }

    [Fact]
    public void Parse_keeps_a_text_that_reads_more_ways_than_it_keeps_apart_ambiguous()
    {
        //Arrange
        // MAM-47 (WP4 review) — 20 places "became" could split the names: past the readings kept apart, the text stays
        // ambiguous and is never read as one of the first few.
        var text = string.Join(" became ", Enumerable.Range(1, 21).Select(index => $"N{index}")) + ".";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.True(candidates.Count >= 2, $"{candidates.Count} readings.");
        Assert.All(candidates, candidate => Assert.Equal("stored.renamed", candidate.Message.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("There is no file at “/media/Alien (1979).mkv”")]
    [InlineData("There is no file at “/media/Alien (1979).mkv”. And more.")]
    public void Parse_reads_nothing_from_a_text_no_wording_writes(string text)
    {
        //Arrange

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.Empty(candidates);
    }

    // ── a text cut at its column's limit ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_reads_a_text_cut_inside_its_last_value()
    {
        //Arrange
        var text = Cut("There is no file at “/media/Movies/A very long folder name/Alien (1979).mkv”.", 40);

        //Act
        var candidates = Parser.Parse(text, text.Length);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.True(candidate.CutValue);
        Assert.Equal("There is no file at “/media/Movies/A ve…", text);
        Assert.Equal(Message.Of("stored.no-file", ("path", "/media/Movies/A ve…")), candidate.Message);
        Assert.Equal("There is no file at “/media/Movies/A ve…”.", Written(candidate.Message));
    }

    [Fact]
    public void Parse_reads_a_text_cut_inside_a_nested_sentences_last_value()
    {
        //Arrange
        var text = Cut("“Alien (1979).mkv” couldn't be moved into the quarantine: There is no file at “/media/Movies/Alien (1979).mkv”.", 90);

        //Act
        var candidates = Parser.Parse(text, text.Length);

        //Assert
        // Read whole, the frame would take the cut sentence as an older one's words: the cut reading reads it too.
        var candidate = Assert.Single(candidates);
        Assert.True(candidate.CutValue);
        Assert.Equal(
            Message.Of("stored.not-moved", ("file", "Alien (1979).mkv"), ("error", Message.Of("stored.no-file", ("path", "/media/Mov…")))),
            candidate.Message);
        Assert.StartsWith(text, Written(candidate.Message));
    }

    [Theory]
    // The cut fell in the template's words after the value: the "value" would hold them.
    [InlineData("“/media/Alien (1979).mkv” went into the quarantine.", 30)]
    // The cut fell in a number: no value but the last on the right spine takes a cut.
    [InlineData("“Alien (1979)” has 1234 files.", 22)]
    // The cut fell in a running time.
    [InlineData("It runs 1:56:30, less than the old copy.", 12)]
    // The nested clause ended, and the cut fell in the frame's words after it.
    [InlineData("There is no file at “/a.mkv”, so nothing was sent.", 37)]
    // The cut fell in a value that isn't the last: the values after it would have no words to be read from.
    [InlineData("The app couldn't rename or delete “/media/Alien (1979).mkv”: Device or resource busy (errno 16).", 50)]
    public void Parse_keeps_a_text_cut_anywhere_but_inside_its_last_value(string whole, int length)
    {
        //Arrange
        var text = Cut(whole, length);

        //Act
        var candidates = Parser.Parse(text, text.Length);

        //Assert
        Assert.Empty(candidates);
    }

    [Theory]
    // The cut fell inside the path, which holds a "." — not the mark that follows the path in the text: the path is the
    // cut value, three sentences deep (once read as an older build's words, the "." taken for the path's end).
    [InlineData("Sending stopped: The move failed: It failed on /media/Movies/A very long folder name/Alien.mkv", "/media/Movies/A very long folder name/Alien.mkv…")]
    // The cut fell in the frame's words right after the path — its ", so" —, the nested sentence's final "." left out by
    // the clause two frames up: no reading (once the path took the comma).
    [InlineData("Sending stopped: The move failed: It failed on /media/Movies/Alien Director's Cut,", null)]
    public void Parse_reads_a_cut_at_the_end_of_a_clause_with_the_words_that_follow_the_clause(string start, string? path)
    {
        //Arrange
        // MAM-47 (WP4 review) — a nested sentence that ends its frame ends where the frame does; when the frame is a clause,
        // the words after the cut value are the clause's frame's, not the nested sentence's final ".".
        var catalog = TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/x.json", """
            {
              "stopped": "Sending stopped: {{reason, message:clause}}, so nothing was sent.",
              "failed": "The move failed: {{reason, message}}",
              "failed-on": "It failed on {{path}}."
            }
            """));
        var parser = new MessageParser(catalog, "{}");
        var text = start + "…";

        //Act
        var candidates = parser.Parse(text, text.Length);

        //Assert
        var expected = path is null
            ? null
            : Message.Of("x.stopped", ("reason", Message.Of("x.failed", ("reason", Message.Of("x.failed-on", ("path", path))))));
        Assert.Equal(expected, candidates.SingleOrDefault()?.Message);
        Assert.All(candidates, candidate => Assert.True(candidate.CutValue));
    }

    [Fact]
    public void Parse_reads_a_text_at_the_limit_whole_when_it_reads_whole()
    {
        //Arrange
        // A service's own words cut at 300 end with "…" too; a text that reads whole is no cut text.
        const string text = "“Alien (1979).mkv” couldn't be moved into the quarantine: The disk said no…";

        //Act
        var candidates = Parser.Parse(text, text.Length);

        //Assert
        var candidate = Assert.Single(candidates);
        Assert.False(candidate.CutValue);
        Assert.Equal(Message.Legacy("The disk said no…"), candidate.Message.Values["error"]);
    }

    [Fact]
    public void Parse_reads_no_cut_below_the_columns_limit()
    {
        //Arrange
        var text = Cut("There is no file at “/media/Movies/A very long folder name/Alien (1979).mkv”.", 40);

        //Act
        var candidates = Parser.Parse(text, text.Length + 1);

        //Assert
        Assert.Empty(candidates);
    }

    // ── never stopping the start ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_gives_up_on_a_text_that_reads_too_many_ways()
    {
        //Arrange
        // Three names and a number between single spaces, over 400 words: millions of splits, none of which reads.
        var catalog = TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/x.json", """{ "many": "{{a}} {{b}} {{c}} {{d, number}} end." }"""));
        var parser = new MessageParser(catalog, "{}");
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(index => $"w{index}")) + " end.";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        //Act
        var candidates = parser.Parse(text, ColumnLength);

        //Assert
        watch.Stop();
        Assert.Empty(candidates);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The parse took {watch.Elapsed}.");
    }

    [Theory]
    // Two sentences side by side: every way to group 333 sentences ties, each a tree of nested readings.
    [InlineData("""{ "pair": "{{a, message}} {{b, message}}", "s": "A." }""", "A.", false)]
    [InlineData("""{ "pair": "{{a, message}} {{b, message}}", "s": "A." }""", "A.", true)]
    // Sentences joined as a list, after two names that can end at any space.
    [InlineData("""{ "l": "{{a}} {{b}} {{names, list:and:message}}", "s": "A.", "t": "{{x}} and {{y}}." }""", "A.", false)]
    [InlineData("""{ "l": "{{a}} {{b}} {{names, list:and:message}}", "s": "A.", "t": "{{x}} and {{y}}." }""", "w", false)]
    // A clause after two names, and wordings that nest a sentence after every word.
    [InlineData("""{ "c": "{{a}} {{b}} {{r, message:clause}}, so nothing was sent.", "t": "{{x}}, so {{y}}.", "u": "{{x}} {{y, message}}" }""", "w", true)]
    [InlineData("""{ "c": "{{a}} {{b}} {{r, message:clause}}, so nothing was sent.", "t": "{{x}}, so {{y}}.", "u": "{{x}} {{y, message}}" }""", "A.", false)]
    // Numbers that can't be read between names: every place tried, none reading.
    [InlineData("""{ "n": "{{a}} {{b}} {{c, number}} {{d}}." }""", "w", false)]
    public void Parse_answers_any_1000_character_text_in_bounded_time(string wordings, string word, bool cut)
    {
        //Arrange
        // MAM-47 (WP4 review) — the first five took between 3 and 100 seconds each when the budget counted only the
        // template parts matched; every unit of work now counts, so a text of the column's length is answered in about
        // a third of a second at most, whatever its shape.
        var parser = new MessageParser(TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/x.json", wordings)), "{}");
        var words = string.Join(' ', Enumerable.Range(0, 600).Select(index => word == "w" ? $"w{index}" : word));
        var text = cut ? Cut(words, ColumnLength) : words[..(ColumnLength - 1)].TrimEnd() + ".";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        //Act
        var candidates = parser.Parse(text, cut ? text.Length : ColumnLength);
        watch.Stop();

        //Assert
        Assert.NotEqual(1, candidates.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The parse took {watch.Elapsed}.");
    }

    [Fact]
    public void Parse_reads_a_long_list_and_deep_nesting_on_a_small_stack()
    {
        //Arrange
        // MAM-47 (WP4 review) — a stack overflow is no exception a catch survives: at start it would stop the boot. A
        // list's items once nested a call each, and a wording that nests a sentence after each word nested once per word.
        var lists = new MessageParser(TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/x.json", """{ "ids": "Ids {{ids, list:comma:quoted}}." }""")), "{}");
        var nesting = new MessageParser(TemplateCatalog.From(("en/common.json", EnglishCommon), ("en/x.json", """{ "u": "{{x}} {{y, message}}" }""")), "{}");
        var list = "Ids " + string.Join(", ", Enumerable.Range(0, 500).Select(index => $"“{index}”")) + ".";
        var words = string.Join(' ', Enumerable.Range(0, 800).Select(index => $"w{index}"));
        IReadOnlyList<ParseCandidate> listed = [], nested = [];

        //Act
        var thread = new Thread(() => (listed, nested) = (lists.Parse(list, list.Length + 1), nesting.Parse(words, words.Length + 1)), 256 * 1024);
        thread.Start();
        thread.Join();

        //Assert
        Assert.True(list.Length is > 3000 and < 4000 && words.Length is > 3000 and < 4000);
        Assert.Equal(500, ((ListValue)Assert.Single(listed).Message.Values["ids"]).Items.Count);
        // Read whole, "w0 w1 … w799" splits at any space, ambiguous; or, past the budget, read no way.
        Assert.NotEqual(1, nested.Count);
    }

    [Theory]
    [InlineData(3990, 1)]
    [InlineData(4010, 0)]
    public void Parse_reads_no_text_longer_than_four_columns(int length, int readings)
    {
        //Arrange
        // MAM-47 (WP4 review) — a column keeps 1000 characters and the writers cut to them; a parse's time grows with the
        // text's length, so a text far longer is no sentence of the app's and is read no way.
        const string opening = "There is no file at “";
        var text = opening + new string('a', length - opening.Length - 2) + "”.";

        //Act
        var candidates = Parser.Parse(text, ColumnLength);

        //Assert
        Assert.Equal(length, text.Length);
        Assert.Equal(readings, candidates.Count);
    }

    [Fact]
    public void New_parser_refuses_older_wordings_that_arent_their_shape()
    {
        //Arrange
        string[] malformed = ["[]", """{ "stored.no-file": "No file." }""", """{ "stored.no-file": [7] }""", """{ "stored.no-file": ["No {{file"] }""", "{"];

        //Act
        var exceptions = malformed.Select(olderEnglish => Record.Exception(() => new MessageParser(Stored, olderEnglish))).ToList();

        //Assert
        Assert.All(exceptions, exception => Assert.IsType<InvalidDataException>(exception));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What today's cut helpers store for <paramref name="text"/> in a column of <paramref name="length"/> — the
    /// rule of <c>FileInspectionRunUpdates.Cut</c> and its siblings, copied: they go once their columns hold messages.</summary>
    private static string Cut(string text, int length) =>
        text.Length <= length ? text : string.Concat(text.AsSpan(0, length - 1), "…");

    /// <summary><paramref name="message"/> written with today's templates of the catalogue of shapes.</summary>
    private static string Written(Message message) => MessageRenderer.Render(Stored, message, TextLanguage.English, strict: true);

    private static IEnumerable<string> ProblemsOf(MessageParser parser, CorpusText entry)
    {
        var candidates = parser.Parse(entry.Text, entry.Length, entry.KeyPrefix);
        var read = candidates.Count == 0
            ? "nothing"
            : string.Join(" or ", candidates.Select(candidate => (candidate.CutValue ? "cut " : "") + MessageJson.Write(candidate.Message)));
        var where = $"“{entry.Text}” ({entry.Column}; {entry.Source})";
        switch (entry.Expect)
        {
            case "kept" when candidates.Count != 0:
                yield return $"{where} should be kept, but reads as {read}";
                break;
            case "ambiguous" when candidates.Count < 2:
                yield return $"{where} should read two ways, but reads as {read}";
                break;
            case "converted" or "cut-value":
                if (candidates is not [var only] || only.CutValue != (entry.Expect == "cut-value"))
                {
                    yield return $"{where} should read as one {(entry.Expect == "cut-value" ? "cut " : "")}message, but reads as {read}";
                    break;
                }

                if (HoldsOlderWords(only.Message))
                    yield return $"{where} reads with older words nested in it: {read}";
                var written = TestTexts.En(only.Message);
                if (entry.Expect == "converted" ? written != entry.Text : !written.StartsWith(entry.Text, StringComparison.Ordinal))
                    yield return $"{where} is written back as “{written}”";
                break;
        }
    }

    private static bool HoldsOlderWords(MessageValue value) => value switch
    {
        Message message => message.Key == Message.LegacyKey || message.Values.Values.Any(HoldsOlderWords),
        ListValue list => list.Items.Any(HoldsOlderWords),
        _ => false,
    };

    private static List<CorpusText> ReadCorpus()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(CorpusFile));
        return
        [
            .. document.RootElement.GetProperty("texts").EnumerateArray().Select(entry => new CorpusText(
                entry.GetProperty("text").GetString()!,
                entry.GetProperty("column").GetString()!,
                entry.GetProperty("source").GetString()!,
                entry.TryGetProperty("expect", out var expect) ? expect.GetString()! : "converted",
                entry.TryGetProperty("length", out var length) ? length.GetInt32() : ColumnLength,
                entry.TryGetProperty("keyPrefix", out var keyPrefix) ? keyPrefix.GetString() : null)),
        ];
    }

    /// <summary>One text of the corpus (see the file's "about").</summary>
    private sealed record CorpusText(
        string Text, string Column, string Source, string Expect, int Length, string? KeyPrefix);
}
