using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// MAM-27 — the owner's writes on a replacement that waits for him (ADR 0035, ADR 0032): the Retry of a stopped check of
// its new files, Keep both, and Drop of one ticked copy. Each decisive guard sits inside its one conditional statement,
// so a follower, a verifier or a mover pass that moved the job after the request read it is never written over: the
// write counts 0 rows, and the action answers that the job changed meanwhile.
public static partial class ReplacementJobUpdates
{
    /// <summary>
    /// The owner's Retry of the replacement <paramref name="replacementJobId"/> after the check of its new files stopped:
    /// back to <see cref="ReplacementJobState.Downloaded"/> at <paramref name="changedUtc"/>, its reason gone, so the
    /// verifier's next pass claims it as it claims any finished download (<see cref="StartCheckAsync"/>) and checks the
    /// files again from the start. Only while it still waits for review at that check — never the mover's stop
    /// (<c>QuarantineCheckedUtc</c> set), never one whose check didn't start (<c>InspectionStartedUtc</c> unset) —, while
    /// its download in <paramref name="downloads"/> is still done and passed the strict check (the verifier reads nothing
    /// else), and while none of its undropped copies in <paramref name="copies"/> has a problem only a drop settles, which a
    /// new check would only stop on again. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> RetryCheckAsync(
        this IQueryable<ReplacementJobEntity> jobs,
        IQueryable<DownloadJobEntity> downloads,
        IQueryable<ReplacementJobFileEntity> copies,
        Guid replacementJobId,
        DateTime changedUtc) =>
        jobs.Where(job => job.Id == replacementJobId
                && job.State == ReplacementJobState.NeedsReview
                && job.QuarantineCheckedUtc == null
                && job.InspectionStartedUtc != null
                && downloads.Any(download => download.Id == job.DownloadJobId
                    && download.Status == DownloadJobStatus.Downloaded
                    && download.OverwriteCheck == DownloadOverwriteCheck.Checked)
                && !copies.Any(copy => copy.ReplacementJobId == job.Id && copy.DroppedUtc == null && copy.OnlyDropSettles))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Downloaded)
                    .SetProperty(job => job.Reason, (string?)null)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);

    /// <summary>
    /// The owner's Keep both: cancels only the replacement <paramref name="replacementJobId"/> at
    /// <paramref name="changedUtc"/> with <paramref name="reason"/>, while it is in a state before any old copy starts to
    /// move (<see cref="ReplacementJobStates.BeforeMove"/>) and none of its names in <paramref name="names"/> left its
    /// folder (<see cref="ReplacementNameStates.Taken"/>) — what did comes back through Restore, never by forgetting it.
    /// Its download is no part of it: <see cref="CancelAsync"/>, the download's own cancel, stays as it is. How many it
    /// wrote (0 or 1).
    /// </summary>
    public static Task<int> CancelByOwnerAsync(
        this IQueryable<ReplacementJobEntity> jobs,
        IQueryable<ReplacementJobNameEntity> names,
        Guid replacementJobId,
        string reason,
        DateTime changedUtc)
    {
        var storedReason = Stored(reason);
        return jobs.Where(job => job.Id == replacementJobId
                && EF.Constant(ReplacementJobStates.BeforeMove).Contains(job.State)
                && !names.Any(name => name.ReplacementJobId == job.Id && EF.Constant(ReplacementNameStates.Taken).Contains(name.State)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Cancelled)
                    .SetProperty(job => job.Reason, storedReason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc),
                CancellationToken.None);
    }

    /// <summary>
    /// Claims the replacement <paramref name="replacementJobId"/> for the owner's Drop of one of its copies, while it still
    /// waits for review at a stop a drop can change — the check of its new files or the mover's, never the download's
    /// following, whose check never started —, and changes nothing: an <c>UPDATE</c> of its state to itself
    /// (<see cref="ClaimAsync"/>'s pattern). It holds SQLite's write lock until the caller commits, so what the caller reads
    /// next — the copy, its names, the copies left — can't change before its own writes. How many it claimed (0 or 1).
    /// </summary>
    /// <remarks>Vasyl, 2026-10-11: a copy is dropped only from a replacement that stopped for review, so no state list is
    /// taken: nothing changes under a check or a move that runs.</remarks>
    public static Task<int> ClaimForDropAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId) =>
        jobs.Where(job => job.Id == replacementJobId
                && job.State == ReplacementJobState.NeedsReview
                && (job.InspectionStartedUtc != null || job.QuarantineCheckedUtc != null))
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.State, ReplacementJobState.NeedsReview), CancellationToken.None);

    /// <summary>
    /// Drops the ticked copy <paramref name="copyId"/> of the replacement <paramref name="replacementJobId"/> at
    /// <paramref name="droppedUtc"/>, while it isn't dropped yet: its own column, never cleared, so no later check pairs it,
    /// waits on it or moves it. Its problem and drop-only mark stay as its history. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> DropAsync(this IQueryable<ReplacementJobFileEntity> copies, Guid replacementJobId, Guid copyId, DateTime droppedUtc) =>
        copies.Where(copy => copy.Id == copyId && copy.ReplacementJobId == replacementJobId && copy.DroppedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.DroppedUtc, (DateTime?)droppedUtc), CancellationToken.None);

    /// <summary>
    /// Deletes the names of the replacement <paramref name="replacementJobId"/> — of its copy <paramref name="copyId"/>
    /// only, when given — that never left their folders: planned and not moved yet, or stayed. On a job no longer going
    /// on, or a copy out of it, such a row would read "waits to move" for ever; the mover's own new plan deletes them so
    /// too. A name put back or lost is kept, as what became of it. How many it deleted.
    /// </summary>
    public static Task<int> ForgetNeverMovedNamesAsync(this IQueryable<ReplacementJobNameEntity> names, Guid replacementJobId, Guid? copyId)
    {
        var ofJob = names.Where(name => name.ReplacementJobId == replacementJobId
            && (name.State == ReplacementNameState.Planned || name.State == ReplacementNameState.Stayed));
        return (copyId is { } copy ? ofJob.Where(name => name.ReplacementJobFileId == copy) : ofJob)
            .ExecuteDeleteAsync(CancellationToken.None);
    }
}
