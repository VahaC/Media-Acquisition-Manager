namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — where a scheduled replacement of old copies stands (ADR 0030–0034). It mirrors its download job until the
/// download is done; MAM-25 then checks the new files (<see cref="Inspecting"/>) and stops at <see cref="Verified"/>,
/// or at <see cref="NeedsReview"/> or <see cref="Failed"/> with the reason (ADR 0035). Moving the old copy aside is
/// MAM-26's, which adds its own states. Stored by name, so a state added later needs no migration and no value is ever
/// reused.
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

    /// <summary>The download is done; the check of the new files starts with the next pass of MAM-25's verifier
    /// (<see cref="Inspecting"/>).</summary>
    Downloaded = 5,

    /// <summary>The app can't tell how to go on without the owner; <c>Reason</c> says why.</summary>
    NeedsReview = 6,

    /// <summary>The download failed or was refused before it was sent — or, MAM-25, a new file is no usable media file —,
    /// so nothing will be replaced; <c>Reason</c> says why.</summary>
    Failed = 7,

    /// <summary>The download was cancelled, so nothing will be replaced.</summary>
    Cancelled = 8,

    /// <summary>
    /// MAM-25 — the new files are being checked: complete in the client, readable as media, and identified by the
    /// library as the old copies' own works (ADR 0035). Nothing is touched; the job rests here between the verifier's
    /// passes, and its evidence says how far it got.
    /// </summary>
    Inspecting = 9,

    /// <summary>
    /// MAM-25 — every ticked copy is paired with a checked new file of its work (ADR 0032, ADR 0035). This version moves
    /// nothing: moving the old copies aside is MAM-26's.
    /// </summary>
    Verified = 10,
}
