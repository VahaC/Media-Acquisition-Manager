using System.Globalization;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — what a page of a recommendation tab may be asked for: the tab, its filters and the page. The rule
/// answers one readable sentence, or <c>null</c> with every value read into a <see cref="RecommendationListFilter"/>
/// — the MAM-4 rules-class pattern.
/// </summary>
/// <remarks>
/// Every value comes as text, the way a query string carries it, so a wrong one reaches the page as this sentence
/// rather than as a model-binding body. A blank filter filters nothing; surrounding whitespace is ignored. Names are
/// read by name alone, in any letter case: <c>Enum.TryParse</c> would also take <c>3</c>, <c>-1</c> and
/// <c>Movies, Series</c>, which it ORs into one value (the <c>DownloadJobListRules</c> rule). The page and its size
/// are <see cref="PagingRules"/>' and answer its sentences, a value that isn't a number included.
/// </remarks>
public static class RecommendationListRules
{
    /// <summary>The page size when none is asked for: the Recommendations page's own.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>The sentence a tab the caller switched off in their Profile gets (Vasyl's decision 5).</summary>
    public const string TabSwitchedOffError = "This tab is switched off in your profile.";

    /// <summary>How much of a value that isn't one a sentence repeats, as the other list rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>The latest decade a filter takes: its first year has four digits, as a year's does.</summary>
    private const int LatestDecade = 9990;

    private const string TabMissingError = "Choose a tab: Movies, Series, Cartoons or Documentaries.";

    // PagingRules' own sentences, for a page that isn't a number at all.
    private const string PageError = "Page must be 1 or more.";
    private const string PageSizeError = "Page size must be between 1 and 100.";

    /// <summary>
    /// The sentence saying what is wrong with the first value that is wrong — the tab, the reason, the availability,
    /// the genre, the decade, the kind, the sort, then the page — or <c>null</c> with every value read into
    /// <paramref name="filter"/> (<c>null</c> when there is a sentence).
    /// </summary>
    /// <param name="tab"><c>Movies</c>, <c>Series</c>, <c>Cartoons</c> or <c>Documentaries</c>; required.</param>
    /// <param name="reason">A reason kind by name (<c>Collection</c>).</param>
    /// <param name="available"><c>true</c> keeps only the works a release source carries; <c>false</c>, like a
    /// blank value, keeps every work — the page's unticked box.</param>
    /// <param name="genreId">A catalog genre id (<c>18</c>).</param>
    /// <param name="decade">A decade's first year (<c>1990</c>).</param>
    /// <param name="kind"><c>Movie</c> or <c>Series</c>.</param>
    /// <param name="sort">A <see cref="RecommendationSort"/> by name (<c>Rating</c>);
    /// <see cref="RecommendationSort.Score"/> when blank.</param>
    /// <param name="page">The page, from 1; 1 when blank.</param>
    /// <param name="pageSize">The works per page, 1 to 100; <see cref="DefaultPageSize"/> when blank.</param>
    /// <param name="filter">What the values say, when every one is acceptable.</param>
    public static string? Validate(
        string? tab,
        string? reason,
        string? available,
        string? genreId,
        string? decade,
        string? kind,
        string? sort,
        string? page,
        string? pageSize,
        out RecommendationListFilter? filter)
    {
        filter = null;

        if (Trimmed(tab) is not { } tabText)
            return TabMissingError;
        if (ByName<RecommendationTab>(tabText) is not { } listedTab)
            return $"“{Echo(tabText)}” is not a recommendation tab. Use Movies, Series, Cartoons or Documentaries.";

        RecommendationReasonKind? reasonKind = null;
        if (Trimmed(reason) is { } reasonText)
        {
            reasonKind = ByName<RecommendationReasonKind>(reasonText);
            if (reasonKind is null)
                return $"“{Echo(reasonText)}” is not a recommendation reason. Use Collection, Director, Creator, Recommended, MissingSeasons, TopRated or NewAtSource.";
        }

        var availableOnly = false;
        if (Trimmed(available) is { } availableText)
        {
            if (string.Equals(availableText, bool.TrueString, StringComparison.OrdinalIgnoreCase))
                availableOnly = true;
            else if (!string.Equals(availableText, bool.FalseString, StringComparison.OrdinalIgnoreCase))
                return $"“{Echo(availableText)}” is not an availability filter. Use true or false.";
        }

        int? genre = null;
        if (Trimmed(genreId) is { } genreText)
        {
            if (!int.TryParse(genreText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedGenre))
                return $"“{Echo(genreText)}” is not a genre id, which is a number such as 18.";
            genre = parsedGenre;
        }

        int? firstYear = null;
        if (Trimmed(decade) is { } decadeText)
        {
            if (!int.TryParse(decadeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDecade)
                || parsedDecade % 10 != 0
                || parsedDecade > LatestDecade)
                return $"“{Echo(decadeText)}” is not a decade. Use its first year, such as 1990.";
            firstYear = parsedDecade;
        }

        MediaWorkKind? workKind = null;
        if (Trimmed(kind) is { } kindText)
        {
            if (ValidateKind(kindText, out var parsedKind) is { } kindError)
                return kindError;
            workKind = parsedKind;
        }

        var order = RecommendationSort.Score;
        if (Trimmed(sort) is { } sortText)
        {
            if (ByName<RecommendationSort>(sortText) is not { } parsedSort)
                return $"“{Echo(sortText)}” is not a sort. Use Score, Rating, Votes, Newest, Oldest, Title or Releases.";
            order = parsedSort;
        }

        var pageNumber = 1;
        if (Trimmed(page) is { } pageText && !int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out pageNumber))
            return PageError;
        var size = DefaultPageSize;
        if (Trimmed(pageSize) is { } sizeText && !int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out size))
            return PageSizeError;
        if (PagingRules.Validate(pageNumber, size) is { } pagingError)
            return pagingError;

        filter = new RecommendationListFilter(listedTab, reasonKind, availableOnly, genre, firstYear, workKind, order, pageNumber, size);
        return null;
    }

    /// <summary>
    /// The sentence saying <paramref name="kind"/> is not <c>Movie</c> or <c>Series</c> (in any letter case), or
    /// <c>null</c> with the kind it names in <paramref name="value"/>. A recommended work is one of the two, never an
    /// episode, so <see cref="MediaWorkKind.Episode"/> is no kind here. Blank is refused: a caller with an optional
    /// kind checks for blank first.
    /// </summary>
    public static string? ValidateKind(string? kind, out MediaWorkKind? value)
    {
        value = null;
        var text = kind?.Trim() ?? "";
        if (string.Equals(text, nameof(MediaWorkKind.Movie), StringComparison.OrdinalIgnoreCase))
            value = MediaWorkKind.Movie;
        else if (string.Equals(text, nameof(MediaWorkKind.Series), StringComparison.OrdinalIgnoreCase))
            value = MediaWorkKind.Series;
        else
            return $"“{Echo(text)}” is not a kind of work. Use Movie or Series.";
        return null;
    }

    /// <summary>The member of <typeparamref name="TEnum"/> named <paramref name="text"/> in any letter case;
    /// <c>null</c> for anything else, a number included.</summary>
    private static TEnum? ByName<TEnum>(string text) where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().FirstOrDefault(name => name.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<TEnum>(name)
            : null;

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
