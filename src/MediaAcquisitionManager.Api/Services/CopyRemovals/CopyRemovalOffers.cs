using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — which of the owner's actions a removal offers (ADR 0030): one rule each, read by the Removed copies list and the
/// actions themselves, so the page never offers a button the action then refuses, nor the other way round. Pure: they
/// judge what the caller read. A replacement's own rules (<c>ReplacementJobOffers</c>) with one deviation: a removal has no
/// Retry, so one waiting for review may be deleted for good too — or a Delete for good cut after its video would leave the
/// rest of it in the quarantine for ever.
/// </summary>
public static class CopyRemovalOffers
{
    /// <summary>
    /// Restore: the removal rests in the quarantine, or waits for review, and some of its names are in the quarantine
    /// (<paramref name="holdsQuarantinedNames"/>) — but its video wasn't deleted for good (<paramref name="videoDeleted"/>):
    /// a subtitle or an .nfo would go back alone, and the copy would read as restored.
    /// </summary>
    public static bool CanRestore(CopyRemovalState state, bool holdsQuarantinedNames, bool videoDeleted) =>
        (state is CopyRemovalState.Quarantined or CopyRemovalState.NeedsReview) && holdsQuarantinedNames && !videoDeleted;

    /// <summary>Delete for good: the removal rests in the quarantine or waits for review — no write under way — and holds
    /// names there (<paramref name="holdsQuarantinedNames"/>) to delete.</summary>
    public static bool CanDeleteForGood(CopyRemovalState state, bool holdsQuarantinedNames) =>
        (state is CopyRemovalState.Quarantined or CopyRemovalState.NeedsReview) && holdsQuarantinedNames;
}
