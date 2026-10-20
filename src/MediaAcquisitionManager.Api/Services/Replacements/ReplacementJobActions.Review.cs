using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-27 — the owner's way out of a replacement that waits for him: Keep both, which cancels only the replacement, and
/// Drop of one ticked copy, which stays where it is. Each one database transaction, writing nothing to the media or a
/// download client.
/// </summary>
/// <remarks>
/// Each in MAM-26's order: the owned job (else not found) — for Drop, the copy of that very job —, the offer rule on the
/// rows as read (refused with its sentence), the job taken in hand, then one transaction — the conditional write, which
/// counts 0 rows for a job that moved on meanwhile, and the audit row — committed before the answer.
/// </remarks>
public sealed partial class ReplacementJobActions
{
    // MAM-27 — the replacement's own reason after Keep both: both copies stay, and the download is left as it is.
    private static readonly Message KeptBothReason = Message.Of("replacements.keep-both.reason");

    private static readonly Message AlreadyEndedError = Message.Of("replacements.keep-both.ended");

    // MAM-27 — a name that left its folder comes back through Restore, never by forgetting the job that holds it.
    private static readonly Message LeftFoldersError = Message.Of("replacements.keep-both.left-folders");

    // MAM-27, Vasyl 2026-10-11 — a copy is dropped only from a replacement that stopped for review: nothing changes under a
    // check or a move that runs.
    private static readonly Message NotStoppedForReviewDropError = Message.Of("replacements.drop.not-in-review");

    private static readonly Message DownloadNotFollowedDropError = Message.Of("replacements.drop.download-not-followed");

