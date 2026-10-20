using MediaAcquisitionManager.Core.Texts;

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

    /// <summary>A page below 1, or one that isn't a number at all (MAM-33's text-valued lists answer it too).</summary>
    internal static readonly Message PageError = Message.Of("common.paging.page-invalid");

    /// <summary>A page size outside 1–<see cref="MaximumPageSize"/>, or one that isn't a number at all.</summary>
    internal static readonly Message PageSizeError = Message.Of("common.paging.page-size-invalid");

    /// <summary>Why <paramref name="page"/> (1-based) or <paramref name="pageSize"/> is refused, as a message the Api
    /// renders (ADR 0042), or <c>null</c> when both are acceptable.</summary>
    public static Message? Validate(int page, int pageSize)
    {
        if (page < 1)
            return PageError;
        if (pageSize is < 1 or > MaximumPageSize)
            return PageSizeError;
        return null;
    }
}
