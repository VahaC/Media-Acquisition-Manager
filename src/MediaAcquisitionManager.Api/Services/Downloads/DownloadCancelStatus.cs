namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>How a request to stop following a download went (MAM-14).</summary>
public enum DownloadCancelStatus
{
    /// <summary>The job is cancelled now, with its audit row; nothing was sent to the client.</summary>
    Cancelled = 1,

    /// <summary>The caller has no such job: there is none, or it is another user's.</summary>
    NotFound = 2,

    /// <summary>The job already ended — downloaded, failed or cancelled — so there is nothing to stop
    /// following; nothing changed.</summary>
    NothingToStop = 3,

    /// <summary>The job is still <c>Created</c> and younger than an interrupted send
    /// (<see cref="DownloadJobPoller.InterruptedSendAge"/>): its send may still be under way, so it can't be
    /// cancelled yet; nothing changed.</summary>
    StillBeingSent = 4,
}
