namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the quarantine's pass over removals (ADR 0030): every removal a restart or a crash cut in the middle of a write
/// — <c>Moving</c>, <c>Restoring</c> or <c>Deleting</c> while no one in this process holds it (<see cref="CopyRemovalsInHand"/>)
/// — settled from where its names are found by device and inode, as a replacement's recovery settles a job; then every
/// library notice a removal still owes. It renames and deletes nothing by itself, and decides nothing on a look that failed.
/// </summary>
public interface ICopyRemovalRecovery
{
    /// <summary>
    /// One pass: the cut removals settled and the notices due sent, each removal on its own — one that breaks is logged and
    /// the others go on. Only a cancellation by the caller propagates. <c>ReplacementQuarantineTracker</c> calls it on each
    /// of its passes, after the replacements'.
    /// </summary>
    Task SettleAsync(CancellationToken cancellationToken = default);
}
