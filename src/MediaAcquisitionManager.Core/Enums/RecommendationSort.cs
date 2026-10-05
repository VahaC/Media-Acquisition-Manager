namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-33 — the order a recommendation tab is listed in (Vasyl asked to sort the list). Every order is total: a tie
/// falls back to <see cref="Score"/>'s order, so a page never reshuffles between two reads. Read by name from the
/// query string.
/// </summary>
public enum RecommendationSort
{
    /// <summary>The default: the best recommended first — the score (ADR 0023), then how many rated it, the title
    /// (ordinal) and the catalog id.</summary>
    Score = 1,

    /// <summary>The best rated first, by the rating floor's weighted average — a 10.0 of three votes doesn't top an 8.5
    /// of thousands — then how many rated it.</summary>
    Rating = 2,

    /// <summary>The most rated first: what most people have seen.</summary>
    Votes = 3,

    /// <summary>The latest released first; a work of no known date last.</summary>
    Newest = 4,

    /// <summary>The earliest released first; a work of no known date last.</summary>
    Oldest = 5,

    /// <summary>By the catalog's title, as the Library page orders titles (Ukrainian collation), then the original
    /// title.</summary>
    Title = 6,

    /// <summary>What the release sources carry most first: the works the last check found, by how many releases;
    /// then those it didn't find; then those never checked.</summary>
    Releases = 7,
}
