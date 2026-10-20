using System.Globalization;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — what a page of a recommendation tab may be asked for: the tab, its filters and the page. The rule
/// answers why a value is refused, as a message the Api renders (ADR 0042), or <c>null</c> with every value read into a
/// <see cref="RecommendationListFilter"/> — the MAM-4 rules-class pattern.
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

    /// <summary>What a tab the caller switched off in their Profile gets (Vasyl's decision 5).</summary>
    public static readonly Message TabSwitchedOffError = Message.Of("recommendations.list.tab-switched-off");

    /// <summary>How much of a value that isn't one a sentence repeats, as the other list rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>The latest decade a filter takes: its first year has four digits, as a year's does.</summary>
    private const int LatestDecade = 9990;

    private static readonly Message TabMissingError = Message.Of("recommendations.list.tab-missing");

    /// <summary>
    /// Why the first value that is wrong is refused — the tab, the reason, the availability,
    /// the genre, the decade, the kind, the sort, then the page — or <c>null</c> with every value read into
    /// <paramref name="filter"/> (<c>null</c> when there is a message).
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
    public static Message? Validate(
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
            return Message.Of("recommendations.list.tab-unknown", ("value", Echo(tabText)));

        RecommendationReasonKind? reasonKind = null;
        if (Trimmed(reason) is { } reasonText)
        {
            reasonKind = ByName<RecommendationReasonKind>(reasonText);
            if (reasonKind is null)
                return Message.Of("recommendations.list.reason-unknown", ("value", Echo(reasonText)));
        }

        var availableOnly = false;
        if (Trimmed(available) is { } availableText)
        {
            if (string.Equals(availableText, bool.TrueString, StringComparison.OrdinalIgnoreCase))
                availableOnly = true;
            else if (!string.Equals(availableText, bool.FalseString, StringComparison.OrdinalIgnoreCase))
                return Message.Of("recommendations.list.availability-invalid", ("value", Echo(availableText)));
        }

        int? genre = null;
        if (Trimmed(genreId) is { } genreText)
        {
            if (!int.TryParse(genreText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedGenre))
                return Message.Of("recommendations.list.genre-invalid", ("value", Echo(genreText)));
            genre = parsedGenre;
        }

        int? firstYear = null;
        if (Trimmed(decade) is { } decadeText)
        {
            if (!int.TryParse(decadeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDecade)
                || parsedDecade % 10 != 0
                || parsedDecade > LatestDecade)
                return Message.Of("recommendations.list.decade-invalid", ("value", Echo(decadeText)));
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
                return Message.Of("recommendations.list.sort-unknown", ("value", Echo(sortText)));
            order = parsedSort;
        }

        var pageNumber = 1;
        // PagingRules' own messages, for a page that isn't a number at all.
        if (Trimmed(page) is { } pageText && !int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out pageNumber))
            return PagingRules.PageError;
        var size = DefaultPageSize;
        if (Trimmed(pageSize) is { } sizeText && !int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out size))
            return PagingRules.PageSizeError;
        if (PagingRules.Validate(pageNumber, size) is { } pagingError)
            return pagingError;

        filter = new RecommendationListFilter(listedTab, reasonKind, availableOnly, genre, firstYear, workKind, order, pageNumber, size);
        return null;
    }

    /// <summary>
    /// Why <paramref name="kind"/> is not <c>Movie</c> or <c>Series</c> (in any letter case), or
    /// <c>null</c> with the kind it names in <paramref name="value"/>. A recommended work is one of the two, never an
    /// episode, so <see cref="MediaWorkKind.Episode"/> is no kind here. Blank is refused: a caller with an optional
    /// kind checks for blank first.
    /// </summary>
    public static Message? ValidateKind(string? kind, out MediaWorkKind? value)
    {
        value = null;
        var text = kind?.Trim() ?? "";
        if (string.Equals(text, nameof(MediaWorkKind.Movie), StringComparison.OrdinalIgnoreCase))
            value = MediaWorkKind.Movie;
        else if (string.Equals(text, nameof(MediaWorkKind.Series), StringComparison.OrdinalIgnoreCase))
            value = MediaWorkKind.Series;
        else
            return Message.Of("recommendations.list.kind-unknown", ("value", Echo(text)));
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
