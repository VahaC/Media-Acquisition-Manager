using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042, design §6.9; WP11) — the startup converter on a copy of production, before the release: skipped unless
// MAM47_PRODUCTION_DB_COPY names the folder tools/mam47/copy_production_db.py wrote. The copy's mam.db is copied again into
// this test's own temporary file, so the conversion never touches it (its SHA-256 is checked), migrated as the app would,
// converted, and every row of every message column compared with what it held before: written back exactly, with a cut
// value, shortened, or kept as its words. The report — production text, so it never enters the repository — goes to
// MAM47_CONVERSION_REPORT, or beside the copy. It fails on what must not happen (a column that failed, a converted row
// that doesn't write back what was stored, a message the strict renderer can't write in Ukrainian); a kept row is the
// report's to list, for the older wordings of WP11.
public sealed class ProductionCopyConversionTests : DatabaseTestBase
{
    /// <summary>The folder holding the copy of production's <c>mam.db</c>.</summary>
    private const string CopyVariable = "MAM47_PRODUCTION_DB_COPY";

    /// <summary>Where the report goes; beside the copy when unset.</summary>
    private const string ReportVariable = "MAM47_CONVERSION_REPORT";

    private const string DatabaseFileName = "mam.db";
    private const string ReportFileName = "mam47-conversion-report.md";
    private const string AuditNames = "AuditEvents.TargetName";

    /// <summary>For <c>SkipUnless</c>: a copy of production is there to convert.</summary>
    public static bool HasCopy => Environment.GetEnvironmentVariable(CopyVariable) is { Length: > 0 } folder && File.Exists(Path.Combine(folder, DatabaseFileName));

    [Fact(Skip = "Set MAM47_PRODUCTION_DB_COPY to the folder of a copy of production's mam.db (tools/mam47/README.md).", SkipUnless = nameof(HasCopy))]
    public void Convert_on_a_copy_of_production_writes_back_every_row_it_converts_and_reports_the_rest()
    {
        //Arrange
        var folder = Environment.GetEnvironmentVariable(CopyVariable)!;
        var original = Path.Combine(folder, DatabaseFileName);
        var untouched = Fingerprint(original);
        File.Copy(original, DatabasePath, overwrite: true);
        // Committed pages a WAL beside the copy still holds are part of it; the shared-memory file never is.
        if (new FileInfo(original + "-wal") is { Exists: true, Length: > 0 })
            File.Copy(original + "-wal", DatabasePath + "-wal", overwrite: true);
        using var logs = new RecordingLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var db = CreateContext();
        db.Database.Migrate();
        var migration = db.Database.GetAppliedMigrations().LastOrDefault() ?? "(none)";
        var before = Read(db);

        //Act
        var result = new StoredTextConverter(db, loggers.CreateLogger<StoredTextConverter>()).Convert();

        //Assert
        var after = Read(db);
        var problems = new List<string>();
        var report = Report(original, migration, result, before, after, logs.Entries, problems);
        File.WriteAllText(ReportPath(folder), report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Assert.Equal(untouched, Fingerprint(original));
        Assert.True(problems.Count == 0, $"The conversion of the copy has problems (the report lists them):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // ── reading ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every text of every message column — neither null nor empty, which the converter leaves as they are — and
    /// every name of an owner's confirmation, by column and row.</summary>
    private static Dictionary<string, SortedDictionary<long, string>> Read(ApplicationDbContext db)
    {
        var columns = new Dictionary<string, SortedDictionary<long, string>>(StringComparer.Ordinal);
        var messageColumns = db.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties().Where(property => property.ClrType == typeof(Message)).Select(property => (Table: type.GetTableName()!, Column: property.GetColumnName())))
            .Distinct();
        foreach (var (table, column) in messageColumns)
        {
            var sql = $"SELECT rowid AS \"RowId\", \"{column}\" AS \"Text\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL AND \"{column}\" <> ''";
            columns[$"{table}.{column}"] = new SortedDictionary<long, string>(db.Database.SqlQueryRaw<StoredRow>(sql).AsEnumerable().ToDictionary(row => row.RowId, row => row.Text));
        }

        var names = "SELECT rowid AS \"RowId\", \"TargetName\" AS \"Text\" FROM \"AuditEvents\" WHERE \"Action\" = {0} AND \"TargetName\" IS NOT NULL";
        columns[AuditNames] = new SortedDictionary<long, string>(
            db.Database.SqlQueryRaw<StoredRow>(names, nameof(AuditAction.ReplacementWorkConfirmed)).AsEnumerable().ToDictionary(row => row.RowId, row => row.Text));
        return columns;
    }

    private static string Fingerprint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var file = new FileInfo(path);
        return $"{Convert.ToHexString(SHA256.HashData(stream))} {file.Length} {file.LastWriteTimeUtc:O}";
    }

    private static string ReportPath(string folder) =>
        Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } path ? path : Path.Combine(folder, ReportFileName);

