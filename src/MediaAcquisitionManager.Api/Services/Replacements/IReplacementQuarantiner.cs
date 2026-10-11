namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — moves the old copies of every verified replacement into the quarantine (ADR 0030, ADR 0031, ADR 0033): the
/// ticked copy's video and its own companions, one <c>IFileQuarantine</c> rename per name, never a copy, never over a
/// name already there. Then the job rests at <c>Quarantined</c> until the owner restores it or deletes it for good, or it
/// stops at <c>NeedsReview</c> with the reason, nothing deleted.
/// </summary>
/// <remarks>
/// <para>
/// Each pass first settles the jobs a restart or a crash cut (<c>ReplacementJobStates.Unsettled</c> while no one in this
/// process holds them, <see cref="ReplacementJobsInHand"/>): every name in the middle of a write is looked up by device
/// and inode where it was and where it was going, recorded, and the job stops for review — it renames and deletes
/// nothing by itself. Then each <c>Verified</c> job, oldest first: while replacement is switched off it is left
/// untouched; otherwise the copies are checked again (check 5: still in a ticked folder, no disc clip, the same identity,
/// not a new file, the pair still holding), their companions listed, and every download client whose paths reach the
/// allowed roots read (check 6: a torrent that lists a video stops the job, one that lists a companion keeps that
/// companion where it is). A stop before the claim renames nothing and writes no name row.
/// </para>
/// <para>
/// Once claimed (<c>Quarantining</c>, the whole plan written as name rows), each name moves with an intent row committed
/// before the call and an outcome row after it, the switch and the ticks read again right before every rename. A video
/// that can't move stops the job with the names already moved left in the quarantine; a companion that can't move stays,
/// named on the job. After every move the library is told (<c>IReplacementLibraryNotice</c>). The app never writes to a
/// download client.
/// </para>
/// </remarks>
public interface IReplacementQuarantiner
{
    /// <summary>
    /// One pass: the cut jobs settled, every verified job taken one step on, and every library notice due sent, each job
    /// on its own — one that breaks is logged and the others go on. Only a cancellation by the caller propagates.
    /// <see cref="ReplacementQuarantineTracker"/> calls it on each of its passes.
    /// </summary>
    Task QuarantineAsync(CancellationToken cancellationToken = default);
}
