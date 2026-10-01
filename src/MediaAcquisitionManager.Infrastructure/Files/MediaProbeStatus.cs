namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>How a run of the media inspector went (MAM-9).</summary>
internal enum MediaProbeStatus
{
    /// <summary>It finished and printed its answer.</summary>
    Succeeded = 1,

    /// <summary>It ran but gave no usable answer: it failed on the file, ran out of time or said too much.
    /// A fact about the file.</summary>
    Failed = 2,

    /// <summary>It couldn't be started. A fact about the app's host, not about the file.</summary>
    Unavailable = 3,
}