    // ── the report ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string Report(
        string original,
        string migration,
        StoredTextConversionResult result,
        Dictionary<string, SortedDictionary<long, string>> before,
        Dictionary<string, SortedDictionary<long, string>> after,
        IReadOnlyList<RecordedLogEntry> logs,
        List<string> problems)
    {
        var outcomes = result.Columns.ToDictionary(column => $"{column.Table}.{column.Column}", column => column.Rows.ToDictionary(row => row.RowId), StringComparer.Ordinal);
        var lines = new List<Line>();
        foreach (var (column, rows) in before.OrderBy(column => column.Key, StringComparer.Ordinal))
        {
            foreach (var (rowId, stored) in rows)
            {
                var now = after[column].GetValueOrDefault(rowId);
                var outcome = outcomes.GetValueOrDefault(column)?.GetValueOrDefault(rowId);
                lines.Add(column == AuditNames ? AuditLine(column, rowId, stored, now, outcome) : MessageLine(column, rowId, stored, now, outcome, problems));
            }
        }

        foreach (var failed in result.Columns.Where(column => column.Failed))
            problems.Add($"{failed.Table}.{failed.Column}: the column's conversion failed (see the converter's log).");

        var report = new StringBuilder();
        report.AppendLine("# MAM-47 — the startup conversion on a copy of production")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Copy: `{original}` ({new FileInfo(original).Length / 1024.0 / 1024.0:F1} MiB, last migration after `Migrate()`: `{migration}`), converted in a temporary copy; the copy itself was not changed (SHA-256, size and time checked).")
            .AppendLine(CultureInfo.InvariantCulture, $"- Converter version {StoredTextConverter.Version}; written {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.")
            .AppendLine(CultureInfo.InvariantCulture, $"- Rows with text: {lines.Count}; untouched (already a message): {lines.Count(line => line.Kind == LineKind.AlreadyMessage)}; problems: {problems.Count}.")
            .AppendLine()
            .AppendLine("This file holds production's own words (paths, titles, services' answers): it stays in the build scratchpad, never in the repository.")
            .AppendLine();

        report.AppendLine("## Counts per column").AppendLine()
            .AppendLine("| column | rows with text | converted exactly | converted, cut value | shortened | kept: service's words | kept: older wording? | kept: cut at the limit | kept: ambiguous | audit: renamed | untouched | failed |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var column in before.Keys.Order(StringComparer.Ordinal))
        {
            var of = lines.Where(line => line.Column == column).ToList();
            var failed = result.Columns.Any(result => $"{result.Table}.{result.Column}" == column && result.Failed) ? "yes" : "";
            report.AppendLine(CultureInfo.InvariantCulture, $"| {column} | {of.Count} | {Count(of, LineKind.Exact)} | {Count(of, LineKind.CutValue)} | {of.Count(line => line.Shortened)} | {Count(of, LineKind.ServiceWords)} | {Count(of, LineKind.OlderWording)} | {Count(of, LineKind.CutInWords)} | {Count(of, LineKind.Ambiguous)} | {of.Count(line => line.Kind == LineKind.AuditName && line.Note?.StartsWith('→') == true)} | {Count(of, LineKind.AlreadyMessage)} | {failed} |");
        }

        report.AppendLine().AppendLine("## Counts per key").AppendLine().AppendLine("| key | rows |").AppendLine("|---|---:|");
        foreach (var group in lines.Where(line => line.Key is not null).GroupBy(line => line.Key!).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal))
            report.AppendLine(CultureInfo.InvariantCulture, $"| `{group.Key}` | {group.Count()} |");

        Section(report, "Kept: a service's own words", "No wording of the app reads them; they stay as they were, in every language.", lines, LineKind.ServiceWords);
        Section(report, "Kept: an app sentence of an older wording?", "They open or close like an app sentence (the nearest key is named): candidates for `Locales/older-en.json`, from evidence (`git log -S`).", lines, LineKind.OlderWording);
        Section(report, "Kept: an app sentence cut at the column limit", "Cut by an older build in its words, a number or a list: no reading takes the cut.", lines, LineKind.CutInWords);
        Section(report, "Kept: ambiguous", "Two readings or more that differ in key or values.", lines, LineKind.Ambiguous);
        Section(report, "Converted with a cut value", "Cut by an older build inside its last value; the English now ends with the template's words after the cut.", lines, LineKind.CutValue);
        Section(report, "Shortened to fit", "Read back exactly, but longer than the column as JSON: the longest value was cut.", [.. lines.Where(line => line.Shortened)], kind: null);
        Section(report, "Audit names of the owner's confirmations", "`ReplacementWorkConfirmed` rows that named the sentence: renamed by the claim's label, or left as they were.", lines, LineKind.AuditName);
        Section(report, "Converted around an older build's words", "The outer sentence read back, a nested one only as its words: candidates for `Locales/older-en.json` too.", [.. lines.Where(line => line.OlderWords)], kind: null);
        Section(report, "Converted exactly", "Written back with today's English, byte for byte.", lines, LineKind.Exact);

        report.AppendLine().AppendLine("## Problems (must be none)").AppendLine();
        report.AppendLine(problems.Count == 0 ? "None." : string.Join(Environment.NewLine, problems.Select(problem => $"- {Cell(problem)}")));

        report.AppendLine().AppendLine("## The converter's log").AppendLine();
        foreach (var entry in logs)
            report.AppendLine(CultureInfo.InvariantCulture, $"- {entry.Level}: {Cell(entry.Message)}{(entry.Exception is null ? "" : $" ({entry.Exception.GetType().Name}: {Cell(entry.Exception.Message)})")}");
        return report.ToString();
    }

    private static Line MessageLine(string column, long rowId, string stored, string? now, StoredTextRowResult? outcome, List<string> problems)
    {
        if (stored.StartsWith("{\"k\":\"", StringComparison.Ordinal) && MessageJson.TryRead(stored) is { } message && message.Key != Message.LegacyKey)
            return new Line(column, rowId, stored, LineKind.AlreadyMessage, message.Key, null, Shortened: false);

        if (MessageJson.TryRead(now) is not { } converted)
        {
            problems.Add($"{column} row {rowId}: the column holds no message after the conversion.");
            return new Line(column, rowId, stored, LineKind.ServiceWords, null, "(no message)", Shortened: false);
        }

        var words = converted.Key == Message.LegacyKey ? null : converted;
        string? uk = null;
        try
        {
            uk = MessageRenderer.Render(converted, TextLanguage.Ukrainian, strict: true);
        }
        catch (MissingTextException exception)
        {
            problems.Add($"{column} row {rowId}: the strict renderer can't write it in Ukrainian ({exception.Message}).");
        }

        if (words is null)
        {
            // A text no wording reads keeps its words to the character (MAM-47 WP10 review): what the reader sees is unchanged.
            if (converted.Values.GetValueOrDefault(Message.LegacyTextName) is not TextValue { Text: var kept } || !string.Equals(kept, LegacyWordsOf(stored), StringComparison.Ordinal))
                problems.Add($"{column} row {rowId}: kept, but not as the words it held.");
            var kind = outcome?.Outcome switch
            {
                ConversionOutcome.KeptCut => LineKind.CutInWords,
                ConversionOutcome.KeptAmbiguous => LineKind.Ambiguous,
                _ => NearestKey(stored) is null ? LineKind.ServiceWords : LineKind.OlderWording,
            };
            return new Line(column, rowId, stored, kind, null, kind == LineKind.OlderWording ? $"like `{NearestKey(stored)}`" : null, Shortened: false);
        }

        var english = MessageRenderer.Render(words, TextLanguage.English, strict: true);
        var shortened = outcome?.Shortened == true;
        var cut = outcome?.Outcome == ConversionOutcome.ConvertedWithCutValue;
        // Shortened: the longest value was cut again to fit, so only the words before that cut are the stored ones.
        var writesBack = shortened || (cut ? english.StartsWith(stored, StringComparison.Ordinal) : string.Equals(english, stored, StringComparison.Ordinal));
        if (!writesBack)
            problems.Add($"{column} row {rowId}: `{words.Key}` doesn't write back what was stored.");
        return new Line(column, rowId, stored, cut ? LineKind.CutValue : LineKind.Exact, words.Key, shortened || cut ? english : uk, shortened, HoldsOlderWords(words));
    }

    // The words a row held: its text, or, for a row an earlier start kept, the words inside it.
    private static string LegacyWordsOf(string stored) =>
        MessageJson.TryRead(stored) is { Key: Message.LegacyKey } kept && kept.Values.GetValueOrDefault(Message.LegacyTextName) is TextValue words
            ? words.Text
            : stored;

    private static Line AuditLine(string column, long rowId, string stored, string? now, StoredTextRowResult? outcome) =>
        new(column, rowId, stored, LineKind.AuditName, null, string.Equals(stored, now, StringComparison.Ordinal) ? $"left as it was ({outcome?.Outcome})" : $"→ {now}", Shortened: false);

    /// <summary>
    /// The key whose English opens or closes like <paramref name="stored"/> — at least 12 characters of a template's own
    /// words at the start or the end — or <c>null</c>: a hint for the report only, the reader decides.
    /// </summary>
    private static string? NearestKey(string stored)
    {
        string? nearest = null;
        var longest = 11;
        foreach (var key in TemplateCatalog.Embedded.Keys(TextLanguage.English))
        {
            if (!TemplateCatalog.Embedded.TryFind(TextLanguage.English, key, out var template) || key.StartsWith(Message.LegacyKey, StringComparison.Ordinal))
                continue;
            var opening = template.Parts.FirstOrDefault() is TemplateLiteral first ? first.Text : "";
            var closing = template.Parts.LastOrDefault() is TemplateLiteral last ? last.Text : "";
            var matched = Math.Max(
                stored.StartsWith(opening, StringComparison.Ordinal) ? opening.Length : 0,
                stored.EndsWith(closing, StringComparison.Ordinal) ? closing.Length : 0);
            if (matched > longest)
                (nearest, longest) = (key, matched);
        }

        return nearest;
    }

    private static void Section(StringBuilder report, string title, string about, List<Line> lines, LineKind? kind)
    {
        var of = kind is null ? lines : [.. lines.Where(line => line.Kind == kind)];
        report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {title} ({of.Count})").AppendLine().AppendLine(about).AppendLine();
        if (of.Count == 0)
        {
            report.AppendLine("None.");
            return;
        }

        report.AppendLine("| column | rowid | stored | key | now / note |").AppendLine("|---|---:|---|---|---|");
        foreach (var line in of)
            report.AppendLine(CultureInfo.InvariantCulture, $"| {line.Column} | {line.RowId} | {Cell(line.Stored)} | {(line.Key is null ? "" : $"`{line.Key}`")} | {Cell(line.Note ?? "")} |");
    }

    private static int Count(List<Line> lines, LineKind kind) => lines.Count(line => line.Kind == kind);

    // A nested sentence no wording read, kept as its words inside a sentence that was (the parser's last resort).
    private static bool HoldsOlderWords(MessageValue value) => value switch
    {
        Message { Key: Message.LegacyKey } => true,
        Message message => message.Values.Values.Any(HoldsOlderWords),
        ListValue list => list.Items.Any(HoldsOlderWords),
        _ => false,
    };

    // A table cell: one line, with its bars escaped.
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>A row as the query read it.</summary>
    private sealed record StoredRow(long RowId, string Text);

    /// <summary>One stored text in the report.</summary>
    private sealed record Line(string Column, long RowId, string Stored, LineKind Kind, string? Key, string? Note, bool Shortened, bool OlderWords = false);

    private enum LineKind
    {
        Exact = 1,
        CutValue = 2,
        ServiceWords = 3,
        OlderWording = 4,
        CutInWords = 5,
        Ambiguous = 6,
        AuditName = 7,
        AlreadyMessage = 8,
    }
}
