namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — where a scheduled replacement of old copies stands (ADR 0030–0034). It mirrors its download job until the
/// download is done, and waits there: verifying the new file and moving the old copy aside are later tickets'
/// (MAM-25, MAM-26), which add their own states. Stored by name, so a state added later needs no migration and no
/// value is ever reused.
/// </summary>
public enum ReplacementJobState
{
    /// <summary>Written with its download job, before anything is fetched or sent (ADR 0013).</summary>
    Scheduled = 1,

    /// <summary>The download client took the release, after the strict check before the send passed (ADR 0034).</summary>
    Submitted = 2,

    /// <summary>The client is downloading it.</summary>
    Downloading = 3,

    /// <summary>The download is paused in the client; it is still followed.</summary>
    Paused = 4,

    /// <summary>The download is done. This version waits here: nothing is verified or moved yet.</summary>
    Downloaded = 5,

    /// <summary>The app can't tell how to go on without the owner; <c>Reason</c> says why.</summary>
    NeedsReview = 6,

    /// <summary>The download failed or was refused before it was sent, so nothing will be replaced; <c>Reason</c>
    /// says why.</summary>
    Failed = 7,

    /// <summary>The download was cancelled, so nothing will be replaced.</summary>
    Cancelled = 8,
}
