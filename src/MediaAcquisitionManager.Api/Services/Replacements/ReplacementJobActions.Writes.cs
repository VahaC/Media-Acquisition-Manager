using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the one write loop of Restore and Delete for good (ADR 0030): the job claimed with its row, then name by name
/// the gates read again, the intent committed, the one call through <see cref="IFileQuarantine"/>, the outcome committed;
/// then the job's end, done or stopped for review. Each step its own transaction, none held across a write to the disk:
/// a restart between two steps leaves rows the mover's recovery reads (<c>ReplacementJobStates.Unsettled</c>). MAM-40 —
/// the loop itself is <see cref="QuarantineWriteLoop{TName}"/>'s, which a removal's Restore and Delete for good run too;
/// the job's claim, hold and end are its hooks here (<see cref="JobWriteLoop"/>).
/// </summary>
public sealed partial class ReplacementJobActions
{
    // MAM-26, ADR 0033 — the {error} of a stop when replacement was switched off between two names.
    private const string SwitchedOffMidwayError = "Replacement was switched off on the Media folders page.";

    /// <summary>
    /// Runs <paramref name="write"/> over <paramref name="names"/> of <paramref name="job"/>, read as
    /// <paramref name="observedState"/>, every check already passed and the job taken by the caller: <c>null</c> when every
    /// name was written and the job ended; otherwise the sentence the owner gets — the stop's reason, the job waiting for
    /// review with the names already written kept so, or why it couldn't go on.
    /// </summary>
    /// <remarks>Never cancelled once the claim is made: a write decided is carried through, whether or not the browser is
    /// still there.</remarks>
    private Task<string?> RunAsync(
        OwnedJob job, ReplacementJobState observedState, IReadOnlyList<ReplacementJobNameEntity> names, OwnerWrite write, AuditActor actor) =>
        new JobWriteLoop(db, gates, auditTrail, time, notice, logger, job, observedState, write).RunAsync(names, write.Names, actor);

    /// <summary>
    /// One owner's write over a job's names: the job's states and audit rows around the loop, and what it does to each name
    /// (<see cref="QuarantineNameWrite"/>, the Strategy the loop runs).
    /// </summary>
    /// <param name="Running">The job's state while it runs.</param>
    /// <param name="End">The job's state once every name is written.</param>
    /// <param name="StartedAction">The job's row at the claim.</param>
    /// <param name="EndedAction">The job's row at the end.</param>
    /// <param name="Names">What the write does to each name.</param>
    private sealed record OwnerWrite(
        ReplacementJobState Running,
        ReplacementJobState End,
        AuditAction StartedAction,
        AuditAction EndedAction,
        QuarantineNameWrite Names);

    /// <summary>
    /// The loop's hooks for a replacement (Template Method): the job claimed from <c>observedState</c> into the write's
    /// running state, held there before each name, and ended — its end state, or <c>NeedsReview</c> with the stop's reason,
    /// marked as the mover's stage so Retry is offered — with the job's audit rows, the owner their actor.
    /// </summary>
    private sealed class JobWriteLoop(
        ApplicationDbContext db,
        IReplacementGates gates,
        IAuditTrail auditTrail,
        TimeProvider time,
        IReplacementLibraryNotice notice,
        ILogger<ReplacementJobActions> logger,
        OwnedJob job,
        ReplacementJobState observedState,
        OwnerWrite write) : QuarantineWriteLoop<ReplacementJobNameEntity>(db, gates, auditTrail, time)
    {
        private ReplacementJobState _endedIn;

        protected override string ChangedMeanwhileError => ReplacementJobActions.ChangedMeanwhileError;

        protected override IQueryable<ReplacementJobNameEntity> Names => Db.ReplacementJobNames;

        protected override async Task<bool> StartAsync(AuditActor actor, DateTime now)
        {
            if (await Db.ReplacementJobs.MoveAsync(job.Replacement.Id, observedState, write.Running, null, now) == 0)
                return false;

            AuditTrail.Record(write.StartedAction, actor, ReplacementJobActions.TargetOf(job));
            return true;
        }

        protected override void Started() => LogMoved(observedState, write.Running);

        protected override async Task<bool> HoldAsync() => await Db.ReplacementJobs.ClaimAsync(job.Replacement.Id, write.Running) == 1;

        protected override AuditTarget TargetOf(ReplacementJobNameEntity name) => AuditTarget.ReplacementJobName(name);

        protected override async Task<bool> FinishAsync(string? stopped, bool noticeDue, AuditActor actor, DateTime now)
        {
            _endedIn = stopped is null ? write.End : ReplacementJobState.NeedsReview;
            if (await Db.ReplacementJobs.EndOwnerWriteAsync(job.Replacement.Id, write.Running, _endedIn, stopped, now, noticeDue ? now : null) == 0)
                return false;

            AuditTrail.Record(stopped is null ? write.EndedAction : AuditAction.ReplacementNeedsReview, actor, ReplacementJobActions.TargetOf(job));
            return true;
        }

        protected override void Finished(string? stopped) => LogMoved(write.Running, _endedIn);

        /// <summary>
        /// ADR 0030 — tells the library the names came back, after the commit and outside any transaction: best effort. A
        /// notice that couldn't go stays owed (<c>LibraryNoticeDueUtc</c>) for the mover's next pass, and a bug here costs the
        /// notice, never the owner's answer: Error, with the type and the stack, never the message.
        /// </summary>
        protected override async Task TellLibraryAsync()
        {
            try
            {
                await notice.SendDueAsync(job.Replacement.Id, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Telling the library about replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                    job.Replacement.Id,
                    exception.GetType().FullName,
                    exception.StackTrace);
            }
        }

        private void LogMoved(ReplacementJobState from, ReplacementJobState to) =>
            logger.LogInformation("Replacement job {ReplacementJobId} went from {From} to {To}.", job.Replacement.Id, from, to);
    }
}
