using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — which of the owner's own actions a replacement offers (ADR 0030, ADR 0033): one rule each, read by the Jobs
/// line, the job's page and the actions themselves, so the page never offers a button the action then refuses, nor the
/// other way round. Pure: they judge what the caller read.
/// </summary>
public static class ReplacementJobOffers
{
    /// <summary>
    /// Retry of a job stopped for review at <paramref name="stop"/> (<see cref="ReplacementJobProgress.StopOf"/>): the
    /// mover's stop goes back to the mover's queue (MAM-26); a stop of the check of the new files goes back to that check
    /// (MAM-27), unless an undropped copy has a problem only a drop settles (<paramref name="dropOnlyCopyCount"/>), which a
    /// new check would only stop on again. A download the app can't follow has nothing to check again, and no other state
    /// is a review.
    /// </summary>
    public static bool CanRetry(ReplacementStop? stop, int dropOnlyCopyCount) =>
        stop == ReplacementStop.Move || (stop == ReplacementStop.Check && dropOnlyCopyCount == 0);

    /// <summary>
    /// MAM-27 — Keep both, which cancels only the replacement: in any state before an old copy starts to move
    /// (<see cref="ReplacementJobStates.BeforeMove"/>), while none of its names left its folder
    /// (<paramref name="holdsTakenNames"/>, <see cref="ReplacementNameStates.Taken"/>) — what did comes back through
    /// Restore. Usable while replacement is switched off: it writes nothing to the media.
    /// </summary>
    public static bool CanCancel(ReplacementJobState state, bool holdsTakenNames) =>
        ReplacementJobStates.BeforeMove.Contains(state) && !holdsTakenNames;

    /// <summary>
    /// MAM-27 — Drop of one ticked copy (Vasyl, 2026-10-11): only from a replacement that stopped for review at the check of
    /// its new files or by the mover (<paramref name="stop"/>) — never under a check or a move that runs, nor from a
    /// download the app can't follow, where dropping changes nothing —, a copy not <paramref name="dropped"/> already, none
    /// of whose names left its folder (<paramref name="holdsTakenNames"/>), and never the last copy the job would replace
    /// (<paramref name="undroppedCopyCount"/> counts it): that is Keep both.
    /// </summary>
    public static bool CanDrop(ReplacementJobState state, ReplacementStop? stop, bool dropped, bool holdsTakenNames, int undroppedCopyCount) =>
        state == ReplacementJobState.NeedsReview
        && stop is (ReplacementStop.Check or ReplacementStop.Move)
        && !dropped
        && !holdsTakenNames
        && undroppedCopyCount > 1;

    /// <summary>
    /// Restore: the job rests in the quarantine, or stopped for review, and some of its names are in the quarantine
    /// (<paramref name="holdsQuarantinedNames"/>) to put back — but none of its videos was deleted for good
    /// (<paramref name="videoDeleted"/>), by a Delete for good that stopped part-way: what is left of that copy would go back
    /// alone, and the job would end saying the old copy is back. Retry settles such a job where it rests, and Delete for
    /// good takes the rest.
    /// </summary>
    public static bool CanRestore(ReplacementJobState state, bool holdsQuarantinedNames, bool videoDeleted) =>
        (state is ReplacementJobState.Quarantined or ReplacementJobState.NeedsReview) && holdsQuarantinedNames && !videoDeleted;

    /// <summary>Delete for good: the job rests in the quarantine — every copy moved, nothing in the middle of a write — and
    /// holds names there (<paramref name="holdsQuarantinedNames"/>) to delete.</summary>
    public static bool CanDeleteForGood(ReplacementJobState state, bool holdsQuarantinedNames) =>
        state == ReplacementJobState.Quarantined && holdsQuarantinedNames;
}
