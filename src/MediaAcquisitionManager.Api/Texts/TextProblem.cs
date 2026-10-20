namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — what the lenient renderer worked around while writing a sentence, for the one Warning per key and
/// language <see cref="RequestTexts"/> logs. Names a key, a value's name and a type at most, never a value: values are
/// paths, titles and a service's words. The words of the Warning belong to the log line, which stays English.
/// </summary>
/// <param name="Key">The message's key.</param>
/// <param name="Language">The language it was written in.</param>
/// <param name="Kind">What was wrong.</param>
/// <param name="ValueName">The value concerned, for a missing or misfit value.</param>
/// <param name="Detail">A misfit value's type and hint, or the type of what rendering threw.</param>
public sealed record TextProblem(
    string Key,
    TextLanguage Language,
    TextProblemKind Kind,
    string? ValueName = null,
    string? Detail = null);
