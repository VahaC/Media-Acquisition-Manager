namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>MAM-47 (ADR 0042, design §6.5) — one stored text the startup converter changed or reported.</summary>
/// <param name="RowId">The row's SQLite <c>rowid</c>: what the log names instead of the text, which may hold paths and
/// a service's words.</param>
/// <param name="Outcome">What the converter did with it.</param>
/// <param name="Shortened">The reading was longer than its column as JSON and was fitted into it by cutting its longest
/// value (design §5.3).</param>
public sealed record StoredTextRowResult(long RowId, ConversionOutcome Outcome, bool Shortened = false);
