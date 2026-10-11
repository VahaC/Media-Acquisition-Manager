using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-24 — which replacement jobs are still going (ADR 0030–0034): the ones following their download, the ones whose
/// new files MAM-25 checks (ADR 0035), the ones MAM-26's mover takes up and the ones a restart may have cut (ADR 0030),
/// and the ones that still hold their old copies. Pure lists, in the order a job goes through them.
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
    /// MAM-26 — the mover's queue (ADR 0030): a <see cref="ReplacementJobState.Verified"/> job whose old copies it moves
    /// next, and a <see cref="ReplacementJobState.Quarantining"/> one it is moving. Never <see cref="Followed"/> nor
    /// <see cref="Verifying"/>: those passes would move it back.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> Quarantine { get; } =
    [
        ReplacementJobState.Verified,
        ReplacementJobState.Quarantining,
    ];

    /// <summary>
    /// MAM-26 — the states a write to the media is under way in: the mover's renames into the quarantine, and the
    /// owner's Restore and Delete for good, each inside its request. A job found in one while no one in this process
    /// holds it was cut by a restart or a crash; the mover's recovery looks its names up and stops it for review.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> Unsettled { get; } =
    [
        ReplacementJobState.Quarantining,
        ReplacementJobState.Restoring,
        ReplacementJobState.Deleting,
    ];

    /// <summary>
    /// The states a replacement still holds its old copies in: every one but the ends. A copy such a job holds can't be
    /// picked by another (<see cref="ReplacementCopyUnavailableReason.AlreadyScheduled"/>): two downloads would replace
    /// one copy, and the second would find it gone. MAM-25 — a job being checked or verified still holds them. MAM-26 —
    /// and one moving them, keeping them in the quarantine, or putting them back or deleting them; a job whose names are
    /// all deleted (<see cref="ReplacementJobState.Completed"/>) or all back (<see cref="ReplacementJobState.Restored"/>)
    /// holds nothing any more.
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
        ReplacementJobState.Quarantining,
        ReplacementJobState.Quarantined,
        ReplacementJobState.Restoring,
        ReplacementJobState.Deleting,
        ReplacementJobState.NeedsReview,
    ];

    /// <summary>
    /// MAM-27 — the states before any old copy starts to move, which the owner's Keep both cancels the replacement in (a
    /// review only while none of its names left its folder, <see cref="ReplacementNameStates.Taken"/>). Never
    /// <see cref="ReplacementJobState.Quarantining"/>, <see cref="ReplacementJobState.Restoring"/> or
    /// <see cref="ReplacementJobState.Deleting"/> — a write under way, or a cut the mover's recovery settles —, never
    /// <see cref="ReplacementJobState.Quarantined"/> — Restore is the way back from there —, and never an end. Read only as
    /// a constant in a query's <c>WHERE</c>: it builds no index, so it changes no migration.
    /// </summary>
    public static IReadOnlyList<ReplacementJobState> BeforeMove { get; } =
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
