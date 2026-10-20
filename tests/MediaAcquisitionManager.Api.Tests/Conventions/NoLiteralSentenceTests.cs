using System.Reflection;
using System.Runtime.CompilerServices;
using MediaAcquisitionManager.Api.Controllers;
using MediaAcquisitionManager.Api.Mcp;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — every sentence a person reads comes from the server's locale files: no text written in the IL of
/// the Api, Core or Infrastructure reads like a sentence unless it goes to a log or an exception (English for the
/// operator and the developer), and no <c>const string</c> sentence waits in a type for a reader or for nobody. Whatever
/// the detector says, a literal never becomes a message's value, a message is never printed into a string nor compared
/// with one, and stored vendor words are made only by the JSON codec and the stored-text parser.
/// </summary>
public sealed class NoLiteralSentenceTests
{
    /// <summary>
    /// The methods the first scan read (WP1, 2026-10-19: 27,518 — Api 19,424, Core 5,948, Infrastructure 2,146), less a
    /// margin, so that deleting dead sentences and the conversion's shims doesn't trip it. The guard of the guard: a scan
    /// that silently stopped reading most of the code would pass everything.
    /// </summary>
    private const int MinimumMethodCount = 25_000;

    private static readonly Assembly[] Scanned =
    [
        typeof(UnknownApiController).Assembly,
        typeof(Message).Assembly,
        typeof(ApplicationDbContext).Assembly,
    ];

    /// <summary>Types whose sentences stay English on purpose, each with why.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.Ordinal)
    {
        [typeof(McpInstructions).FullName!] = "AI-facing: the MCP server's instructions to an assistant stay English (ADR 0042)",
        [typeof(McpServerIdentity).FullName!] = "AI-facing: the MCP server's name and description stay English (ADR 0042)",
        ["MediaAcquisitionManager.Api.Http.SecurityHeadersMiddleware"] = "HTTP header values (the CSP), not words for a person",
    };

    /// <summary>Literals allowed into a message's text value, by exact text (a hole written {}), each with why.</summary>
    private static readonly Dictionary<string, string> AllowedTextValueLiterals = new(StringComparer.Ordinal)
    {
        ["…"] = "the fit marker that ends a shortened list (design §5.3)",
        ["{}…"] = "the fit marker that ends a cut value (design §5.3)",
        [""] = "no words: Message.Redacted's fallback for a value redaction left null",
        ["{} ({})"] = "a work's title and year, as the Library names a work in every language (ReplacementPairingWork.NameOf)",
    };

    /// <summary>The only types that may make <see cref="VendorText"/> from stored text.</summary>
    private static readonly HashSet<string> StoredVendorTextReaders = new(StringComparer.Ordinal)
    {
        typeof(MessageJson).FullName!,
        "MediaAcquisitionManager.Api.Texts.Conversion.MessageParser",
    };

    private static readonly Lazy<IlScan[]> Scans = new(() => [.. Scanned.Select(IlLiteralScanner.Scan)]);

