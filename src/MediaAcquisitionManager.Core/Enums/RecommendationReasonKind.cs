namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-33 — why a work is recommended (ADR 0023: every reason is explained). Each is a reason row of its
/// candidate and a chip on its card. Stored by name.
/// </summary>
public enum RecommendationReasonKind
{
    /// <summary>A film of a collection the library holds released parts of, and lacks this one.</summary>
    Collection = 1,

    /// <summary>A film by a director at least two of the library's films are by.</summary>
    Director = 2,

    /// <summary>A show by a creator at least two of the library's shows are by.</summary>
    Creator = 3,

    /// <summary>A work the catalog recommends beside works the library holds.</summary>
    Recommended = 4,

    /// <summary>A series the library holds, with aired seasons it holds no episode of.</summary>
    MissingSeasons = 5,

    /// <summary>A best-rated work of a tab the library holds few works of: a fallback, listed only once a
    /// release source carried it.</summary>
    TopRated = 6,

    /// <summary>A work a release source has just published.</summary>
    NewAtSource = 7,
}
