using System.Globalization;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// What a release search may be asked for (MAM-10). Each rule answers one readable sentence, or
/// <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern.
/// </summary>
/// <remarks>
/// The search text is required even with an identifier: a source that can't search by IMDb or TMDb id
/// (Toloka's movie search takes only text, ADR 0004) is still searched, by the title.
/// </remarks>
public static class ReleaseSearchRules
{
    /// <summary>Longer than any title worth typing, short enough that a pasted page of text isn't sent
    /// to every source.</summary>
    public const int TextMaximumLength = 200;

    /// <summary>How much of a value that isn't an id or a year a sentence repeats.</summary>
    private const int EchoLength = 32;

    private const string TextMissingError = "Enter the title to search for.";
    private const string TextTooLongError = "The search text can be at most 200 characters long.";
    private const string TextControlCharacterError = "The search text can't contain line breaks or other control characters.";

    /// <summary>The sentence saying what is wrong with <paramref name="text"/>, or <c>null</c>.
    /// Surrounding whitespace is ignored; whoever searches with it searches with it trimmed.</summary>
    public static string? ValidateText(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
            return TextMissingError;
        if (trimmed.Length > TextMaximumLength)
            return TextTooLongError;
        // A line break would end the query a source receives early, and split the log line it lands in.
        if (trimmed.Any(char.IsControl))
            return TextControlCharacterError;
        return null;
    }

    /// <summary>
    /// The year in <paramref name="year"/>, or the sentence saying why it isn't one; <c>null</c> year
    /// and <c>null</c> error for a blank value. ASCII digits only, 1 to 9999 — the year the Library's
    /// search takes.
    /// </summary>
    public static string? ValidateYear(string? year, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(year))
            return null;
        var trimmed = year.Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 9999)
            return $"“{Echo(trimmed)}” is not a year.";
        value = parsed;
        return null;
    }

    /// <summary>
    /// MAM-32 — the season in <paramref name="season"/>, or the sentence saying why it isn't one;
    /// <c>null</c> season and <c>null</c> error for a blank value. ASCII digits only, 0 (the specials, as
    /// the Library numbers them) to 9999 — the season the Library's episode list takes.
    /// </summary>
    public static string? ValidateSeason(string? season, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(season))
            return null;
        var trimmed = season.Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed > 9999)
            return $"“{Echo(trimmed)}” is not a season. Use a season number, such as 1, or 0 for the specials.";
        value = parsed;
        return null;
    }

    /// <summary>The sentence saying what is wrong with <paramref name="imdbId"/>, or <c>null</c>
    /// (blank included). Any id a media library may hold is accepted (<see cref="MediaIdentifierRules"/>).</summary>
    public static string? ValidateImdbId(string? imdbId) =>
        string.IsNullOrWhiteSpace(imdbId) || MediaIdentifierRules.IsImdbId(imdbId.Trim())
            ? null
            : $"“{Echo(imdbId.Trim())}” is not an IMDb id, which looks like tt0816692.";

    /// <summary>The sentence saying what is wrong with <paramref name="tmdbId"/>, or <c>null</c>
    /// (blank included).</summary>
    public static string? ValidateTmdbId(string? tmdbId) =>
        string.IsNullOrWhiteSpace(tmdbId) || MediaIdentifierRules.IsTmdbId(tmdbId.Trim())
            ? null
            : $"“{Echo(tmdbId.Trim())}” is not a TMDb id, which is a number such as 157336.";

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
