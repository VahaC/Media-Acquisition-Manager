namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>MAM-47 (ADR 0042, design §6.1, §6.5) — what one run of the startup converter did in one column.</summary>
/// <param name="Table">The table, as the model maps it.</param>
/// <param name="Column">The column.</param>
/// <param name="Rows">Every text it converted or kept, by row; a text converted or kept by an earlier start isn't here.</param>
/// <param name="Failed">The column's walk threw: it was logged and left to the tolerant column reader, and the next start
/// tries it again. <paramref name="Rows"/> holds what was written before the failure.</param>
public sealed record StoredTextColumnResult(string Table, string Column, IReadOnlyList<StoredTextRowResult> Rows, bool Failed = false)
{
    /// <summary>How many texts became key and values, with a cut value or not.</summary>
    public int Converted => Rows.Count(row => row.Outcome is ConversionOutcome.Converted or ConversionOutcome.ConvertedWithCutValue);

    /// <summary>How many of <see cref="Converted"/> were cut by an older build inside their last value.</summary>
    public int CutValue => Rows.Count(row => row.Outcome == ConversionOutcome.ConvertedWithCutValue);

    /// <summary>How many of <see cref="Converted"/> were fitted into the column.</summary>
    public int Shortened => Rows.Count(row => row.Shortened);

    /// <summary>How many texts were kept as their words, whatever the reason.</summary>
    public int Kept => Rows.Count - Converted;
}
