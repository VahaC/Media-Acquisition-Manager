namespace MediaAcquisitionManager.Core.Accounts;

/// <summary>
/// MAM-46 — the languages the web app speaks, which a user can save on their Profile so the app follows them to other
/// browsers and devices (ADR 0001: every UI string comes from a locale resource, one per language).
/// </summary>
/// <remarks>
/// A bare language code, never a region: the web app formats dates and numbers in the browser's own locale of that
/// language (<c>en-GB</c> keeps its day-first dates), so the account only says which language. No saved language
/// (<c>null</c>) means "as in the browser".
/// </remarks>
public static class UiLanguages
{
    /// <summary>The longest code a user's language column holds: far past the two letters of today's codes.</summary>
    public const int MaximumLength = 8;

    /// <summary>English, the language every string is written in first.</summary>
    public const string English = "en";

    /// <summary>Ukrainian.</summary>
    public const string Ukrainian = "uk";

    /// <summary>Every language the web app has a locale for, in the order a sentence lists them.</summary>
    public static IReadOnlyList<string> Supported { get; } = [English, Ukrainian];

    /// <summary>Whether <paramref name="language"/> is one of <see cref="Supported"/>, exactly as written there.</summary>
    public static bool IsSupported(string? language) =>
        language is not null && Supported.Contains(language, StringComparer.Ordinal);
}
