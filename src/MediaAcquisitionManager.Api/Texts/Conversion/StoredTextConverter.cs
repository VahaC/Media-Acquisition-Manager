using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>
/// MAM-47 (ADR 0042, R22, design §6) — turns the English an older build stored in the message columns into key + values,
/// at start, after <c>Migrate()</c> and before the reconcilers: in C#, because a migration holds only EF's own operations.
/// A text it can't read back keeps its words (read as <see cref="Message.LegacyKey"/>), and it never stops the boot.
/// </summary>
/// <remarks>
/// <para>
/// Finds the columns from the model — every entity property typed <see cref="Message"/> — rather than from a hand list
/// that could fall behind the entities (MessageColumnsTests pins the set). Each is read in keyset pages by
/// <c>rowid</c>; a page is converted in memory and written in one transaction, each row only if it still holds what was
/// read, so a row the app changed meanwhile is left for the next start.
/// </para>
/// <para>
/// Idempotent by content: a message the app wrote starts with <c>{"k":"</c>, which English never does, so a converted
/// row is never read as a candidate again; a row kept as its words is marked with <see cref="Version"/> and tried again
/// only by a build whose version is higher. An older image's English after a rollback is converted at the next start.
/// </para>
/// <para>
/// Q1 (Vasyl, 2026-10-19) — the audit rows of the owner's confirmations (<c>ReplacementWorkConfirmed</c>) named the
/// release by the sentence he ticked; their name is a column of names, not a message column, so it is rewritten to the
/// claim's label in no language (<see cref="ReplacementConfirmationLabel"/>), the one the scheduling writes today, and an
/// old row and a new one read alike. One it can't read back is left as it is and reported.
/// </para>
/// </remarks>
public sealed class StoredTextConverter(ApplicationDbContext db, ILogger<StoredTextConverter> logger)
{
    /// <summary>
    /// The version a kept row is marked with; a later build that learns an older wording raises it, and the rows kept
    /// under a lower one are tried again. Per row, like <c>CatalogStore.CurrentDetailsVersion</c>, so no table and no
    /// <c>PRAGMA</c>.
    /// </summary>
    public const int Version = 1;

    /// <summary>The rows read and written at a time: a page is one short transaction, so a large library's column never
    /// holds SQLite's write lock for long.</summary>
    internal const int PageSize = 500;

    /// <summary>What every message the column converter writes starts with, and English never does.</summary>
    private const string MessageOpening = "{\"k\":\"";

    /// <summary>
    /// The keys the owner's consent words are read with: they end with no full stop, so the parser takes them only when
    /// it is told to (WP4 hand-over, deviation 1).
    /// </summary>
    private const string ConfirmationKeyPrefix = "replacements.confirm.";

    /// <summary>The wording whose opening words every consent sentence shares, current or older: what selects the audit
    /// rows that still name a sentence.</summary>
    private const string ConfirmationWorkKey = "replacements.confirm.work";

    private const string Ellipsis = "…";

    /// <summary>What a row kept as its words starts with.</summary>
    private static readonly string KeptOpening = MessageOpening + Message.LegacyKey + "\"";

    /// <summary>Walks every message column, then the audit rows' names of the owner's confirmations; never throws, so a
    /// failure leaves the rows to the tolerant column reader and the next start.</summary>
    public StoredTextConversionResult Convert()
    {
        var columns = new List<StoredTextColumnResult>();
        try
        {
            foreach (var column in MessageColumns())
                columns.Add(Walk(column));
            if (ConfirmationNames() is { } names)
                columns.Add(Walk(names));
        }
        catch (Exception exception)
        {
            // Error, so it ships off the box: stored sentences stay English until a start converts them.
            logger.LogError(exception, "The conversion of stored sentences failed; they are shown as stored until the next start.");
        }

        return new StoredTextConversionResult(columns);
    }

    // ── the columns ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private List<Column> MessageColumns()
    {
        var columns = new Dictionary<(string Table, string Column), Column>();
        foreach (var type in db.Model.GetEntityTypes())
        {
            if (type.GetTableName() is not { } table)
                continue;
            foreach (var property in type.GetProperties().Where(property => property.ClrType == typeof(Message)))
            {
                var name = property.GetColumnName();
                columns.TryAdd(
                    (table, name),
                    new Column(table, name, property.GetMaxLength() ?? MessageJson.ColumnLength, KeyPrefixOf(type, property), Names: null));
            }
        }

        return [.. columns.Values.OrderBy(column => column.Table, StringComparer.Ordinal).ThenBy(column => column.Name, StringComparer.Ordinal)];
    }

    // Only the consent words end with no full stop; every other column holds whole sentences, and reading those with every
    // key would read a service's words as a fragment ("{{title}} ({{year}})").
    private static string? KeyPrefixOf(IEntityType type, IProperty property) =>
        type.ClrType == typeof(ReplacementJobWorkEntity) && property.Name == nameof(ReplacementJobWorkEntity.ConfirmationText)
            ? ConfirmationKeyPrefix
            : null;

