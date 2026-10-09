using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-24 — which replacement jobs are still going (ADR 0030–0034): the ones following their download, and the ones
/// that still hold their old copies. Pure lists, in the order a job goes through them.
/// </summary>
public static class ReplacementJobStates
{
    /// <summary>
    /// The states a replacement still follows its download in: until the download is done, and while it waits for the
    /// owner's review, which a cancelled or failed download still ends. A <see cref="ReplacementJobState.Downloaded"/>
    /// job waits for MAM-25's check, not for its download.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> Followed { get; } =
    [
        ReplacementJobState.Scheduled,
        ReplacementJobState.Submitted,
        ReplacementJobState.Downloading,
        ReplacementJobState.Paused,
        ReplacementJobState.NeedsReview,
    ];

    /// <summary>
    /// The states a replacement still holds its old copies in: every one but the two ends. A copy such a job holds
    /// can't be picked by another (<see cref="ReplacementCopyUnavailableReason.AlreadyScheduled"/>): two downloads would
    /// replace one copy, and the second would find it gone.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> Open { get; } =
    [
        ReplacementJobState.Scheduled,
        ReplacementJobState.Submitted,
        ReplacementJobState.Downloading,
        ReplacementJobState.Paused,
        ReplacementJobState.Downloaded,
        ReplacementJobState.NeedsReview,
    ];
}
