using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementVerifier"/>
/// <remarks>
/// <para>
/// Three steps, each one pass's work for a job and each reading its rows afresh: the claim of a <c>Downloaded</c> job;
/// the files' checks (<see cref="IReplacementNewFileReader"/>) and, once they pass, the one request to look
/// (<see cref="IReplacementLibraryReader"/>), while the library wasn't asked yet; and the library's read with the pairing
/// (<see cref="ReplacementPairing"/>) on every pass after. The pure rules are Core's; this class reads, writes and moves.
/// </para>
/// <para>
/// Every write is in a transaction that first holds the job in the state the pass read — a conditional move for a
/// decision, a claim of the state to itself for evidence alone —, so the owner's Keep both that got there first (MAM-27)
/// is never written over, and a decision and its evidence and audit row commit together or not at all. Once decided, a
/// write is never cancelled (<see cref="ReplacementJobUpdates"/>' rule). The owner's Retry and Drop meet no pass: they
/// apply only to a job stopped for review, which the verifier never reads; a Retry sends the job back to Downloaded, and
/// the next pass checks it again from its files.
/// </para>
/// <para>
/// Information: a replacement's id, its states and counts, never a path, a title or a sentence.
/// </para>
/// </remarks>
public sealed partial class ReplacementVerifier(
    ApplicationDbContext db,
    IReplacementNewFileReader newFiles,
    IReplacementLibraryReader library,
    IFileInventory files,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<ReplacementVerifier> logger) : IReplacementVerifier
{
    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await VerifyingAsync(cancellationToken);
        foreach (var job in jobs)
        {
            // One each: a bug on one job costs that job this pass, never the others' (the follower's rule).
            try
            {
                await CheckAsync(job, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                logger.LogError(
                    "Checking replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                    job.Replacement.Id,
                    exception.GetType().FullName,
                    exception.StackTrace);
            }
            finally
            {
                // Whatever a job read or staged goes with it — a broken one's unsaved evidence above all —, so the next
                // job's save can't carry it.
                db.ChangeTracker.Clear();
            }
        }

        // Debug, not Information: a pass runs every 15 s while anything is being checked.
        if (jobs.Count > 0)
            logger.LogDebug("Checked {Count} replacement jobs.", jobs.Count);
    }

    /// <summary>
    /// The replacements being checked, each with its download and its release's title (the audit rows' name); oldest
    /// first, so a pass reads the same way every time. Filtered by the states inlined as constants: they are then
    /// IX_ReplacementJobs_Verifying's own filter, which SQLite uses only for a query naming them. The release can't be
    /// gone while a download points at it (Restrict), so the join loses no job.
    /// </summary>
    private async Task<List<VerifyingJob>> VerifyingAsync(CancellationToken cancellationToken) =>
        (await db.ReplacementJobs
            .AsNoTracking()
            .Where(job => EF.Constant(ReplacementJobStates.Verifying).Contains(job.State))
            .Join(
                db.DownloadJobs.AsNoTracking(),
                replacement => replacement.DownloadJobId,
                download => download.Id,
                (replacement, download) => new { Replacement = replacement, Download = download })
            .Join(
                db.Releases.AsNoTracking(),
                row => row.Download.ReleaseId,
                release => release.Id,
                (row, release) => new { row.Replacement, row.Download, ReleaseTitle = release.Title })
            .OrderBy(row => row.Replacement.CreatedUtc)
            .ThenBy(row => row.Replacement.Id)
            .ToListAsync(cancellationToken))
        .Select(row => new VerifyingJob(row.Replacement, row.Download, row.ReleaseTitle))
        .ToList();

    /// <summary>Takes <paramref name="job"/> one step on: claimed when it is still Downloaded, then its stage.</summary>
    private async Task CheckAsync(VerifyingJob job, CancellationToken cancellationToken)
    {
        // Defence in depth: the follower makes a replacement Downloaded only with a done download the strict check let
        // through, and nothing moves such a download on. Anything else is a hand-edited row; its new files are no one's
        // to read. Warning, so it ships off the box.
        if (job.Download is not { Status: DownloadJobStatus.Downloaded, OverwriteCheck: DownloadOverwriteCheck.Checked })
        {
            logger.LogWarning(
                "Replacement job {ReplacementJobId} is {State} while its download is {Status} ({OverwriteCheck}); it is left as it is.",
                job.Replacement.Id,
                job.Replacement.State,
                job.Download.Status,
                job.Download.OverwriteCheck);
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (job.Replacement.State == ReplacementJobState.Downloaded)
        {
            if (!await StartAsync(job, now))
                return;

            // As the claim wrote it: the files' checks start in this same pass.
            job.Replacement.State = ReplacementJobState.Inspecting;
            job.Replacement.StateChangedUtc = now;
            job.Replacement.InspectionStartedUtc = now;
            job.Replacement.LibraryAskedUtc = null;
        }

        if (job.Replacement.LibraryAskedUtc is null)
            await CheckFilesAsync(job, now, cancellationToken);
        else
            await CheckLibraryAsync(job, now, cancellationToken);
    }

    // ── writes ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Claims a Downloaded <paramref name="job"/>: Inspecting from <paramref name="now"/>, with its
    /// <c>ReplacementCheckStarted</c> row in the same transaction; whether it did — 0 rows means another writer moved it
    /// first.
    /// </summary>
    private async Task<bool> StartAsync(VerifyingJob job, DateTime now)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.StartCheckAsync(job.Replacement.Id, now) == 0)
            return false;

        auditTrail.Record(AuditAction.ReplacementCheckStarted, null, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(job.Replacement.Id, ReplacementJobState.Downloaded, ReplacementJobState.Inspecting);
        return true;
    }

    /// <summary>
    /// Ends the check of <paramref name="job"/> as <paramref name="state"/> — Verified, NeedsReview or Failed — with
    /// <paramref name="reason"/>, its evidence (what the context tracks, and what <paramref name="writeEvidence"/> adds)
    /// and its audit row, all in one transaction; nothing at all when another writer moved the job first.
    /// </summary>
    private async Task FinishAsync(VerifyingJob job, ReplacementJobState state, string? reason, DateTime now, Func<Task> writeEvidence)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.MoveAsync(job.Replacement.Id, ReplacementJobState.Inspecting, state, reason, now) == 0)
        {
            // A cancel or a retry won: the evidence staged for this decision goes too.
            db.ChangeTracker.Clear();
            return;
        }

        await writeEvidence();
        auditTrail.Record(ActionOf(state), null, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(job.Replacement.Id, ReplacementJobState.Inspecting, state);
    }

    /// <summary>
    /// Keeps the evidence of a pass that decided nothing yet — what the context tracks, and what
    /// <paramref name="writeEvidence"/> adds — while the job is still Inspecting; whether it did.
    /// </summary>
    private async Task<bool> KeepAsync(Guid replacementJobId, Func<Task> writeEvidence)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.ClaimAsync(replacementJobId, ReplacementJobState.Inspecting) == 0)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        await writeEvidence();
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }

    private static AuditTarget TargetOf(VerifyingJob job) => AuditTarget.ReplacementJob(job.Replacement, job.ReleaseTitle);

    private static AuditAction ActionOf(ReplacementJobState state) => state switch
    {
        ReplacementJobState.Verified => AuditAction.ReplacementVerified,
        ReplacementJobState.Failed => AuditAction.ReplacementFailed,
        ReplacementJobState.NeedsReview => AuditAction.ReplacementNeedsReview,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A check ends Verified, NeedsReview or Failed."),
    };

    private void LogMove(Guid replacementJobId, ReplacementJobState from, ReplacementJobState to) =>
        logger.LogInformation("Replacement job {ReplacementJobId} went from {From} to {To}.", replacementJobId, from, to);

    /// <summary>One replacement being checked, as the pass read it, with its download and its release's title.</summary>
    private sealed record VerifyingJob(ReplacementJobEntity Replacement, DownloadJobEntity Download, string ReleaseTitle);
}
