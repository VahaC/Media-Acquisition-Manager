namespace MediaAcquisitionManager.Core.Enums;

/// <summary>Where one scan of the media folders stands (MAM-18). Stored by name.</summary>
public enum FolderScanStatus
{
    /// <summary>Recorded and waiting for the worker; nothing has been looked at yet.</summary>
    Queued = 1,

    /// <summary>The worker is walking the folders.</summary>
    Running = 2,

    /// <summary>Every allowed folder was walked and what was found is stored. A folder the app couldn't
    /// list is a result of the scan, not a failure of it.</summary>
    Succeeded = 3,

    /// <summary>The scan stopped; its error says why. Nothing it found was stored.</summary>
    Failed = 4,
}
