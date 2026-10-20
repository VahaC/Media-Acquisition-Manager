using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Texts.Conversion;

/// <summary>
/// MAM-47 (ADR 0042, design §6.4) — a reading of a stored English sentence that <see cref="MessageParser"/> verified:
/// written back with the very wordings it matched, it gives the stored text byte for byte — or, for a text cut at its
/// column's limit, it starts with it.
/// </summary>
/// <param name="Message">The sentence as key and values: what the column stores instead of the words.</param>
/// <param name="LiteralLength">How many characters of template words the reading matched — a nested sentence's, a quote's
/// and a list's joining words included. The more, the more specific; the parser gives only the readings that matched the
/// most.</param>
/// <param name="CutValue">The text was cut at its column's limit inside its last value (design §6.4): that value keeps the
/// "…", and a reader sees the template's words after it — the converter's outcome "converted, with a cut value".</param>
public sealed record ParseCandidate(Message Message, int LiteralLength, bool CutValue);
