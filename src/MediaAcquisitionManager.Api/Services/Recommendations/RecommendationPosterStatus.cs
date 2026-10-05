namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — how a request for a recommended work's poster ended.</summary>
public enum RecommendationPosterStatus
{
    /// <summary>The poster's bytes, from the cache or just fetched.</summary>
    Served = 1,

    /// <summary>
    /// Nothing to serve: a name that is no poster's, no catalog work's poster, one the catalog's image host doesn't
    /// have, or one not cached while no catalog is connected. Answered as a missing row is, without saying which.
    /// </summary>
    NotFound = 2,

    /// <summary>The catalog couldn't be asked for it, or failed to send it; the error says why.</summary>
    Failed = 3,
}
