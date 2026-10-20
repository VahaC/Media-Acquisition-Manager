namespace MediaAcquisitionManager.Api.Texts;

/// <summary>MAM-47 (ADR 0042) — the languages the server writes its sentences in.</summary>
public enum TextLanguage
{
    /// <summary>English: the templates of <c>Locales/en</c>, and the language of every request that asks for no other,
    /// of logs and of background work.</summary>
    English = 1,

    /// <summary>Ukrainian: the templates of <c>Locales/uk</c>; a key whose Ukrainian file hasn't landed falls back to
    /// English.</summary>
    Ukrainian = 2,

    /// <summary>The test suite's pseudo-language (<c>qps-ploc</c>, only with <c>Texts:Pseudo</c>): English with every
    /// message wrapped in <c>⟦…⟧</c>, so a test can tell a rendered sentence from one that bypassed the
    /// renderer.</summary>
    Pseudo = 3,
}
