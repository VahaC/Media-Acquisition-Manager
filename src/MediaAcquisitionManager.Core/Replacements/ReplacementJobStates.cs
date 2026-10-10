using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-24 — which replacement jobs are still going (ADR 0030–0034): the ones following their download, the ones whose
/// new files MAM-25 checks (ADR 0035), and the ones that still hold their old copies. Pure lists, in the order a job
/// goes through them.
/// </summary>
public static class ReplacementJobStates
{
    /// <summary>
    /// The states a replacement still follows its download in: until the download is done, and while it waits for the
    /// owner's review, which a cancelled or failed download still ends. A <see cref="ReplacementJobState.Downloaded"/>
    /// job waits for MAM-25's check, not for its download — and <see cref="ReplacementJobState.Inspecting"/> is never
    /// followed: its download is done, so the following pass would mirror it back to Downloaded on every pass.
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
    /// MAM-25 — the states whose new files the verifier checks (ADR 0035): a <see cref="ReplacementJobState.Downloaded"/>
    /// job it hasn't claimed yet, and an <see cref="ReplacementJobState.Inspecting"/> one it goes on with from its rows on
    /// every pass, so a restart loses nothing.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> Verifying { get; } =
    [
        ReplacementJobState.Downloaded,
        ReplacementJobState.Inspecting,
    ];

    /// <summary>
    /// The states a replacement still holds its old copies in: every one but the two ends. A copy such a job holds
    /// can't be picked by another (<see cref="ReplacementCopyUnavailableReason.AlreadyScheduled"/>): two downloads would
    /// replace one copy, and the second would find it gone. MAM-25 — a job being checked or verified still holds them:
    /// they move only in MAM-26.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> Open { get; } =
    [
        ReplacementJobState.Scheduled,
        ReplacementJobState.Submitted,
        ReplacementJobState.Downloading,
        ReplacementJobState.Paused,
        ReplacementJobState.Downloaded,
        ReplacementJobState.Inspecting,
        ReplacementJobState.Verified,
        ReplacementJobState.NeedsReview,
    ];
}
