using System.Globalization;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// What a stored search's release list may be filtered and sorted by (MAM-12). The rule answers one
/// readable sentence, or <c>null</c> when every value is acceptable — the MAM-4 rules-class pattern —
/// and reads the values into a <see cref="SearchReleaseFilter"/>.
/// </summary>
/// <remarks>
/// Every value comes as text, the way a query string carries it, so a wrong one reaches the page as this
/// sentence rather than as a model-binding error. A blank value filters nothing; surrounding whitespace is
/// ignored. Paging is <c>PagingRules</c>', as on every other list.
/// </remarks>
public static class SearchReleaseListRules
{
    /// <summary>The value that filters for the releases whose title claims nothing of the kind.</summary>
    private const string Unknown = "unknown";

    /// <summary>The largest resolution class the quality filter takes, four digits: past any frame
    /// height a title names.</summary>
    private const int ResolutionMaximum = 9999;

    /// <summary>How much of a value that isn't one a sentence repeats, as <see cref="ReleaseSearchRules"/>
    /// does.</summary>
    private const int EchoLength = 32;

    private const string SizeRangeError = "The smallest size can't be larger than the largest.";

    private static readonly Dictionary<string, SearchReleaseSort> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["seeders"] = SearchReleaseSort.Seeders,
        ["largest"] = SearchReleaseSort.Largest,
        ["smallest"] = SearchReleaseSort.Smallest,
        ["newest"] = SearchReleaseSort.Newest,
        ["quality"] = SearchReleaseSort.Quality,
    };

    /// <summary>
    /// The sentence saying what is wrong with the first value that is wrong, or <c>null</c> with every
    /// value read into <paramref name="filter"/> (<c>null</c> when there is a sentence).
    /// </summary>
    /// <param name="source">A search source's id.</param>
    /// <param name="language">A three-letter ISO 639-2 code in any letter case (<c>ukr</c>), or
    /// <c>unknown</c>.</param>
    /// <param name="quality">A resolution class (<c>1080</c>), or <c>unknown</c>.</param>
    /// <param name="minSizeBytes">The smallest size, in bytes.</param>
    /// <param name="maxSizeBytes">The largest size, in bytes.</param>
    /// <param name="sort"><c>seeders</c> (the default), <c>largest</c>, <c>smallest</c>, <c>newest</c> or
    /// <c>quality</c>, in any letter case.</param>
    /// <param name="filter">What the values say, when every one is acceptable.</param>
    public static string? Validate(
        string? source,
        string? language,
        string? quality,
        string? minSizeBytes,
        string? maxSizeBytes,
        string? sort,
        out SearchReleaseFilter? filter)
    {
        filter = null;

        Guid? sourceId = null;
        if (Trimmed(source) is { } sourceText)
        {
            if (!Guid.TryParse(sourceText, out var parsed))
                return $"“{Echo(sourceText)}” is not a release source.";
            sourceId = parsed;
        }

        string? audioLanguage = null;
        var withoutAudioLanguage = false;
        if (Trimmed(language) is { } languageText)
        {
            if (IsUnknown(languageText))
                withoutAudioLanguage = true;
            // ASCII letters checked one by one rather than by an ignore-case [a-z]: .NET's case folding
            // would let the Kelvin sign through as a "k".
            else if (languageText.Length == 3 && languageText.All(char.IsAsciiLetter))
                audioLanguage = languageText.ToLowerInvariant();
            else
                return $"“{Echo(languageText)}” is not a language code. Use a three-letter code such as ukr, or unknown.";
        }

        int? resolution = null;
        var withoutResolution = false;
        if (Trimmed(quality) is { } qualityText)
        {
            if (IsUnknown(qualityText))
                withoutResolution = true;
            else if (int.TryParse(qualityText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= ResolutionMaximum)
                resolution = parsed;
            else
                return $"“{Echo(qualityText)}” is not a resolution. Use one such as 1080, or unknown.";
        }

        if (ValidateSize(minSizeBytes, out var minimum) is { } minimumError)
            return minimumError;
        if (ValidateSize(maxSizeBytes, out var maximum) is { } maximumError)
            return maximumError;
        if (minimum > maximum)
            return SizeRangeError;

        var order = SearchReleaseSort.Seeders;
        if (Trimmed(sort) is { } sortText && !Sorts.TryGetValue(sortText, out order))
            return $"“{Echo(sortText)}” is not a sort order. Use seeders, largest, smallest, newest or quality.";

        filter = new SearchReleaseFilter(sourceId, audioLanguage, withoutAudioLanguage, resolution, withoutResolution, minimum, maximum, order);
        return null;
    }

    /// <summary>A size of zero or more bytes, ASCII digits only — no sign, separator or unit.</summary>
    private static string? ValidateSize(string? text, out long? size)
    {
        size = null;
        if (Trimmed(text) is not { } sizeText)
            return null;
        if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return $"“{Echo(sizeText)}” is not a size in bytes.";
        size = parsed;
        return null;
    }

    private static bool IsUnknown(string text) => string.Equals(text, Unknown, StringComparison.OrdinalIgnoreCase);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
