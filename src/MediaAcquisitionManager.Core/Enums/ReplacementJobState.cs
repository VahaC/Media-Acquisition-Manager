namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — where a scheduled replacement of old copies stands (ADR 0030–0034). It mirrors its download job until the
/// download is done; MAM-25 then checks the new files (<see cref="Inspecting"/>) and stops at <see cref="Verified"/>,
/// or at <see cref="NeedsReview"/> or <see cref="Failed"/> with the reason (ADR 0035). MAM-26 then moves the old copies
/// into the quarantine (<see cref="Quarantining"/>) and keeps them there (<see cref="Quarantined"/>) until the owner
/// puts them back (<see cref="Restoring"/>, then <see cref="Restored"/>) or deletes them for good
/// (<see cref="Deleting"/>, then <see cref="Completed"/>) (ADR 0030). MAM-27 — the owner may cancel it on its own before
/// anything of it moves (<see cref="Cancelled"/>, with Keep both). Stored by name, so a state added later needs no
/// migration and no value is ever reused.
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

    /// <summary>
    /// The replacement was cancelled, so nothing will be replaced: with its download, which was cancelled — or, MAM-27, on
    /// its own by the owner's Keep both, which leaves the download as it is, going on or done. <c>Reason</c> says which.
    /// </summary>
    Cancelled = 8,

    /// <summary>
    /// MAM-25 — the new files are being checked: complete in the client, readable as media, and identified by the
    /// library as the old copies' own works (ADR 0035). Nothing is touched; the job rests here between the verifier's
    /// passes, and its evidence says how far it got.
    /// </summary>
    Inspecting = 9,

    /// <summary>
    /// MAM-25 — every ticked copy is paired with a checked new file of its work (ADR 0032, ADR 0035). MAM-26 — the
    /// mover's queue: its next pass checks the old copies again and moves them into the quarantine
    /// (<see cref="Quarantining"/>), or stops for review before it renames anything; while replacement is switched off the
    /// job waits here, untouched (ADR 0033).
    /// </summary>
    Verified = 10,

    /// <summary>
    /// MAM-26 — the mover claimed the job and is renaming its names into the quarantine, one at a time, each with its
    /// intent and outcome rows (ADR 0030). A job found here at the start of a pass while no one in this process holds it
    /// was cut by a restart or a crash: the names are looked up by device and inode, and it stops for review.
    /// </summary>
    Quarantining = 11,

    /// <summary>
    /// MAM-26 — every ticked copy's video is in the quarantine, with the companions that could move (ADR 0030). It rests
    /// here, with no timer, until the owner restores it or deletes it for good.
    /// </summary>
    Quarantined = 12,

    /// <summary>MAM-26 — the owner's Delete for good removed every name the job held in the quarantine. An end.</summary>
    Completed = 13,

    /// <summary>MAM-26 — the owner's Restore put every name the job held in the quarantine back where it was: both copies
    /// are in the library again. An end.</summary>
    Restored = 14,

    /// <summary>
    /// MAM-26 — the owner's Restore is renaming the names back, inside the request. Found by a pass while no one in this
    /// process holds it, it was cut, and stops for review.
    /// </summary>
    Restoring = 15,

    /// <summary>
    /// MAM-26 — the owner's Delete for good is deleting the names from the quarantine, inside the request. Found by a pass
    /// while no one in this process holds it, it was cut, and stops for review.
    /// </summary>
    Deleting = 16,
}
