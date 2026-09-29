namespace MediaAcquisitionManager.Core.Paging;

/// <summary>
/// What a list request's page and page size may be — one rule for every server-side list (the users
/// of MAM-4, the integrations of MAM-7), so they answer the same sentences.
/// </summary>
public static class PagingRules
{
    /// <summary>
    /// The largest page a list serves: a page-size menu offers up to this, and anything bigger would
    /// only let one request read a whole large table at once.
    /// </summary>
    public const int MaximumPageSize = 100;

    private const string PageError = "Page must be 1 or more.";
    private const string PageSizeError = "Page size must be between 1 and 100.";

    /// <summary>The sentence saying what is wrong with <paramref name="page"/> (1-based) or
    /// <paramref name="pageSize"/>, or <c>null</c> when both are acceptable.</summary>
    public static string? Validate(int page, int pageSize)
    {
        if (page < 1)
            return PageError;
        if (pageSize is < 1 or > MaximumPageSize)
            return PageSizeError;
        return null;
    }
}
