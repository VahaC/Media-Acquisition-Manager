namespace MediaAcquisitionManager.Core.Enums;

/// <summary>What started an inspection of the library's files (MAM-9). Stored by name.</summary>
public enum FileInspectionTrigger
{
    /// <summary>The installation owner asked for it on the Library page.</summary>
    Requested = 1,

    /// <summary>A library sync stored a new snapshot, which may name files not read yet.</summary>
    LibrarySynced = 2,

    /// <summary>The app started, and picks up whatever a stop interrupted or a file changed meanwhile.</summary>
    AppStarted = 3,
}
