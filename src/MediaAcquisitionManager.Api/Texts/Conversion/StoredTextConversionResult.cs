namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>MAM-47 (ADR 0042, design §6.1) — what one run of the startup converter found and did, column by column: for
/// the tests and the production-copy run, which read it instead of the log.</summary>
/// <param name="Columns">Every column it walked, as the model maps them — the message columns, then the audit rows' names
/// of the owner's confirmations — whether or not it found work in them.</param>
public sealed record StoredTextConversionResult(IReadOnlyList<StoredTextColumnResult> Columns)
{
    /// <summary>The column <paramref name="table"/>.<paramref name="column"/>, or <c>null</c> when the run didn't walk
    /// it.</summary>
    public StoredTextColumnResult? Of(string table, string column) =>
        Columns.FirstOrDefault(result => string.Equals(result.Table, table, StringComparison.Ordinal)
            && string.Equals(result.Column, column, StringComparison.Ordinal));
}
