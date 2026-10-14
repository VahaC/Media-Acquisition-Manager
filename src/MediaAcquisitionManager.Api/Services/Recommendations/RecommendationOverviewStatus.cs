namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>How a request for a recommended work's description ended.</summary>
public enum RecommendationOverviewStatus
{
    /// <summary>The description, or the answer that the catalog has none: kept, or just asked for.</summary>
    Found = 1,

    /// <summary>A kind, an id or a language the request doesn't take; the error says which.</summary>
    Invalid = 2,

    /// <summary>No catalog work of that kind and id. Answered as a missing row is.</summary>
    NotFound = 3,

    /// <summary>Nothing kept, and no catalog connected to ask.</summary>
    NoCatalog = 4,

    /// <summary>Nothing kept, and the catalog couldn't be asked or failed to answer; the error says why.</summary>
    Failed = 5,
}
