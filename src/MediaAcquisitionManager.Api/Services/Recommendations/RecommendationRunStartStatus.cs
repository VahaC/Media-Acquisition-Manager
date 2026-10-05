namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — how a request for a computation of the recommendations ended.</summary>
public enum RecommendationRunStartStatus
{
    /// <summary>A run is queued: a new one, or the one already waiting, which the request joined.</summary>
    Queued = 1,

    /// <summary>No metadata catalog is connected, so there is nothing to compute from; nothing was written.</summary>
    NoCatalog = 2,

    /// <summary>The queue is full; the new run was recorded as failed with the error.</summary>
    Unavailable = 3,
}
