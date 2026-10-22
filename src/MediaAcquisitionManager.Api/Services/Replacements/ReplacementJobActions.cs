using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementJobActions"/>
/// <remarks>
/// <para>
/// Split by action: Retry here; the details (<c>.Details.cs</c>); Restore (<c>.Restore.cs</c>) and Delete for good
/// (<c>.Delete.cs</c>), each with the checks it makes before anything is written; and the one write loop both run name by
/// name (<c>.Writes.cs</c>) — the claim, each name's intent and outcome, the end —, so the two can't record a write two
/// ways. MAM-27 — Keep both and Drop (<c>.Review.cs</c>).
/// </para>
/// <para>
/// The answer to a Retry, a Restore, a Delete for good — MAM-27 — a Keep both or a Drop is the download job with its
/// replacement line, read the way the Jobs page reads it (<see cref="IDownloadService.GetAsync"/>): the page shows what it
/// would show after a reload.
/// </para>
/// <para>
/// Information: a replacement's id, its states and counts, never a path, a title or a sentence.
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — every refusal is a message the controller renders in the request's language, and the job's page
/// (<c>.Details.cs</c>) renders the stored reasons and problems with its <c>texts</c>.
/// </para>
/// </remarks>
public sealed partial class ReplacementJobActions(
    ApplicationDbContext db,
    IReplacementGates gates,
    IFileInventory files,
    IFileQuarantine quarantine,
    ReplacementJobsInHand inHand,
    IReplacementLibraryNotice notice,
    IDownloadService downloads,
    IAuditTrail auditTrail,
    IRequestTexts texts,
    TimeProvider time,
    ILogger<ReplacementJobActions> logger) : IReplacementJobActions
{
    /// <summary>What a Delete for good body MVC can't read gets (<c>UnreadableBodyErrorAttribute</c>): what it must
    /// be.</summary>
    public const string UnreadableBodyError = "replacements.delete-for-good.unreadable-body";

    // MAM-26 — a Retry of a job that isn't waiting for review. MAM-27 — no longer "after a move": a stop of the check of
    // the new files is retried too.
    private static readonly Message NothingToRetryError = Message.Of("replacements.retry.not-waiting");

    // MAM-27 — a review the download's following stopped before any check started (qBittorrent's downloads couldn't be
    // told apart, say), or one whose download is no longer done and checked: there is no new file the app could check
    // again. Keep both, or Cancel job, ends it.
    private static readonly Message DownloadNotFollowedRetryError = Message.Of("replacements.retry.download-not-followed");

    // MAM-26 — someone in this process — the mover, or another click of the owner's — is writing for the job right now.
    private static readonly Message BeingWorkedOnError = Message.Of("replacements.job.busy");

    // MAM-26 — the job left the state the request claimed it in, between the claim and a later write: never written over.
    private static readonly Message ChangedMeanwhileError = Message.Of("replacements.job.changed-meanwhile");

    public async Task<ReplacementActionResult> RetryAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return ReplacementActionResult.NotFound;

        // MAM-27 — the stage that stopped it decides where it goes back: the mover's queue, or the check of the new files.
        var replacement = job.Replacement;
        var stop = ReplacementJobProgress.StopOf(replacement.State, replacement.InspectionStartedUtc, replacement.QuarantineCheckedUtc);
        if (await RetryRefusalAsync(replacement, stop, cancellationToken) is { } refusal)
            return ReplacementActionResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(replacementJobId))
            return ReplacementActionResult.Refused(BeingWorkedOnError);
        try
        {
            // Nothing is written to the media or a client: the job goes back to a queue whose next pass checks again — the
            // mover's, which checks its copies (the switch, the ticks, the torrents) before anything moves; or the
            // verifier's, which checks the new files from the start and asks the library to look again, so the library's
            // ids decide once more. Conditional, and the audit row in the same transaction: a job that moved on meanwhile
            // is left as it is, with nothing recorded.
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var now = time.GetUtcNow().UtcDateTime;
            ReplacementJobState retriedTo;
            if (stop == ReplacementStop.Move)
            {
                if (await db.ReplacementJobs.RetryAsync(replacementJobId, now) == 0)
                    return ReplacementActionResult.Refused(ChangedMeanwhileError);
                auditTrail.Record(AuditAction.ReplacementRetried, actor, TargetOf(job));
                retriedTo = ReplacementJobState.Verified;
            }
            else
            {
                if (await db.ReplacementJobs.RetryCheckAsync(db.DownloadJobs, db.ReplacementJobFiles, replacementJobId, now) == 0)
                    return ReplacementActionResult.Refused(ChangedMeanwhileError);
                // The last run's evidence goes now, not at the next pass's files stage — minutes later for a pack —, so the
                // page never shows "identified as Aliens" from a check that no longer stands.
                await db.ForgetCheckAsync(replacementJobId);
                auditTrail.Record(AuditAction.ReplacementCheckRetried, actor, TargetOf(job));
                retriedTo = ReplacementJobState.Downloaded;
            }

            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            LogMove(replacementJobId, ReplacementJobState.NeedsReview, retriedTo);
        }
        finally
        {
            inHand.Release(replacementJobId);
        }

        return await AnswerAsync(job, callerUserId);
    }

    /// <summary>
    /// MAM-27 — why <paramref name="replacement"/>, stopped at <paramref name="stop"/>, can't be retried; <c>null</c> when it
    /// can (<see cref="ReplacementJobOffers.CanRetry(ReplacementStop?, int)"/>): it isn't waiting for review; its download
    /// can't be followed — or, for a stop of the check, is no longer done and checked, which the verifier never reads —;
    /// or an undropped copy has a problem only a drop settles, the first by its place named.
    /// </summary>
    private async Task<Message?> RetryRefusalAsync(ReplacementJobEntity replacement, ReplacementStop? stop, CancellationToken cancellationToken)
    {
        if (stop is null)
            return NothingToRetryError;
        if (stop == ReplacementStop.Download)
            return DownloadNotFollowedRetryError;
        if (stop == ReplacementStop.Move)
            return null;

        if (!await db.DownloadJobs.AnyAsync(
                download => download.Id == replacement.DownloadJobId
                    && download.Status == DownloadJobStatus.Downloaded
                    && download.OverwriteCheck == DownloadOverwriteCheck.Checked,
                cancellationToken))
            return DownloadNotFollowedRetryError;

        var dropOnly = await db.ReplacementJobFiles
            .AsNoTracking()
            .Where(copy => copy.ReplacementJobId == replacement.Id && copy.DroppedUtc == null && copy.OnlyDropSettles)
            .OrderBy(copy => copy.Position)
            .Select(copy => copy.AppPath)
            .ToListAsync(cancellationToken);
        return ReplacementJobOffers.CanRetry(stop, dropOnly.Count) ? null : DropOnlyError(dropOnly[0]);
    }

    // MAM-27 — an undropped copy whose problem no fix in the library or the client can change: a new check would only stop
    // on it again, so dropping it, or keeping both, is the way on.
    private static Message DropOnlyError(string appPath) =>
        Message.Of("replacements.retry.drop-only", ("file", MessageText.Clean(ReplacementText.FileNameOf(appPath))));

    // ── reads ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The caller's replacement <paramref name="replacementJobId"/> with its release's name (the audit rows' name,
    /// ReleaseEntity.Description's), or <c>null</c>: another user's answers as none, as their download jobs do (ADR 0014).
    /// The download and the release can't be gone while the job points at them (Restrict), so the joins lose no job.
    /// </summary>
    private Task<OwnedJob?> FindOwnedAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken) =>
        (from replacement in db.ReplacementJobs.AsNoTracking().Where(job => job.Id == replacementJobId && job.RequestedByUserId == callerUserId)
         join download in db.DownloadJobs.AsNoTracking() on replacement.DownloadJobId equals download.Id
         join release in db.Releases.AsNoTracking() on download.ReleaseId equals release.Id
         select new OwnedJob(replacement, release.Description ?? release.Title))
        .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The names of <paramref name="replacementJobId"/> in the quarantine now, videos first, then companions,
    /// each in its plan's order: the order Restore and Delete for good check and write them in.</summary>
    private async Task<List<ReplacementJobNameEntity>> QuarantinedNamesAsync(Guid replacementJobId, CancellationToken cancellationToken) =>
        // Ordered here, not by SQL: the kind is stored by name, and "Companion" sorts before "Video".
        [
            .. (await db.ReplacementJobNames
                    .AsNoTracking()
                    .Where(name => name.ReplacementJobId == replacementJobId && name.State == ReplacementNameState.Quarantined)
                    .ToListAsync(cancellationToken))
                .OrderBy(name => name.Kind)
                .ThenBy(name => name.Position),
        ];

    // Read from the database rather than the token: the audit rows carry the name the account has.
    private Task<AuditActor> ActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    /// <summary>
    /// The download job of <paramref name="job"/> as the Jobs page reads it now, for the caller. Read after every write
    /// is committed and whatever this context tracked is let go; never cancelled, as the writes weren't.
    /// </summary>
    private async Task<ReplacementActionResult> AnswerAsync(OwnedJob job, Guid callerUserId)
    {
        db.ChangeTracker.Clear();
        return await downloads.GetAsync(job.Replacement.DownloadJobId, callerUserId, CancellationToken.None) is { } answer
            ? ReplacementActionResult.Done(answer)
            : ReplacementActionResult.NotFound;
    }

    private static AuditTarget TargetOf(OwnedJob job) => AuditTarget.ReplacementJob(job.Replacement, job.ReleaseTitle);

    private void LogMove(Guid replacementJobId, ReplacementJobState from, ReplacementJobState to) =>
        logger.LogInformation("Replacement job {ReplacementJobId} went from {From} to {To}.", replacementJobId, from, to);

    /// <summary>One of the caller's replacements, as the request read it, with its release's name.</summary>
    private sealed record OwnedJob(ReplacementJobEntity Replacement, string ReleaseTitle);
}
