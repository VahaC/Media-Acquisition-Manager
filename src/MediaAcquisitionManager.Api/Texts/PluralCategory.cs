namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the CLDR plural categories English and Ukrainian use; a counted template's key ends with the
/// category's suffix (<c>_one</c>, <c>_few</c>, <c>_many</c>, <c>_other</c>), as in the web app's locale files.
/// </summary>
public enum PluralCategory
{
    /// <summary>English 1; Ukrainian 1, 21, 31, 101… (not 11).</summary>
    One = 1,

    /// <summary>Ukrainian 2–4, 22–24… (not 12–14).</summary>
    Few = 2,

    /// <summary>Ukrainian 0, 5–20, 25–30, 111…</summary>
    Many = 3,

    /// <summary>Every English number but 1; Ukrainian fractions.</summary>
    Other = 4,
}
