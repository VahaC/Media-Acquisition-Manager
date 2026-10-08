namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>MAM-38 — how the Download dialog's question about a destination's folder went.</summary>
public enum DownloadOverwriteCheckStatus
{
    /// <summary>Answered, the folder checkable or not.</summary>
    Answered = 1,

    /// <summary>No destination was named; the error says so.</summary>
    Invalid = 2,

    /// <summary>The destination no longer exists.</summary>
    DestinationMissing = 3,
}