    private Column? ConfirmationNames()
    {
        var type = db.Model.FindEntityType(typeof(AuditEventEntity));
        var name = type?.FindProperty(nameof(AuditEventEntity.TargetName));
        var action = type?.FindProperty(nameof(AuditEventEntity.Action));
        if (type?.GetTableName() is not { } table || name is null || action is null || OpeningOfTheConsentWords() is not { } opening)
            return null;

        // The action as the column keeps it (its name, through the model's converter), not as the enum's number.
        var confirmed = action.GetTypeMapping().Converter?.ConvertToProvider(AuditAction.ReplacementWorkConfirmed) ?? AuditAction.ReplacementWorkConfirmed;
        return new Column(
            table,
            name.GetColumnName(),
            name.GetMaxLength() ?? AuditTarget.TargetNameMaximumLength,
            ConfirmationKeyPrefix,
            new NameFilter(action.GetColumnName(), confirmed, opening));
    }

    private static string? OpeningOfTheConsentWords() =>
        TemplateCatalog.Embedded.TryFind(TextLanguage.English, ConfirmationWorkKey, out var template)
        && template.Parts.FirstOrDefault() is TemplateLiteral { Text.Length: > 0 } opening
            ? opening.Text
            : null;

    // ── one column ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private StoredTextColumnResult Walk(Column column)
    {
        var rows = new List<StoredTextRowResult>();
        try
        {
            var last = long.MinValue;
            while (true)
            {
                var page = Page(column, last);
                if (page.Count == 0)
                    break;
                last = page[^1].RowId;
                Write(column, [.. page.Select(row => ConvertRow(column, row)).OfType<Change>()], rows);
                if (page.Count < PageSize)
                    break;
            }

            Log(column, rows);
            return new StoredTextColumnResult(column.Table, column.Name, rows);
        }
        catch (Exception exception)
        {
            // Names the column, never a text: the rows hold paths and a service's words.
            logger.LogError(
                exception,
                "The conversion of the stored texts of {Table}.{Column} failed; they are shown as stored until the next start.",
                column.Table,
                column.Name);
            return new StoredTextColumnResult(column.Table, column.Name, rows, Failed: true);
        }
    }

    private List<StoredText> Page(Column column, long last)
    {
        // The names are the model's, quoted (Quote); every value is a parameter.
        var sql = SelectSql(column);
        return column.Names is { } names
            ? [.. db.Database.SqlQueryRaw<StoredText>(sql, last, names.Action, names.Opening.Length, names.Opening, PageSize)]
            : [.. db.Database.SqlQueryRaw<StoredText>(sql, last, MessageOpening.Length, MessageOpening, KeptOpening.Length, KeptOpening, PageSize)];
    }

    private static string SelectSql(Column column)
    {
        var table = Quote(column.Table);
        var text = Quote(column.Name);
        if (column.Names is { } names)
        {
            return $"SELECT rowid AS \"RowId\", {text} AS \"Text\" FROM {table} WHERE rowid > {{0}} AND {Quote(names.ActionColumn)} = {{1}} " +
                $"AND substr({text}, 1, {{2}}) = {{3}} ORDER BY rowid LIMIT {{4}}";
        }

        // English never starts like a message; a row kept as its words does, and is read to see whether its mark is older.
        // An empty text holds no words to read or keep: it stays as it is and reads as before (MAM-47 WP10 review).
        return $"SELECT rowid AS \"RowId\", {text} AS \"Text\" FROM {table} WHERE rowid > {{0}} AND {text} IS NOT NULL AND {text} <> '' " +
            $"AND (substr({text}, 1, {{1}}) <> {{2}} OR substr({text}, 1, {{3}}) = {{4}}) ORDER BY rowid LIMIT {{5}}";
    }

    private Change? ConvertRow(Column column, StoredText row)
    {
        if (column.Names is not null)
            return Relabel(column, row);

        var words = row.Text;
        if (words.StartsWith(MessageOpening, StringComparison.Ordinal))
        {
            // Kept by an earlier start: tried again only when this build knows more wordings than the one that kept it.
            if (MessageJson.TryRead(words) is not { Key: Message.LegacyKey } kept
                || kept.Values.GetValueOrDefault(Message.LegacyTextName) is not TextValue keptWords
                || (MessageJson.KeptVersionOf(words) ?? 0) >= Version)
                return null;
            words = keptWords.Text;
        }

        var readings = MessageParser.Embedded.Parse(words, column.Length, column.KeyPrefix);
        if (readings.Count == 1)
        {
            var reading = readings[0];
            // Fitted as the column converter fits what the app writes: the reading is the old words, which an older build
            // cut to the column, but as JSON with its key it can be longer.
            var fitted = MessageFit.Fit(reading.Message, MessageJson.ColumnLength);
            var outcome = reading.CutValue ? ConversionOutcome.ConvertedWithCutValue : ConversionOutcome.Converted;
            return new Change(row, MessageJson.Write(fitted), new StoredTextRowResult(row.RowId, outcome, !fitted.Equals(reading.Message)));
        }

        return new Change(row, MessageJson.WriteKept(words, Version), new StoredTextRowResult(row.RowId, KeptOutcome(readings.Count, words, column.Length)));
    }

