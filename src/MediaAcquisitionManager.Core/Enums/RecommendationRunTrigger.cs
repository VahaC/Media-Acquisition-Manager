namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-33 — what queued a computation of the recommendations. Stored by name.</summary>
public enum RecommendationRunTrigger
{
    /// <summary>A library sync stored a new snapshot: what the library holds may have changed.</summary>
    LibrarySynced = 1,

    /// <summary>The installation owner asked for it on the Recommendations page.</summary>
    Requested = 2,

    /// <summary>A user switched on a tab nobody else had on, whose works were never computed.</summary>
    TabEnabled = 3,

    /// <summary>A metadata catalog was connected, or switched back on.</summary>
    CatalogConnected = 4,

    /// <summary>The reader of a release source's newest releases resolved new ones to catalog works.</summary>
    FeedRead = 5,
}
