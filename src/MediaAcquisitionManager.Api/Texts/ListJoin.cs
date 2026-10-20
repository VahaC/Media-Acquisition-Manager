namespace MediaAcquisitionManager.Api.Texts;

/// <summary>MAM-47 (ADR 0042) — how a list placeholder joins its items.</summary>
public enum ListJoin
{
    /// <summary>Not a list.</summary>
    None = 0,

    /// <summary><c>and</c>: "a", "a and b", "a, b and c" (no Oxford comma, as today); Ukrainian «і».</summary>
    And = 1,

    /// <summary><c>or</c>: "a", "a or b", "a, b or c".</summary>
    Or = 2,

    /// <summary><c>comma</c>: "a, b, c".</summary>
    Comma = 3,

    /// <summary><c>space</c>: "a b c" — sentences one after another.</summary>
    Space = 4,
}
