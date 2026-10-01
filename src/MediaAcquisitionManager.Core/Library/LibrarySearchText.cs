namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// The form library text is searched in (MAM-8): a title's and a file path's normalised copy, and the
/// search term, all upper-cased the same way.
/// </summary>
/// <remarks>
/// Stored beside the text and compared in .NET's form because SQLite can't do it: its <c>upper()</c>,
/// <c>lower()</c> and <c>LIKE</c> fold ASCII letters only, so a search for "чужі" would never find
/// "Чужі" in SQL (the precedent is MAM-7's <c>NormalizedName</c>). Culture independent, so the stored
/// form never depends on the machine that wrote it. Nothing is trimmed here: the stored copy keeps
/// the text as it is, and whoever normalises a search term trims it first.
/// </remarks>
public static class LibrarySearchText
{
    /// <summary><paramref name="value"/> upper-cased, culture independent.</summary>
    public static string Normalize(string value) => value.ToUpperInvariant();
}
