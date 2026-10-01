namespace MediaAcquisitionManager.Core.Enums;

/// <summary>Where one inspection of the library's files stands (MAM-9). Stored by name.</summary>
public enum FileInspectionRunStatus
{
    /// <summary>Recorded and waiting for the worker; nothing has been read yet.</summary>
    Queued = 1,

    /// <summary>The worker is reading the files.</summary>
    Running = 2,

    /// <summary>Every file was looked at. A file the app could not read is a result of the run, not a
    /// failure of it.</summary>
    Succeeded = 3,

    /// <summary>The run stopped; its error says why. What it had already read is kept, file by
    /// file.</summary>
    Failed = 4,
}
