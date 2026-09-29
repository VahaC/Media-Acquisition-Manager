namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>A download client's job state, mapped from the client's own states.</summary>
public enum ClientJobState
{
    /// <summary>Waiting to start.</summary>
    Queued = 1,

    /// <summary>Downloading.</summary>
    Downloading = 2,

    /// <summary>Stopped by someone before it completed.</summary>
    Paused = 3,

    /// <summary>Complete and uploading to others.</summary>
    Seeding = 4,

    /// <summary>Complete.</summary>
    Completed = 5,

    /// <summary>The client reports an error.</summary>
    Errored = 6,

    /// <summary>A state this build doesn't know; never guessed into one it does.</summary>
    Unknown = 7,
}
