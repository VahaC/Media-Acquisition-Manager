namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>How storing a library snapshot ended.</summary>
public enum LibrarySnapshotStatus
{
    /// <summary>The snapshot was replaced and the run marked succeeded, in one commit.</summary>
    Applied = 1,

    /// <summary>The integration was removed while its library was being read; nothing was written.</summary>
    SourceRemoved = 2,

    /// <summary>The run was no longer running — something else finished it; nothing was written, so the
    /// stored snapshot and the run's outcome never disagree.</summary>
    RunNoLongerRunning = 3,
}
