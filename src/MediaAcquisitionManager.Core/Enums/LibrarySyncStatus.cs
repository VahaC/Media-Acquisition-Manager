namespace MediaAcquisitionManager.Core.Enums;

/// <summary>Where one library sync stands (MAM-8). Stored by name.</summary>
public enum LibrarySyncStatus
{
    /// <summary>Recorded and waiting for the worker; nothing has been read yet.</summary>
    Queued = 1,

    /// <summary>The worker is reading the library.</summary>
    Running = 2,

    /// <summary>The library was read in full and its snapshot replaced.</summary>
    Succeeded = 3,

    /// <summary>The sync stopped; its error says why, and the stored snapshot is the one from before
    /// it.</summary>
    Failed = 4,
}
