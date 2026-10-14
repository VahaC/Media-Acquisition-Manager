using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// Which language a recommended work's description may be asked in: a two-letter language code, with a region when
/// wanted (<c>en</c>, <c>uk-UA</c>) — the shape TMDb takes — and English when none is given. One readable sentence for a
/// value refused, <c>null</c> for one accepted — the MAM-4 rules-class pattern.
/// </summary>
public static partial class RecommendationOverviewRules
{
    /// <summary>The language every description falls back to when the catalog has none in the one asked for.</summary>
    public const string FallbackLanguage = "en";

    /// <summary>How much of a value that isn't a language code a sentence repeats, as the other rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="language"/>, or <c>null</c> with it, trimmed — English when
    /// empty — in <paramref name="validLanguage"/>.
    /// </summary>
    public static string? ValidateLanguage(string? language, out string? validLanguage)
    {
        var trimmed = language?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            validLanguage = FallbackLanguage;
            return null;
        }

        if (!IsLanguage(trimmed))
        {
            validLanguage = null;
            var echo = trimmed.Length <= EchoLength ? trimmed : string.Concat(trimmed.AsSpan(0, EchoLength), "…");
            return $"“{echo}” is not a language code, which is two letters with a region when wanted, such as en or uk-UA.";
        }

        validLanguage = trimmed;
        return null;
    }

    /// <summary>Whether <paramref name="language"/> is a language code as TMDb takes one: <c>en</c>, <c>uk-UA</c>.</summary>
    public static bool IsLanguage(string language) => LanguagePattern().IsMatch(language);

    /// <summary>Whether <paramref name="language"/> is English, of any region.</summary>
    public static bool IsFallback(string language) =>
        language.Split('-')[0].Equals(FallbackLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>\z</c>, not <c>$</c>, which would also accept a trailing line break.</summary>
    [GeneratedRegex(@"^[a-z]{2}(-[A-Z]{2})?\z")]
    private static partial Regex LanguagePattern();
}