    public async Task<ReplacementActionResult> CancelAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return ReplacementActionResult.NotFound;
        if (CancelRefusal(job.Replacement.State, await HoldsTakenNamesAsync(replacementJobId, null, cancellationToken)) is { } refusal)
            return ReplacementActionResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(replacementJobId))
            return ReplacementActionResult.Refused(BeingWorkedOnError);
        try
        {
            // Only the replacement's row: its download is no part of it — no write to the download job, no
            // DownloadCancelled row, nothing sent to the client —, and Cancel job still ends both while the download is
            // followed. A follower, verifier or mover pass that reads the job after this commit finds it Cancelled, in no
            // queue of theirs; one that read it before writes 0 rows, every write of theirs being conditional on the state
            // it read.
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var now = time.GetUtcNow().UtcDateTime;
            // The state it leaves, read under the transaction's write lock: the write takes any state before a move, and the
            // follower or the verifier, which never take a job in hand, may have moved it on since the read above.
            var from = await StateNowAsync(replacementJobId);
            if (await db.ReplacementJobs.CancelByOwnerAsync(db.ReplacementJobNames, replacementJobId, KeptBothReason, now) == 0)
                return ReplacementActionResult.Refused(await CancelRefusalNowAsync(replacementJobId) ?? ChangedMeanwhileError);

            // Names never moved would read "waits to move" on a job that has ended; a name put back or lost stays as history.
            await db.ReplacementJobNames.ForgetNeverMovedNamesAsync(replacementJobId, null);
            auditTrail.Record(AuditAction.ReplacementCancelledAlone, actor, TargetOf(job));
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            logger.LogInformation(
                "Replacement job {ReplacementJobId} went from {From} to {To} by its owner's Keep both; its download is left as it is.",
                replacementJobId,
                from,
                ReplacementJobState.Cancelled);
        }
        finally
        {
            inHand.Release(replacementJobId);
        }

        return await AnswerAsync(job, callerUserId);
    }

    public async Task<ReplacementActionResult> DropCopyAsync(
        Guid replacementJobId, Guid copyId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return ReplacementActionResult.NotFound;
        // A copy of another job — the caller's or not — is "not found" here, as a missing one: the route names this job.
        if (await CopyOfAsync(replacementJobId, copyId, cancellationToken) is not { } copy)
            return ReplacementActionResult.NotFound;

        var replacement = job.Replacement;
        var stop = ReplacementJobProgress.StopOf(replacement.State, replacement.InspectionStartedUtc, replacement.QuarantineCheckedUtc);
        if (await DropRefusalAsync(replacement.State, stop, copy, cancellationToken) is { } refusal)
            return ReplacementActionResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(replacementJobId))
            return ReplacementActionResult.Refused(BeingWorkedOnError);
        try
        {
            // The claim first: it holds SQLite's write lock, so the copy, its names and the copies left are read again
            // below exactly as the drop finds them — two drops of a job's last two copies can't both pass "another copy is
            // left". The job's state and reason stay: Retry, the owner's own, sends it on once he dropped what he would.
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var now = time.GetUtcNow().UtcDateTime;
            if (await db.ReplacementJobs.ClaimForDropAsync(replacementJobId) == 0)
                return ReplacementActionResult.Refused(ChangedMeanwhileError);
            if (await CopyOfAsync(replacementJobId, copyId, CancellationToken.None) is not { } claimed)
                return ReplacementActionResult.Refused(ChangedMeanwhileError);
            if (await DropRefusalAsync(ReplacementJobState.NeedsReview, stop, claimed, CancellationToken.None) is { } changed)
                return ReplacementActionResult.Refused(changed);
            if (await db.ReplacementJobFiles.DropAsync(replacementJobId, copyId, now) == 0)
                return ReplacementActionResult.Refused(ChangedMeanwhileError);

            // The copy stays where it is: its names that never left their folders would read "waits to move".
            await db.ReplacementJobNames.ForgetNeverMovedNamesAsync(replacementJobId, copyId);
            auditTrail.Record(AuditAction.ReplacementCopyDropped, actor, AuditTarget.ReplacementJobFile(claimed));
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            logger.LogInformation(
                "Copy {ReplacementJobFileId} was dropped from replacement job {ReplacementJobId} by its owner; the job stays {State}.",
                copyId,
                replacementJobId,
                ReplacementJobState.NeedsReview);
        }
        finally
        {
            inHand.Release(replacementJobId);
        }

        return await AnswerAsync(job, callerUserId);
    }

    // ── refusals ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why a replacement in <paramref name="state"/> can't be cancelled with Keep both; <c>null</c> when it can
    /// (<see cref="ReplacementJobOffers.CanCancel"/>): it has ended; part of it left its folders —
    /// <see cref="ReplacementJobState.Quarantined"/>, or a name in <see cref="ReplacementNameStates.Taken"/>
    /// (<paramref name="holdsTakenNames"/>) —; or a write to the media is under way, or was cut and the mover's recovery
    /// settles it.
    /// </summary>
    private static Message? CancelRefusal(ReplacementJobState state, bool holdsTakenNames) =>
        ReplacementJobOffers.CanCancel(state, holdsTakenNames) ? null
        : state is ReplacementJobState.Failed or ReplacementJobState.Cancelled or ReplacementJobState.Completed or ReplacementJobState.Restored
            ? AlreadyEndedError
        : state == ReplacementJobState.Quarantined || holdsTakenNames ? LeftFoldersError
        : BeingWorkedOnError;

    /// <summary>Why the replacement <paramref name="replacementJobId"/>, which Keep both's write just missed, can't be
    /// cancelled as it is now; <c>null</c> when nothing explains it. Read inside the caller's transaction, which holds the
    /// write lock.</summary>
    private async Task<Message?> CancelRefusalNowAsync(Guid replacementJobId) =>
        CancelRefusal(await StateNowAsync(replacementJobId), await HoldsTakenNamesAsync(replacementJobId, null, CancellationToken.None));

    /// <summary>The state the replacement <paramref name="replacementJobId"/> is stored in now; read inside the caller's
    /// transaction, which holds the write lock, it is the state the caller's own write then finds.</summary>
    private Task<ReplacementJobState> StateNowAsync(Guid replacementJobId) =>
        db.ReplacementJobs
            .AsNoTracking()
            .Where(job => job.Id == replacementJobId)
            .Select(job => job.State)
            .SingleAsync(CancellationToken.None);

    /// <summary>
    /// Why <paramref name="copy"/> can't be dropped from its replacement, in <paramref name="state"/> and stopped at
    /// <paramref name="stop"/>; <c>null</c> when it can (<see cref="ReplacementJobOffers.CanDrop"/>). The first that applies
    /// of: not stopped for review; a download the app can't follow; dropped already; a name of it left its folder; the last
    /// copy the job would replace.
    /// </summary>
    private async Task<Message?> DropRefusalAsync(
        ReplacementJobState state, ReplacementStop? stop, ReplacementJobFileEntity copy, CancellationToken cancellationToken)
    {
        var dropped = copy.DroppedUtc is not null;
        var holdsTakenNames = await HoldsTakenNamesAsync(copy.ReplacementJobId, copy.Id, cancellationToken);
        var undroppedCopyCount = await db.ReplacementJobFiles.CountAsync(
            other => other.ReplacementJobId == copy.ReplacementJobId && other.DroppedUtc == null, cancellationToken);
        if (ReplacementJobOffers.CanDrop(state, stop, dropped, holdsTakenNames, undroppedCopyCount))
            return null;

        var name = MessageText.Clean(ReplacementText.FileNameOf(copy.AppPath));
        if (state != ReplacementJobState.NeedsReview)
            return NotStoppedForReviewDropError;
        if (stop is not (ReplacementStop.Check or ReplacementStop.Move))
            return DownloadNotFollowedDropError;
        if (dropped)
            return Message.Of("replacements.drop.already-dropped", ("file", name));
        if (holdsTakenNames)
            return Message.Of("replacements.drop.left-folder", ("file", name));
        return Message.Of("replacements.drop.last-copy", ("file", name));
    }

    // ── reads ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The ticked copy <paramref name="copyId"/> of the replacement <paramref name="replacementJobId"/> as stored
    /// now; <c>null</c> when that job has none such.</summary>
    private Task<ReplacementJobFileEntity?> CopyOfAsync(Guid replacementJobId, Guid copyId, CancellationToken cancellationToken) =>
        db.ReplacementJobFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(copy => copy.Id == copyId && copy.ReplacementJobId == replacementJobId, cancellationToken);

    /// <summary>Whether a name of the replacement <paramref name="replacementJobId"/> — of its copy
    /// <paramref name="copyId"/> only, when given — left its folder and wasn't given back
    /// (<see cref="ReplacementNameStates.Taken"/>).</summary>
    private Task<bool> HoldsTakenNamesAsync(Guid replacementJobId, Guid? copyId, CancellationToken cancellationToken) =>
        db.ReplacementJobNames.AnyAsync(
            name => name.ReplacementJobId == replacementJobId
                && (copyId == null || name.ReplacementJobFileId == copyId)
                && EF.Constant(ReplacementNameStates.Taken).Contains(name.State),
            cancellationToken);
}
