using System.Globalization;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// The order library titles are listed in (MAM-8): Ukrainian alphabetical order, letter case ignored.
/// The database sorts by it through a collation of <see cref="CollationName"/>, and the API picks a
/// work's title with <see cref="Comparer"/>, so the page's order and the title each card shows agree.
/// </summary>
/// <remarks>
/// <para>
/// Code-point order would put І, Є and Ї before А, and Ґ after Я. The invariant culture (ICU's root
/// collation) gets the letters right one by one but weighs ґ as a variant of г and ї as a variant of
/// і, so "Ґава" would come before "Гнів" and "Їжа" before "Ірина". Ukrainian's own collation orders
/// а б в г ґ д е є … и і ї й as letters of their own, and puts Cyrillic before Latin, which suits a
/// library whose titles are mostly Ukrainian. Pinned by <c>LibraryTitleOrderTests</c>.
/// </para>
/// <para>
/// It needs ICU, which the runtime image has. Without it (invariant globalization mode) the Ukrainian
/// culture can't be created, and the comparer falls back to the invariant culture's letter-case
/// insensitive order instead of failing every database connection that registers the collation.
/// </para>
/// </remarks>
public static class LibraryTitleOrder
{
    /// <summary>The collation name the database registers <see cref="Comparer"/> under on every
    /// connection, for an <c>ORDER BY … COLLATE</c>.</summary>
    public const string CollationName = "LIBRARY_TITLE";

    /// <summary>Ukrainian alphabetical order, letter case ignored.</summary>
    public static StringComparer Comparer { get; } = Create();

    private static StringComparer Create()
    {
        try
        {
            return StringComparer.Create(CultureInfo.GetCultureInfo("uk"), CompareOptions.IgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
        }
    }
}
