namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-33 — where one computation of the recommendations stands. Stored by name.</summary>
public enum RecommendationRunStatus
{
    /// <summary>Recorded and waiting for the worker; nothing has been asked of the catalog yet.</summary>
    Queued = 1,

    /// <summary>The worker is reading the library and the catalog.</summary>
    Running = 2,

    /// <summary>Every candidate was computed and stored; the previous ones were replaced in one go.</summary>
    Succeeded = 3,

    /// <summary>The run stopped; its error says why. The previous run's candidates are kept as they were.</summary>
    Failed = 4,
}
