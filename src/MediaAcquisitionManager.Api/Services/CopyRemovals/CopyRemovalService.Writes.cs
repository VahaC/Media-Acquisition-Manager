using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — Restore and Delete for good of a removal run the quarantine's one write loop (<see cref="QuarantineWriteLoop{TName}"/>,
/// a replacement's own): the removal's claim, hold and end are its hooks here (<see cref="RemovalWriteLoop"/>).
/// </summary>
public sealed partial class CopyRemovalService
{
    /// <summary>
    /// Runs <paramref name="write"/> over <paramref name="names"/> of <paramref name="removal"/>, every check already passed
    /// and the removal taken by the caller: <c>null</c> when every name was written and the removal ended; otherwise the
    /// sentence the owner gets.
    /// </summary>
    private Task<Message?> RunAsync(CopyRemovalEntity removal, IReadOnlyList<CopyRemovalNameEntity> names, RemovalWrite write, AuditActor actor) =>
        new RemovalWriteLoop(db, gates, auditTrail, time, notice, logger, removal, write).RunAsync(names, write.Names, actor);

    /// <summary>
    /// One owner's write over a removal's names: the removal's states and audit rows around the loop, and what it does to
    /// each name (<see cref="QuarantineNameWrite"/>, the Strategy the loop runs).
    /// </summary>
    /// <param name="Running">The removal's state while it runs.</param>
    /// <param name="End">Its state once every name is written.</param>
    /// <param name="StartedAction">Its row at the claim.</param>
    /// <param name="EndedAction">Its row at the end.</param>
    /// <param name="Names">What the write does to each name.</param>
    private sealed record RemovalWrite(
        CopyRemovalState Running,
        CopyRemovalState End,
        AuditAction StartedAction,
        AuditAction EndedAction,
        QuarantineNameWrite Names);

    /// <summary>
    /// The loop's hooks for a removal (Template Method): claimed from the state the request read into the write's running
    /// state, held there before each name, and ended — its end state; or, when a name stopped it, <c>NeedsReview</c> with the
    /// stop's reason while a name is still in the quarantine, and its end state with the reason when none is, as nothing is
    /// left to review — with the removal's audit rows, the owner their actor.
    /// </summary>
    private sealed class RemovalWriteLoop(
        ApplicationDbContext db,
        IReplacementGates gates,
        IAuditTrail auditTrail,
        TimeProvider time,
        ICopyRemovalLibraryNotice notice,
        ILogger<CopyRemovalService> logger,
        CopyRemovalEntity removal,
        RemovalWrite write) : QuarantineWriteLoop<CopyRemovalNameEntity>(db, gates, auditTrail, time)
    {
        private CopyRemovalState _endedIn;

        protected override Message ChangedMeanwhileError => CopyRemovalService.ChangedMeanwhileError;

        protected override IQueryable<CopyRemovalNameEntity> Names => Db.CopyRemovalNames;

        protected override async Task<bool> StartAsync(AuditActor actor, DateTime now)
        {
            if (await Db.CopyRemovals.MoveAsync(removal.Id, removal.State, write.Running, null, now) == 0)
                return false;

            AuditTrail.Record(write.StartedAction, actor, AuditTarget.CopyRemoval(removal));
            return true;
        }

        protected override void Started() => LogMoved(removal.State, write.Running);

        protected override async Task<bool> HoldAsync() => await Db.CopyRemovals.HoldAsync(removal.Id, write.Running) == 1;

        protected override AuditTarget TargetOf(CopyRemovalNameEntity name) => AuditTarget.CopyRemovalName(name);

        protected override async Task<bool> FinishAsync(Message? stopped, bool noticeDue, AuditActor actor, DateTime now)
        {
            // Read inside the end's transaction: a removal with nothing left in the quarantine has nothing to review.
            _endedIn = stopped is not null
                && await Db.CopyRemovalNames.AnyAsync(
                    name => name.CopyRemovalId == removal.Id && name.State == ReplacementNameState.Quarantined, CancellationToken.None)
                    ? CopyRemovalState.NeedsReview
                    : write.End;
            if (await Db.CopyRemovals.MoveAsync(removal.Id, write.Running, _endedIn, stopped, now, noticeDue ? now : null) == 0)
                return false;

            AuditTrail.Record(_endedIn == CopyRemovalState.NeedsReview ? AuditAction.CopyRemovalNeedsReview : write.EndedAction, actor, AuditTarget.CopyRemoval(removal));
            return true;
        }

        protected override void Finished(Message? stopped) => LogMoved(write.Running, _endedIn);

        /// <summary>
        /// ADR 0030 — tells the library the names came back, after the commit and outside any transaction: best effort. A
        /// notice that couldn't go stays owed for the quarantine's next pass; a bug here costs the notice, never the owner's
        /// answer: Error, with the type and the stack, never the message.
        /// </summary>
        protected override async Task TellLibraryAsync()
        {
            try
            {
                await notice.SendDueAsync(removal.Id, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Telling the library about copy removal {CopyRemovalId} threw {ExceptionType} {StackTrace}",
                    removal.Id,
                    exception.GetType().FullName,
                    exception.StackTrace);
            }
        }

        private void LogMoved(CopyRemovalState from, CopyRemovalState to) =>
            logger.LogInformation("Copy removal {CopyRemovalId} went from {From} to {To}.", removal.Id, from, to);
    }
}
