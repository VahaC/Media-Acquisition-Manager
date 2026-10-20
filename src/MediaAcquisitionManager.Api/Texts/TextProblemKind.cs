namespace MediaAcquisitionManager.Api.Texts;

/// <summary>MAM-47 (ADR 0042) — what the lenient renderer had to work around in a sentence.</summary>
public enum TextProblemKind
{
    /// <summary>The key has no English template (a row written by another version); the message-unavailable sentence
    /// was written instead.</summary>
    NoTemplate = 1,

    /// <summary>The key is missing from a Ukrainian file that exists; it was written in English.</summary>
    MissingTranslation = 2,

    /// <summary>The message lacks a value its template names; an ellipsis (or, for a nested sentence, nothing) was
    /// written instead.</summary>
    MissingValue = 3,

    /// <summary>A value doesn't fit its placeholder's hint; it was written as it reads.</summary>
    Misfit = 4,

    /// <summary>Rendering threw; the nearest fallback was written.</summary>
    Failed = 5,
}
