using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the writes that move a scheduled replacement on, each a conditional <c>UPDATE</c> of a job still in the state
/// its writer read (the <c>DownloadJobUpdates</c> pattern): whoever writes first wins — the follower mirroring the
/// download, MAM-25's verifier checking its new files, or the owner's cancel — and the other's write, made against a
/// state the job no longer has, changes nothing.
/// </summary>
/// <remarks>
/// <para>
/// Never cancelled, the <c>DownloadJobUpdates</c> rule: once a state is decided it is written, even while the app shuts
/// down. MAM-47 (ADR 0042) — the reason is a message, which the column's converter fits into its 1000 characters by
/// cutting its longest value with an ellipsis, so the reader sees what was cut.
/// </para>
/// <para>
/// MAM-26 — partial: the mover's writes and the owner's Restore, Delete for good and Retry each keep theirs in a file of
/// their own beside these.
/// </para>
/// </remarks>
public static partial class ReplacementJobUpdates
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
        Message? reason,
        DateTime changedUtc) =>
        jobs.Where(job => job.Id == replacementJobId && job.State == observedState)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, state)
                    .SetProperty(job => job.Reason, reason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);

    /// <summary>
    /// Cancels the replacement <paramref name="replacementJobId"/> at <paramref name="changedUtc"/> with
    /// <paramref name="reason"/> while it is still followed (<see cref="ReplacementJobStates.Followed"/>), whatever state
    /// it has by then — the cancel of its download wins over a pass that moved it meanwhile, as that cancel itself does —;
    /// how many it wrote (0 or 1).
    /// </summary>
    public static Task<int> CancelAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, Message reason, DateTime changedUtc) =>
        jobs.Where(job => job.Id == replacementJobId && EF.Constant(ReplacementJobStates.Followed).Contains(job.State))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Cancelled)
                    .SetProperty(job => job.Reason, reason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);

    /// <summary>
    /// MAM-25 — starts the check of the replacement <paramref name="replacementJobId"/>'s new files while it is still
    /// <see cref="ReplacementJobState.Downloaded"/>: <see cref="ReplacementJobState.Inspecting"/> at
    /// <paramref name="startedUtc"/>, the files' wait reckoned from then, and the library not asked yet — a check started
    /// again (MAM-27's Retry puts a job back to Downloaded) begins with the files, as the first did. How many it wrote
    /// (0 or 1).
    /// </summary>
    public static Task<int> StartCheckAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, DateTime startedUtc) =>
        jobs.Where(job => job.Id == replacementJobId && job.State == ReplacementJobState.Downloaded)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Inspecting)
                    .SetProperty(job => job.Reason, (Message?)null)
                    .SetProperty(job => job.StateChangedUtc, startedUtc)
                    .SetProperty(job => job.InspectionStartedUtc, startedUtc)
                    .SetProperty(job => job.LibraryAskedUtc, (DateTime?)null),
                CancellationToken.None);

    /// <summary>
    /// MAM-25 — claims the replacement <paramref name="replacementJobId"/>, read as <paramref name="observedState"/>, for
    /// writes that go with it in the caller's transaction — its check's evidence — and changes nothing: an <c>UPDATE</c>
    /// of its state to itself (<c>DownloadJobUpdates.ClaimAsync</c>'s pattern). It holds SQLite's write lock until the
    /// caller commits and counts the job only while it still has the state the pass read, so evidence is never written
    /// over a job a cancel or a retry moved meanwhile. How many it claimed (0 or 1).
    /// </summary>
    public static Task<int> ClaimAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, ReplacementJobState observedState) =>
        jobs.Where(job => job.Id == replacementJobId && job.State == observedState)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.State, observedState), CancellationToken.None);

    /// <summary>
    /// MAM-25 — records that the library was asked at <paramref name="askedUtc"/> to look at the new files of the
    /// replacement <paramref name="replacementJobId"/>, while it is <see cref="ReplacementJobState.Inspecting"/> and the
    /// library wasn't asked yet: the library's wait is reckoned from the first request it took, never moved by a later
    /// one. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordLibraryAskedAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, DateTime askedUtc) =>
        jobs.Where(job => job.Id == replacementJobId && job.State == ReplacementJobState.Inspecting && job.LibraryAskedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.LibraryAskedUtc, askedUtc), CancellationToken.None);
}
