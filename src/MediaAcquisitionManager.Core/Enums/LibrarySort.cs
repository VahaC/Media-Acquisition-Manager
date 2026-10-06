namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-8 — the order the Library page's works are listed in (Vasyl asked for a sort on the page). Every order is
/// total: a tie falls back to <see cref="Title"/>'s order, then the id, so a page never reshuffles between two reads
/// and no work shows on two pages. Read by name from the query string.
/// </summary>
public enum LibrarySort
{
    /// <summary>The default: by the title a card shows, in Ukrainian alphabetical order, letter case ignored.</summary>
    Title = 1,

    /// <summary>By the same title, from the end of the alphabet.</summary>
    TitleDescending = 2,

    /// <summary>The latest year first, the year a card shows; a work with none last.</summary>
    Newest = 3,

    /// <summary>The earliest year first; a work with none last.</summary>
    Oldest = 4,

    /// <summary>The most on disk first: the sizes its files report added up, a series' episodes' files too; a work
    /// whose files report none last.</summary>
    Largest = 5,

    /// <summary>The most recently added first: when the app first saw the work, a series by its latest episode.</summary>
    Added = 6,
}
