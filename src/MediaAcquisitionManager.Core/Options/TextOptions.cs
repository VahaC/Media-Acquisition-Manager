namespace MediaAcquisitionManager.Core.Options;

/// <summary>
/// MAM-47 (ADR 0042) — how the server renders its sentences. Bound from the <c>Texts</c> section; both switches are off
/// in production and set only by the test host.
/// </summary>
public sealed class TextOptions
{
    public const string SectionName = "Texts";

    /// <summary>
    /// Throw on a missing key, a missing or unused value, or a value that doesn't fit its placeholder, instead of
    /// rendering the nearest readable fallback and logging a Warning. On in every test host, so the test that reaches a
    /// broken sentence fails there.
    /// </summary>
    public bool Strict { get; set; }

    /// <summary>
    /// Accept the pseudo-language <c>qps-ploc</c>: every message in English wrapped in <c>⟦…⟧</c>, which proves that a
    /// sentence reached the reader through the renderer. On only in the test suite's second pass.
    /// </summary>
    public bool Pseudo { get; set; }
}
