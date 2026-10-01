namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>How a request for a file inspection ended (MAM-9).</summary>
public enum FileInspectionStartStatus
{
    /// <summary>A run is queued: a new one, or the one already waiting, which the request joined.</summary>
    Queued = 1,

    /// <summary>No folder is allowed, so there is nothing the app may read; nothing was written.</summary>
    NoAllowedRoots = 2,

    /// <summary>The queue is full; the new run was recorded as failed with the error.</summary>
    Unavailable = 3,
}
