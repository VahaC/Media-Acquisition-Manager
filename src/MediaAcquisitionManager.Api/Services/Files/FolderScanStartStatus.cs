namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>How a request for a scan of the media folders ended (MAM-18).</summary>
public enum FolderScanStartStatus
{
    /// <summary>A scan is queued: a new one, or the one already waiting, which the request joined.</summary>
    Queued = 1,

    /// <summary>No media folder is allowed, so there is nothing to scan; nothing was queued.</summary>
    NoAllowedRoots = 2,

    /// <summary>The queue refused the scan; the run is recorded as failed.</summary>
    Unavailable = 3,
}
