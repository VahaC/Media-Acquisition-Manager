using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — which of the owner's own actions a replacement offers (ADR 0030, ADR 0033): one rule each, read by the Jobs
/// line, the job's page and the actions themselves, so the page never offers a button the action then refuses, nor the
/// other way round. Pure: they judge what the caller read.
/// </summary>
public static class ReplacementJobOffers
{
    /// <summary>
    /// Retry: the job waits for review after the mover stopped it — its checks 5 and 6, a rename refused, or a move,
    /// restore or deletion cut —, which <paramref name="quarantineCheckedUtc"/> marks. A stop of MAM-25's check of the new
    /// files never sets it, and its Retry is MAM-27's.
    /// </summary>
    public static bool CanRetry(ReplacementJobState state, DateTime? quarantineCheckedUtc) =>
        state == ReplacementJobState.NeedsReview && quarantineCheckedUtc is not null;

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
