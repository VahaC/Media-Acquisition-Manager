namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-33 — what a running computation of the recommendations is doing, for the page to say more than
/// "Computing…" (Vasyl asked). The stages come in this order; a counted stage with nothing to count is passed over.
/// Stored by name.
/// </summary>
public enum RecommendationRunStage
{
    /// <summary>Reading the library's works and the names of the files only a folder scan found. Not counted.</summary>
    ReadingLibrary = 1,

    /// <summary>Asking the catalog which of its works the library's works without a catalog id are, by IMDb id or by
    /// title and year. Counted in library works looked up.</summary>
    FindingLibraryWorks = 2,

    /// <summary>Reading the details of each catalog work the library holds (the seeds). Counted in seeds.</summary>
    ReadingWorks = 3,

    /// <summary>Reading the collections the library's films belong to. Counted in collections.</summary>
    ReadingCollections = 4,

    /// <summary>Reading the works of the directors and creators two or more of the library's works are by. Counted in
    /// people.</summary>
    ReadingPeople = 5,

    /// <summary>Reading the catalog's best-rated works for the tabs the library holds few works of. Counted in
    /// pages.</summary>
    Discovering = 6,

    /// <summary>Weighing every candidate and storing them in place of the previous run's. Not counted.</summary>
    Saving = 7,
}
