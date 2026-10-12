using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-40 — which removals of a copy are still going: the ones holding names in the quarantine or writing for them, and the
/// ones a restart may have cut. Pure lists, as <see cref="ReplacementJobStates"/>' are.
/// </summary>
public static class CopyRemovalStates
{
    /// <summary>
    /// The states a removal still holds its copy in, which the Removed copies tab lists: moving it, keeping it in the
    /// quarantine, waiting for review, or putting it back or deleting it. A copy such a removal holds can't be removed or
    /// replaced again. <see cref="CopyRemovalState.Restored"/>, <see cref="CopyRemovalState.Deleted"/> and
    /// <see cref="CopyRemovalState.NotMoved"/> are ends, kept as history with their audit rows.
    /// </summary>
    public static IReadOnlyList<CopyRemovalState> Open { get; } =
    [
        CopyRemovalState.Moving,
        CopyRemovalState.Quarantined,
        CopyRemovalState.NeedsReview,
        CopyRemovalState.Restoring,
        CopyRemovalState.Deleting,
    ];

    /// <summary>
    /// The states a write to the media is under way in, each inside its request. A removal found in one while no one in
    /// this process holds it was cut by a restart or a crash; the quarantine's pass looks its names up and settles it.
    /// </summary>
    public static IReadOnlyList<CopyRemovalState> Unsettled { get; } =
    [
        CopyRemovalState.Moving,
        CopyRemovalState.Restoring,
        CopyRemovalState.Deleting,
    ];
}
