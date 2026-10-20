using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the owner's writes on a replacement (ADR 0030): Retry, and the end of a Restore or a Delete for good, each
/// conditional on the state the request read, as every write of <see cref="ReplacementJobUpdates"/> is. The claim of a
/// Restore or a Delete for good is a plain <see cref="ReplacementJobUpdates.MoveAsync"/>. MAM-27 — the Retry of a stopped
/// check, Keep both and Drop keep theirs beside these (<c>ReplacementJobUpdates.Review.cs</c>).
/// </summary>
public static partial class ReplacementJobUpdates
{
    /// <summary>
    /// The owner's Retry of the replacement <paramref name="replacementJobId"/> at <paramref name="changedUtc"/>: back to
    /// <see cref="ReplacementJobState.Verified"/>, its reason gone, while it still waits for review after the mover
    /// stopped it (<see cref="ReplacementJobOffers.CanRetry(ReplacementStop?, int)"/>), so the mover's next pass checks it
    /// again. A review of MAM-25's check is never moved here: MAM-27 — its Retry goes back to that check
    /// (<see cref="RetryCheckAsync"/>). How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> RetryAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, DateTime changedUtc) =>
        jobs.Where(job => job.Id == replacementJobId && job.State == ReplacementJobState.NeedsReview && job.QuarantineCheckedUtc != null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Verified)
                    .SetProperty(job => job.Reason, (Message?)null)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);

    /// <summary>
    /// Ends the owner's Restore or Delete for good of the replacement <paramref name="replacementJobId"/>, read as
    /// <paramref name="observedState"/> (<c>Restoring</c> or <c>Deleting</c>), as <paramref name="state"/> at
    /// <paramref name="changedUtc"/> with <paramref name="reason"/>: the library owed a notice since
    /// <paramref name="libraryNoticeDueUtc"/> when given — one already owed is kept otherwise —, and a stop for review
    /// marked as the mover's stage (<c>QuarantineCheckedUtc</c>, kept when set), so the owner's Retry is offered for it.
    /// How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> EndOwnerWriteAsync(
        this IQueryable<ReplacementJobEntity> jobs,
        Guid replacementJobId,
        ReplacementJobState observedState,
        ReplacementJobState state,
        Message? reason,
        DateTime changedUtc,
        DateTime? libraryNoticeDueUtc)
    {
        DateTime? checkedUtc = state == ReplacementJobState.NeedsReview ? changedUtc : null;
        return jobs.Where(job => job.Id == replacementJobId && job.State == observedState)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, state)
                    .SetProperty(job => job.Reason, reason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc)
                    .SetProperty(job => job.QuarantineCheckedUtc, job => job.QuarantineCheckedUtc ?? checkedUtc)
                    .SetProperty(job => job.LibraryNoticeDueUtc, job => libraryNoticeDueUtc ?? job.LibraryNoticeDueUtc),
                CancellationToken.None);
    }
}
