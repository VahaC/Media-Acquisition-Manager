using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the writes that move a scheduled replacement on, each a conditional <c>UPDATE</c> of a job still in the state
/// its writer read (the <c>DownloadJobUpdates</c> pattern): whoever writes first wins — the follower mirroring the
/// download, or the owner's cancel — and the other's write, made against a state the job no longer has, changes nothing.
/// </summary>
/// <remarks>
/// Never cancelled, the <c>DownloadJobUpdates</c> rule: once a state is decided it is written, even while the app shuts
/// down. The reason is cut to its column with an ellipsis, so the reader sees it was cut.
/// </remarks>
public static class ReplacementJobUpdates
{
    /// <summary>
    /// Moves the replacement <paramref name="replacementJobId"/>, read as <paramref name="observedState"/>, to
    /// <paramref name="state"/> at <paramref name="changedUtc"/>, with why it stopped in <paramref name="reason"/> or none;
    /// how many it wrote (0 or 1).
    /// </summary>
    public static Task<int> MoveAsync(
        this IQueryable<ReplacementJobEntity> jobs,
        Guid replacementJobId,
        ReplacementJobState observedState,
        ReplacementJobState state,
        string? reason,
        DateTime changedUtc)
    {
        var storedReason = Stored(reason);
        return jobs.Where(job => job.Id == replacementJobId && job.State == observedState)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, state)
                    .SetProperty(job => job.Reason, storedReason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);
    }

    /// <summary>
    /// Cancels the replacement <paramref name="replacementJobId"/> at <paramref name="changedUtc"/> with
    /// <paramref name="reason"/> while it is still followed (<see cref="ReplacementJobStates.Followed"/>), whatever state
    /// it has by then — the cancel of its download wins over a pass that moved it meanwhile, as that cancel itself does —;
    /// how many it wrote (0 or 1).
    /// </summary>
    public static Task<int> CancelAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, string reason, DateTime changedUtc)
    {
        var storedReason = Stored(reason);
        return jobs.Where(job => job.Id == replacementJobId && EF.Constant(ReplacementJobStates.Followed).Contains(job.State))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Cancelled)
                    .SetProperty(job => job.Reason, storedReason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);
    }

    private static string? Stored(string? reason) =>
        reason is null || reason.Length <= ReplacementJobEntity.ReasonMaximumLength
            ? reason
            : string.Concat(reason.AsSpan(0, ReplacementJobEntity.ReasonMaximumLength - 1), "…");
}
