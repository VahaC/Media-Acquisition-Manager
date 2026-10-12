using System.Linq.Expressions;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-41 — which of the Jobs page's tabs a job falls in (<see cref="DownloadJobView"/>), as a predicate the database
/// applies: the list is filtered, counted and paged by the server, never sorted out of every job in memory. The first tab
/// that fits wins — NeedsYou, then InProgress, then Finished —, so each job is in exactly one.
/// </summary>
/// <remarks>
/// <para>
/// NeedsYou: the download needs linking, or its replacement waits for the owner's review, rests in the quarantine until he
/// restores or deletes it, or is verified while replacement is switched off — the mover takes it up only once he switches it
/// on (ADR 0033). InProgress: the download is still followed (<see cref="DownloadJobFollowing.FollowedStatuses"/>), or its
/// replacement's new files are checked (<see cref="ReplacementJobStates.Verifying"/>), it is verified while the switch is
/// on, or a move, a Restore or a Delete for good runs (<see cref="ReplacementJobStates.Unsettled"/>). The Jobs page's
/// <c>isJobWorkedOn</c> judges a row by the same states, so a row that polls is never in a tab that says it waits for you.
/// </para>
/// <para>
/// The switch is the installation's one master switch, read once by the caller and built into the query as a constant —
/// one read per request, never one per job. A job carries at most one replacement (its unique index), so "its replacement
/// is in one of these states" is an <c>EXISTS</c>. A member's job carries none and falls by its status alone.
/// </para>
/// </remarks>
public static class DownloadJobViews
{
    // A review, and a rest in the quarantine until the owner restores or deletes: they wait for him whatever the switch says.
    private static readonly ReplacementJobState[] WaitingWhileSwitchedOn = [ReplacementJobState.NeedsReview, ReplacementJobState.Quarantined];

    // Verified too: nothing moves while replacement is off, so it waits for the owner to switch it on.
    private static readonly ReplacementJobState[] WaitingWhileSwitchedOff =
        [ReplacementJobState.NeedsReview, ReplacementJobState.Quarantined, ReplacementJobState.Verified];

    private static readonly ReplacementJobState[] WorkedOnWhileSwitchedOn =
    [
        ReplacementJobState.Downloaded,
        ReplacementJobState.Inspecting,
        ReplacementJobState.Verified,
        ReplacementJobState.Quarantining,
        ReplacementJobState.Restoring,
        ReplacementJobState.Deleting,
    ];

    // The check of the new files goes on while the switch is off (ADR 0033): it writes nothing to the media.
    private static readonly ReplacementJobState[] WorkedOnWhileSwitchedOff =
    [
        ReplacementJobState.Downloaded,
        ReplacementJobState.Inspecting,
        ReplacementJobState.Quarantining,
        ReplacementJobState.Restoring,
        ReplacementJobState.Deleting,
    ];

    /// <summary>
    /// The jobs of <paramref name="view"/>, judged against <paramref name="replacements"/> — the replacement jobs the
    /// query reads — with replacement switched on or off as <paramref name="replacementSwitchedOn"/> says.
    /// </summary>
    public static Expression<Func<DownloadJobEntity, bool>> Of(
        DownloadJobView view, IQueryable<ReplacementJobEntity> replacements, bool replacementSwitchedOn)
    {
        var waiting = replacementSwitchedOn ? WaitingWhileSwitchedOn : WaitingWhileSwitchedOff;
        var workedOn = replacementSwitchedOn ? WorkedOnWhileSwitchedOn : WorkedOnWhileSwitchedOff;
        // Every list a constant: two query shapes in all, one per position of the switch.
        return view switch
        {
            DownloadJobView.NeedsYou => job =>
                job.Status == DownloadJobStatus.NeedsLinking
                || replacements.Any(replacement => replacement.DownloadJobId == job.Id && EF.Constant(waiting).Contains(replacement.State)),
            DownloadJobView.InProgress => job =>
                job.Status != DownloadJobStatus.NeedsLinking
                && !replacements.Any(replacement => replacement.DownloadJobId == job.Id && EF.Constant(waiting).Contains(replacement.State))
                && (EF.Constant(DownloadJobFollowing.FollowedStatuses).Contains(job.Status)
                    || replacements.Any(replacement => replacement.DownloadJobId == job.Id && EF.Constant(workedOn).Contains(replacement.State))),
            DownloadJobView.Finished => job =>
                job.Status != DownloadJobStatus.NeedsLinking
                && !EF.Constant(DownloadJobFollowing.FollowedStatuses).Contains(job.Status)
                && !replacements.Any(replacement => replacement.DownloadJobId == job.Id
                    && (EF.Constant(waiting).Contains(replacement.State) || EF.Constant(workedOn).Contains(replacement.State))),
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Not a view of the jobs."),
        };
    }
}
