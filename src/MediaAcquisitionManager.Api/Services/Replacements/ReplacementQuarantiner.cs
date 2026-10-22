using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementQuarantiner"/>
/// <remarks>
/// <para>
/// Three steps a pass, each job on its own and each reading its rows afresh: the recovery of the jobs a restart or a crash
/// cut (<c>ReplacementQuarantiner.Recovery.cs</c>); the checks right before a verified job's move — check 5, the
/// companions and check 6 (<c>ReplacementQuarantiner.Checks.cs</c>) —, then its claim, its renames and its end
/// (<c>ReplacementQuarantiner.Moves.cs</c>); and the library notices still owed. The sentences are
/// <c>ReplacementQuarantiner.Reasons.cs</c>'. The pure rules — which names move with a video, where they go, what a disc
/// clip is — are Core's; this class reads, writes and renames.
/// </para>
/// <para>
/// Every job the mover works on is held in <see cref="ReplacementJobsInHand"/> while it does, so an owner's Restore,
/// Delete for good or Retry waits for it, and its own recovery never takes a job someone in this process is writing for.
/// Every write is in a transaction that first holds the job — or, for a rename's outcome, the name — in the state the
/// mover read, and none is held across a disk or network call: a name's intent is committed before its rename and its
/// outcome after, so a restart between the two finds the name in the middle. Once decided, a write is never cancelled
/// (<see cref="ReplacementJobUpdates"/>' rule), the rename included.
/// </para>
/// <para>
/// Information: a replacement's id, its states and counts, never a path, a title or a sentence.
/// </para>
/// </remarks>
public sealed partial class ReplacementQuarantiner(
    ApplicationDbContext db,
    IReplacementGates gates,
    IReplacementHoldingReader holdingReader,
    IReplacementLibraryNotice notice,
    IFileInventory files,
    IFileQuarantine quarantine,
    ReplacementJobsInHand inHand,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<ReplacementQuarantiner> logger) : IReplacementQuarantiner
{
    public async Task QuarantineAsync(CancellationToken cancellationToken = default)
    {
        // ── 1. the jobs a restart or a crash cut ──
        var cut = await WithTitlesAsync(
            db.ReplacementJobs.AsNoTracking().Where(job => EF.Constant(ReplacementJobStates.Unsettled).Contains(job.State)),
            cancellationToken);
        foreach (var job in cut)
            await TakeAsync(job, recovering: true, RecoverAsync, cancellationToken);

        // ── 2. the verified jobs ──
        // The mover's queue less the jobs it is moving, which only a cut leaves behind and step 1 settled. Filtered by the
        // states inlined as constants too: they are IX_ReplacementJobs_Quarantine's own filter, which SQLite uses only for a
        // query naming them.
        var verified = await WithTitlesAsync(
            db.ReplacementJobs.AsNoTracking()
                .Where(job => EF.Constant(ReplacementJobStates.Quarantine).Contains(job.State) && job.State == ReplacementJobState.Verified),
            cancellationToken);
        var told = new HashSet<Guid>();
        foreach (var job in verified)
            await TakeAsync(job, recovering: false, (taken, token) => MoveAsync(taken, told, token), cancellationToken);

        // ── 3. the library notices still owed ──
        // A job told in step 2 waits for the next pass: asking the library twice in one pass tells it nothing new.
        var due = await db.ReplacementJobs
            .AsNoTracking()
            .Where(job => job.LibraryNoticeDueUtc != null)
            .OrderBy(job => job.CreatedUtc)
            .ThenBy(job => job.Id)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
        foreach (var replacementJobId in due.Where(replacementJobId => !told.Contains(replacementJobId)))
            await TellAsync(replacementJobId, cancellationToken);

        // Debug, not Information: a pass runs every 15 s.
        if (cut.Count + verified.Count + due.Count > 0)
            logger.LogDebug(
                "Settled {CutCount} cut replacement jobs, took up {VerifiedCount} verified ones and owed {NoticeCount} library notices.",
                cut.Count,
                verified.Count,
                due.Count);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on <paramref name="job"/> while holding it in <see cref="ReplacementJobsInHand"/> — or
    /// not at all, when someone in this process already holds it: an owner's Restore, Delete for good or Retry is writing
    /// for it, and the next pass takes it up. One each: a bug on one job costs that job this pass, never the others'.
    /// </summary>
    private async Task TakeAsync(
        QuarantineJob job, bool recovering, Func<QuarantineJob, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        if (!inHand.TryTake(job.Replacement.Id))
            return;

        try
        {
            await work(job, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // A job left Quarantining by this is found cut on the next pass and stops for review there, its names looked up
            // where they were and where they were going. Error, with the type and the stack, never the message.
            if (recovering)
                logger.LogError(
                    "Settling the cut replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                    job.Replacement.Id,
                    exception.GetType().FullName,
                    exception.StackTrace);
            else
                logger.LogError(
                    "Moving the old copies of replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                    job.Replacement.Id,
                    exception.GetType().FullName,
                    exception.StackTrace);
        }
        finally
        {
            // Released whatever happened, a shutdown included: the registry tells a write under way from one cut, and this
            // one is over.
            inHand.Release(job.Replacement.Id);
            // Whatever a job read or staged goes with it, so the next job's save can't carry it.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Sends the library notice <paramref name="replacementJobId"/> owes, on its own: the notice never throws but for the
    /// caller's cancellation, and a bug in it costs that notice this pass, never the others'.
    /// </summary>
    private async Task TellAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        try
        {
            await notice.SendDueAsync(replacementJobId, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogError(
                "Telling the library about replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                replacementJobId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// <paramref name="jobs"/>, each with its download and its release's name (the audit rows' name,
    /// ReleaseEntity.Description's); oldest first, so a pass reads the same way every time. The release can't be gone while
    /// a download points at it (Restrict), so the join loses no job.
    /// </summary>
    private async Task<List<QuarantineJob>> WithTitlesAsync(IQueryable<ReplacementJobEntity> jobs, CancellationToken cancellationToken) =>
        (await jobs
            .Join(
                db.DownloadJobs.AsNoTracking(),
                replacement => replacement.DownloadJobId,
                download => download.Id,
                (replacement, download) => new { Replacement = replacement, Download = download })
            .Join(
                db.Releases.AsNoTracking(),
                row => row.Download.ReleaseId,
                release => release.Id,
                (row, release) => new { row.Replacement, row.Download, ReleaseTitle = release.Description ?? release.Title })
            .OrderBy(row => row.Replacement.CreatedUtc)
            .ThenBy(row => row.Replacement.Id)
            .ToListAsync(cancellationToken))
        .Select(row => new QuarantineJob(row.Replacement, row.Download, row.ReleaseTitle))
        .ToList();

    private static AuditTarget TargetOf(QuarantineJob job) => AuditTarget.ReplacementJob(job.Replacement, job.ReleaseTitle);

    private void LogMove(Guid replacementJobId, ReplacementJobState from, ReplacementJobState to) =>
        logger.LogInformation("Replacement job {ReplacementJobId} went from {From} to {To}.", replacementJobId, from, to);

    /// <summary>One replacement the mover takes up, as the pass read it, with its download and its release's title.</summary>
    private sealed record QuarantineJob(ReplacementJobEntity Replacement, DownloadJobEntity Download, string ReleaseTitle);
}