    [Fact]
    public void No_literal_sentence_reaches_a_reader()
    {
        //Arrange
        var scans = Scans.Value;

        //Act
        var offenders = scans
            .SelectMany(scan => scan.Uses)
            .Where(IsSentenceForAReader)
            .Select(use => use.ToString())
            .Distinct()
            .ToList();

        //Assert
        // Joined rather than Assert.Empty, which cuts each item short.
        Assert.True(
            offenders.Count == 0,
            $"Sentences written in code that reach a reader — move each into the locale files (design §10.1 recipe):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void No_constant_sentence_waits_for_a_reader()
    {
        //Arrange
        var uses = Scans.Value.SelectMany(scan => scan.Uses).ToLookup(use => use.Text, StringComparer.Ordinal);

        //Act
        var offenders = Scans.Value
            .SelectMany(scan => scan.Constants)
            .Where(constant => IsConstantSentenceForAReader(constant, uses))
            .Select(constant => constant.ToString())
            .Distinct()
            .ToList();

        //Assert
        Assert.True(
            offenders.Count == 0,
            $"Sentences kept in constants that reach a reader, or that nothing reads any more — move each into the locale files (a const string becomes a static readonly Message under the same name, design §1.3), or delete a dead one:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void No_literal_becomes_a_messages_value()
    {
        //Arrange
        var scans = Scans.Value;

        //Act
        var offenders = scans
            .SelectMany(scan => scan.Uses)
            .Where(IntoATextValue)
            .Where(use => !AllowedTextValueLiterals.ContainsKey(use.Text))
            .Select(use => use.ToString())
            .ToList();

        //Assert
        Assert.True(
            offenders.Count == 0,
            $"Literals made a message's value — words of ours go into a template, a value holds data:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void No_message_is_printed_into_a_string()
    {
        //Arrange
        var scans = Scans.Value;

        //Act
        var offenders = scans
            .SelectMany(scan => scan.Calls.Where(PrintsAMessage).Concat(scan.Printed))
            .Select(call => $"{call.Owner.FullName}.{call.Method.Name} → {call.Callee.DeclaringType?.Name}.{call.Callee.Name}")
            .Distinct()
            .ToList();

        //Assert
        Assert.True(
            offenders.Count == 0,
            $"A message interpolated, concatenated, formatted, joined, appended or logged into a string prints its JSON, not a sentence — render it (texts.Render / texts.English):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void No_message_is_compared_with_a_string()
    {
        //Arrange
        // MAM-47 WP12 — the tests too: a test's row.Error == "…" compiles and is always false, so the check it meant never
        // ran. Here the source assemblies, this project and the harness; Core.Tests and Infrastructure.Tests
        // scan their own (NoMessageComparedWithAStringTests). The probe compares on purpose, for the guard test below.
        IlScan[] scans =
        [
            .. Scans.Value,
            IlLiteralScanner.Scan(typeof(NoLiteralSentenceTests).Assembly),
            IlLiteralScanner.Scan(typeof(ProbeSentences).Assembly),
        ];

        //Act
        var offenders = MessageComparisons.WithAStringIn(scans, typeof(ProbeSentences));

        //Assert
        Assert.True(
            offenders.Count == 0,
            $"A message compared with == or != against a string meets it as a text value through the implicit conversion, so it is always unequal — compare keys (message.Key == …), whole messages with Equals, or test for null with is null (design §5.4):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Only_the_json_codec_and_the_stored_text_parser_make_stored_vendor_words()
    {
        //Arrange
        var scans = Scans.Value;

        //Act
        var offenders = scans
            .SelectMany(scan => scan.Calls)
            .Where(MakesStoredVendorText)
            .Where(call => !StoredVendorTextReaders.Contains(call.Owner.FullName!))
            .Select(call => $"{call.Owner.FullName}.{call.Method.Name}")
            .Distinct()
            .ToList();

        //Assert
        Assert.True(
            offenders.Count == 0,
            $"VendorText.Stored skips the masking VendorText.Clean does; only text read back from the database may use it:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void The_scan_covers_the_three_assemblies_and_loses_track_nowhere()
    {
        //Arrange
        Type[] mustBeScanned =
        [
            typeof(UnknownApiController), typeof(MessageRenderer), typeof(Message), typeof(ApplicationDbContext),
        ];

        //Act
        var scans = Scans.Value;

        //Assert
        var methodCount = scans.Sum(scan => scan.MethodCount);
        Assert.True(methodCount >= MinimumMethodCount, $"Only {methodCount} methods were scanned.");
        Assert.Subset(scans.SelectMany(scan => scan.Types).ToHashSet(), mustBeScanned.ToHashSet());
        Assert.Empty(scans.SelectMany(scan => scan.Unresolved));
    }

    [Fact]
    public void The_scan_flags_the_probes_sentences_and_spares_its_log_and_exception()
    {
        //Arrange
        var probe = IlLiteralScanner.Scan([typeof(ProbeSentences)]);

        //Act
        var flagged = probe.Uses.Where(IsSentenceForAReader).Select(use => use.Text).ToHashSet(StringComparer.Ordinal);
        var uses = probe.Uses.ToLookup(use => use.Text, StringComparer.Ordinal);
        var constants = probe.Constants
            .Where(constant => IsConstantSentenceForAReader(constant, uses))
            .Select(constant => constant.Field.Name)
            .ToHashSet(StringComparer.Ordinal);
        var intoTextValues = probe.Uses.Where(IntoATextValue).Select(use => use.Text).ToList();
        var printers = probe.Calls.Where(PrintsAMessage).Concat(probe.Printed).Select(call => call.Method.Name).ToHashSet(StringComparer.Ordinal);
        var storers = probe.Calls.Where(MakesStoredVendorText).Select(call => call.Method.Name).ToList();
        var comparers = probe.Calls.Where(MessageComparisons.WithAString).Select(call => call.Method.Name).ToHashSet(StringComparer.Ordinal);

        //Assert
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                ProbeSentences.Returned, ProbeSentences.Interpolated, ProbeSentences.FirstArm, ProbeSentences.SecondArm,
            },
            flagged);
        // Logged and Thrown are read only by a log and an exception: spared; Unread is read by nothing: a dead sentence.
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(ProbeSentences.Returned), nameof(ProbeSentences.Interpolated), nameof(ProbeSentences.FirstArm),
                nameof(ProbeSentences.SecondArm), nameof(ProbeSentences.Unread),
            },
            constants);
        Assert.Equal([ProbeSentences.IntoATextValue], intoTextValues);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(ProbeSentences.Printed), nameof(ProbeSentences.Glued), nameof(ProbeSentences.Formatted),
                nameof(ProbeSentences.Appended), nameof(ProbeSentences.Joined), nameof(ProbeSentences.LoggedMessage),
            },
            printers);
        Assert.Equal([nameof(ProbeSentences.Stored)], storers);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { nameof(ProbeSentences.Compared), nameof(ProbeSentences.Differs) },
            comparers);
    }

    [Theory]
    [InlineData("The job is gone.", true)]
    [InlineData("Couldn't reach {} in time.", true)]
    [InlineData("Is the folder still there?", true)]
    [InlineData("Pick one of these:", true)]
    [InlineData("“{}”", true)]
    [InlineData("«Повторити»", true)]
    [InlineData("Не вдалося", true)]
    [InlineData("Two words.", false)]
    [InlineData("{} couldn't {}", false)]
    [InlineData("GET POST PUT.", false)]
    [InlineData("application/json", false)]
    [InlineData("common.request.unknown-endpoint", false)]
    [InlineData("PRAGMA foreign_keys = OFF;", false)]
    [InlineData("yyyy-MM-dd HH:mm:ss", false)]
    [InlineData("…", false)]
    [InlineData("", false)]
    public void The_detector_flags_words_for_a_person_and_spares_code(string text, bool expected)
    {
        //Arrange

        //Act
        var flagged = SentenceDetector.IsSentence(text);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the rules ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A sentence that goes somewhere a person reads it: anywhere but a log, an exception, an authentication
    /// failure (logged by the framework) or raw SQL, outside the types allowed to keep English and generated code.</summary>
    private static bool IsSentenceForAReader(LiteralUse use) =>
        SentenceDetector.IsSentence(use.Text)
        && !IsAllowedConsumer(use.Callee)
        && !AllowedTypes.ContainsKey(use.Owner.FullName!)
        && !IsGenerated(use.Owner);

    /// <summary>
    /// A constant sentence that goes somewhere a person reads it: one whose text some use hands to a reader, or that no
    /// IL reads at all (a dead sentence, or one only an attribute names); spared when every use of its text is a log, an
    /// exception or another allowed consumer, and in the types allowed to keep English and generated code.
    /// </summary>
    private static bool IsConstantSentenceForAReader(IlConstant constant, ILookup<string, LiteralUse> uses) =>
        SentenceDetector.IsSentence(constant.Text)
        && !AllowedTypes.ContainsKey(constant.Owner.FullName!)
        && !IsGenerated(constant.Owner)
        && !(uses[constant.Text].Any() && uses[constant.Text].All(use => IsAllowedConsumer(use.Callee)));

    /// <summary>EF's migrations, and the regex source generator's matchers (a pattern holding “ or Cyrillic is code).</summary>
    private static bool IsGenerated(Type owner) =>
        (owner.Namespace ?? "") is var name
        && (name.Contains(".Migrations", StringComparison.Ordinal)
            || name.StartsWith("System.Text.RegularExpressions.Generated", StringComparison.Ordinal));

    private static bool IsAllowedConsumer(MethodBase? callee) => callee switch
    {
        ConstructorInfo constructor => typeof(Exception).IsAssignableFrom(constructor.DeclaringType),
        { DeclaringType: var type } when type == typeof(LoggerExtensions) => true,
        { DeclaringType: var type, Name: "Log" or "BeginScope" } when type == typeof(ILogger) => true,
        { DeclaringType: var type, Name: "Fail" } when type == typeof(AuthenticateResult) => true,
        { DeclaringType: { IsGenericType: true } type, Name: "Fail" } when type.GetGenericTypeDefinition() == typeof(ResultContext<>) => true,
        { DeclaringType: var type, Name: var name } when type == typeof(RelationalDatabaseFacadeExtensions)
            && (name.StartsWith("ExecuteSqlRaw", StringComparison.Ordinal) || name.StartsWith("SqlQueryRaw", StringComparison.Ordinal)) => true,
        _ => false,
    };

    private static bool IntoATextValue(LiteralUse use) => use.Callee switch
    {
        ConstructorInfo constructor => constructor.DeclaringType == typeof(TextValue),
        MethodInfo { Name: "op_Implicit" } method => method.DeclaringType == typeof(MessageValue)
            && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(string),
        _ => false,
    };

    private static bool PrintsAMessage(IlCall call) =>
        call.Callee is MethodInfo { Name: "AppendFormatted", IsGenericMethod: true } method
        && method.DeclaringType == typeof(DefaultInterpolatedStringHandler)
        && typeof(MessageValue).IsAssignableFrom(method.GetGenericArguments()[0]);

    private static bool MakesStoredVendorText(IlCall call) =>
        call.Callee is MethodInfo { Name: nameof(VendorText.Stored) } method && method.DeclaringType == typeof(VendorText);
}