    private static Change Relabel(Column column, StoredText row)
    {
        var readings = MessageParser.Embedded.Parse(row.Text, column.Length, column.KeyPrefix);
        if (readings.Count != 1)
            // A name the consent words don't read back is no message: it stays, and is reported at every start.
            return new Change(row, NewText: null, new StoredTextRowResult(row.RowId, KeptOutcome(readings.Count, row.Text, column.Length)));

        var reading = readings[0];
        var outcome = reading.CutValue ? ConversionOutcome.ConvertedWithCutValue : ConversionOutcome.Converted;
        return new Change(row, AuditTarget.Cut(ReplacementConfirmationLabel.Of(reading.Message)), new StoredTextRowResult(row.RowId, outcome));
    }

    private static ConversionOutcome KeptOutcome(int readings, string words, int columnLength) =>
        readings > 1 ? ConversionOutcome.KeptAmbiguous
        : words.Length == columnLength && words.EndsWith(Ellipsis, StringComparison.Ordinal) ? ConversionOutcome.KeptCut
        : ConversionOutcome.Kept;

    private void Write(Column column, List<Change> changes, List<StoredTextRowResult> rows)
    {
        var writes = changes.Where(change => change.NewText is not null && !string.Equals(change.NewText, change.Row.Text, StringComparison.Ordinal)).ToList();
        var written = new HashSet<long>();
        if (writes.Count > 0)
        {
            var sql = $"UPDATE {Quote(column.Table)} SET {Quote(column.Name)} = {{0}} WHERE rowid = {{1}} AND {Quote(column.Name)} = {{2}}";
            using var transaction = db.Database.BeginTransaction();
            foreach (var change in writes)
            {
                // A row the app changed since it was read is left as the app wrote it; the next start reads it again.
                if (db.Database.ExecuteSqlRaw(sql, change.NewText!, change.Row.RowId, change.Row.Text) == 1)
                    written.Add(change.Row.RowId);
            }

            transaction.Commit();
        }

        // In page order: what was written, and what was only reported (an audit name that stays as it is).
        rows.AddRange(changes.Where(change => change.NewText is null || written.Contains(change.Row.RowId)).Select(change => change.Result));
    }

    private void Log(Column column, List<StoredTextRowResult> rows)
    {
        if (rows.Count == 0)
            return;

        var result = new StoredTextColumnResult(column.Table, column.Name, rows);
        // Information: counts and row ids only, never a text — the rows hold paths and a service's words. The report for
        // Vasyl comes from the production-copy run, not from here.
        if (column.Names is null)
        {
            logger.LogInformation(
                "Converted {Converted} stored texts of {Table}.{Column}; {CutValue} with a cut value, {Shortened} shortened to fit, {Kept} kept as their words.",
                result.Converted,
                column.Table,
                column.Name,
                result.CutValue,
                result.Shortened,
                result.Kept);
        }
        else
        {
            logger.LogInformation(
                "Named {Converted} owner's confirmations in {Table}.{Column} by their claim; {Kept} left as they were.",
                result.Converted,
                column.Table,
                column.Name,
                result.Kept);
        }

        var kept = rows.Where(row => row.Outcome is not (ConversionOutcome.Converted or ConversionOutcome.ConvertedWithCutValue)).Select(row => row.RowId).ToList();
        var shortened = rows.Where(row => row.Shortened).Select(row => row.RowId).ToList();
        if (kept.Count > 0 || shortened.Count > 0)
        {
            logger.LogInformation(
                "Rows of {Table}.{Column} kept as their words: {KeptRows}; shortened to fit: {ShortenedRows}.",
                column.Table,
                column.Name,
                string.Join(", ", kept),
                string.Join(", ", shortened));
        }
    }

    // Names come from the model, never from input; quoted all the same, as ReleaseAbandonedMigrationLock's are.
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>A column the converter walks: a message column, or — with <paramref name="Names"/> — the audit rows' names
    /// of the owner's confirmations.</summary>
    private sealed record Column(string Table, string Name, int Length, string? KeyPrefix, NameFilter? Names);

    /// <summary>Which audit rows still name the sentence the owner ticked: their action, and the words every consent
    /// sentence opens with.</summary>
    private sealed record NameFilter(string ActionColumn, object Action, string Opening);

    /// <summary>A row as the page read it.</summary>
    private sealed record StoredText(long RowId, string Text);

    /// <summary>What to write over <paramref name="Row"/> (<c>null</c>: nothing, only report it), and what to report.</summary>
    private sealed record Change(StoredText Row, string? NewText, StoredTextRowResult Result);
}
