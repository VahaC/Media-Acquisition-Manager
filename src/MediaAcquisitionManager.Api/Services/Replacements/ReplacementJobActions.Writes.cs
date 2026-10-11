using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the one write loop of Restore and Delete for good (ADR 0030): the job claimed with its row, then name by name
/// the gates read again, the intent committed, the one call through <see cref="IFileQuarantine"/>, the outcome committed;
/// then the job's end, done or stopped for review. Each step its own transaction, none held across a write to the disk:
/// a restart between two steps leaves rows the mover's recovery reads (<c>ReplacementJobStates.Unsettled</c>).
/// </summary>
public sealed partial class ReplacementJobActions
{
    // MAM-26, ADR 0033 — the {error} of a stop when replacement was switched off between two names.
    private const string SwitchedOffMidwayError = "Replacement was switched off on the Media folders page.";

    // MAM-26 — the {error} of a write the file system refused without saying why; the contract always says, but a name's
    // problem is never left blank.
    private const string NoReasonError = "The file system gave no reason.";

    /// <summary>
    /// Runs <paramref name="write"/> over <paramref name="names"/> of <paramref name="job"/>, read as
    /// <paramref name="observedState"/>, every check already passed and the job taken by the caller: <c>null</c> when every
    /// name was written and the job ended; otherwise the sentence the owner gets — the stop's reason, the job waiting for
    /// review with the names already written kept so, or why it couldn't go on.
    /// </summary>
    /// <remarks>Never cancelled once the claim is made: a write decided is carried through, whether or not the browser is
    /// still there.</remarks>
    private async Task<string?> RunAsync(
        OwnedJob job, ReplacementJobState observedState, IReadOnlyList<ReplacementJobNameEntity> names, OwnerWrite write, AuditActor actor)
    {
        if (!await ClaimAsync(job, observedState, write, actor))
            return ChangedMeanwhileError;

        // The checked names again, tracked, now the job is claimed: each name's state is written through them, in the
        // order the checks read them.
        List<Guid> ids = [.. names.Select(name => name.Id)];
        var rows = (await db.ReplacementJobNames
                .Where(name => EF.Parameter(ids).Contains(name.Id) && name.State == ReplacementNameState.Quarantined)
                .ToListAsync(CancellationToken.None))
            .OrderBy(name => name.Kind)
            .ThenBy(name => name.Position)
            .ToList();

        var done = 0;
        string? stopped = null;
        foreach (var row in rows)
        {
            // ADR 0033 — the gates again right before every write: replacement switched off, the name's folder unticked,
            // or its quarantine out of the Media folders since the checks, and the rest stays where it is.
            var gate = await gates.ReadAsync(CancellationToken.None);
            if (Closed(row, gate, write, out var roots) is { } closed)
            {
                stopped = write.StopReason(row, closed, done, rows.Count);
                break;
            }

            if (!await IntentAsync(job, row, write, actor))
                return ChangedMeanwhileError;

            var result = await write.WriteAsync(row, roots);
            if (await OutcomeAsync(row, result, write, actor) is { } problem)
            {
                stopped = write.StopReason(row, problem, done, rows.Count);
                break;
            }

            done++;
        }

        if (!await EndAsync(job, write, stopped, done, actor))
            return ChangedMeanwhileError;
        if (write.TellsLibrary && done > 0)
            await TellLibraryAsync(job.Replacement.Id);
        return stopped;
    }

    /// <summary>Claims <paramref name="job"/>, read as <paramref name="observedState"/>, for the write — its running state,
    /// with the start's audit row in the same transaction; whether it did: 0 rows means the job moved on meanwhile.</summary>
    private async Task<bool> ClaimAsync(OwnedJob job, ReplacementJobState observedState, OwnerWrite write, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var now = time.GetUtcNow().UtcDateTime;
        if (await db.ReplacementJobs.MoveAsync(job.Replacement.Id, observedState, write.Running, null, now) == 0)
            return false;

        auditTrail.Record(write.StartedAction, actor, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(job.Replacement.Id, observedState, write.Running);
        return true;
    }

    /// <summary>
    /// The intent of one name's write, committed before the call (ADR 0030): the name in its running state with its row,
    /// while the job is still claimed — the claim of its state to itself holds SQLite's write lock until the commit;
    /// whether it was written.
    /// </summary>
    private async Task<bool> IntentAsync(OwnedJob job, ReplacementJobNameEntity row, OwnerWrite write, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.ClaimAsync(job.Replacement.Id, write.Running) == 0)
            return false;

        row.State = write.NameRunning;
        auditTrail.Record(write.NameIntentAction, actor, AuditTarget.ReplacementJobName(row));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// The outcome of one name's write, committed after the call whatever became of the job meanwhile — the disk did what
    /// it did: written, with its time; back in the quarantine, untouched, with the file system's sentence; or, when the
    /// rename moved another file put at the name after the last look (<see cref="FileQuarantineStatus.Swapped"/>), lost,
    /// with why. The name's problem, <c>null</c> when it was written.
    /// </summary>
    private async Task<string?> OutcomeAsync(ReplacementJobNameEntity row, FileQuarantineResult<FileFacts> result, OwnerWrite write, AuditActor actor)
    {
        string? problem = null;
        if (result.Status == FileQuarantineStatus.Done)
        {
            row.State = write.NameEnd;
            row.Problem = null;
            write.Stamp(row, time.GetUtcNow().UtcDateTime);
            auditTrail.Record(write.NameDoneAction, actor, AuditTarget.ReplacementJobName(row));
        }
        else
        {
            // A swap left another file where this one was going and this one nowhere the app knows: never Quarantined,
            // which Restore and Delete for good would act on.
            var swapped = result.Status == FileQuarantineStatus.Swapped;
            problem = swapped ? write.SwappedProblem(row) : result.Error ?? NoReasonError;
            row.State = swapped ? ReplacementNameState.Lost : ReplacementNameState.Quarantined;
            row.Problem = FileInspectionRunUpdates.Cut(problem, ReplacementJobNameEntity.ProblemMaximumLength);
            auditTrail.Record(write.NameNotDoneAction, actor, AuditTarget.ReplacementJobName(row));
        }

        // One save, so the name and its row commit together.
        await db.SaveChangesAsync(CancellationToken.None);
        return problem;
    }

    /// <summary>
    /// Ends the write on <paramref name="job"/>: its end state when nothing <paramref name="stopped"/> it, otherwise
    /// <c>NeedsReview</c> with the stop's reason, marked as the mover's stage so Retry is offered; the library owed a
    /// notice when the write tells it and a name came back; the row of the end, the owner its actor. Whether it was
    /// written: 0 rows means the job left its running state meanwhile.
    /// </summary>
    private async Task<bool> EndAsync(OwnedJob job, OwnerWrite write, string? stopped, int done, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var now = time.GetUtcNow().UtcDateTime;
        var state = stopped is null ? write.End : ReplacementJobState.NeedsReview;
        DateTime? noticeDueUtc = write.TellsLibrary && done > 0 ? now : null;
        if (await db.ReplacementJobs.EndOwnerWriteAsync(job.Replacement.Id, write.Running, state, stopped, now, noticeDueUtc) == 0)
            return false;

        auditTrail.Record(stopped is null ? write.EndedAction : AuditAction.ReplacementNeedsReview, actor, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(job.Replacement.Id, write.Running, state);
        return true;
    }

    /// <summary>
    /// ADR 0030 — tells the library the names came back, after the commit and outside any transaction: best effort. A
    /// notice that couldn't go stays owed (<c>LibraryNoticeDueUtc</c>) for the mover's next pass, and a bug here costs the
    /// notice, never the owner's answer: Error, with the type and the stack, never the message.
    /// </summary>
    private async Task TellLibraryAsync(Guid replacementJobId)
    {
        try
        {
            await notice.SendDueAsync(replacementJobId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Telling the library about replacement job {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                replacementJobId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
    }

    // ── the checks both writes share ────────────────────────────────────────────────────────────

    /// <summary>
    /// Why the next write of <paramref name="row"/> can't go now — replacement switched off, its quarantine out of the
    /// Media folders, or the write's own gate (<see cref="OwnerWrite.Closed"/>) — as the {error} of a stop; <c>null</c>
    /// when it can, with the <paramref name="roots"/> to write inside.
    /// </summary>
    private static string? Closed(ReplacementJobNameEntity row, ReplacementGateState gate, OwnerWrite write, out IReadOnlyList<string> roots)
    {
        roots = [];
        if (!gate.IsEnabled)
            return SwitchedOffMidwayError;
        if (RootsOf(row, gate) is not { } reachable)
            return $"{ReplacementText.Quote(QuarantineOf(row))} is no longer inside the Media folders.";

        roots = reachable;
        return write.Closed(row, gate);
    }

    /// <summary>
    /// The roots a write of <paramref name="row"/> may act inside: its own allowed root alone, the one whose quarantine
    /// holds it, while an allowed root of now holds that root — itself, or a parent the owner put in its place (ADR 0030:
    /// a Save that keeps the quarantine within reach) —; <c>null</c> when none does. Its own root rather than today's: the
    /// write contract finds a quarantine only at the top of a root it is given, and a root inside an allowed one widens
    /// nothing.
    /// </summary>
    private static IReadOnlyList<string>? RootsOf(ReplacementJobNameEntity row, ReplacementGateState gate) =>
        NormalizedPath.TryParse(row.AllowedRoot, out var root, out _)
        && gate.AllowedRoots.Any(allowed => NormalizedPath.TryParse(allowed, out var parsed, out _) && root.IsUnder(parsed))
            ? [row.AllowedRoot]
            : null;

    /// <summary>The quarantine holding <paramref name="row"/>: <c>&lt;its allowed root&gt;/.mam-quarantine</c>.</summary>
    private static string QuarantineOf(ReplacementJobNameEntity row) =>
        NormalizedPath.TryParse(row.AllowedRoot, out var root, out _) ? QuarantineFolder.Of(root).ToString() : row.AllowedRoot;

    /// <summary>Whether the file at <paramref name="row"/>'s quarantine path is no longer the one the move put there: gone,
    /// or another device, inode, size or modification time (ADR 0030).</summary>
    private async Task<bool> ChangedInQuarantineAsync(ReplacementJobNameEntity row, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var described = await files.DescribeAsync(row.QuarantinePath, roots, cancellationToken);
        return described is not { Status: FileInventoryStatus.Found, Value: { } facts }
            || facts.DeviceId != row.DeviceId
            || facts.Inode != row.Inode
            || facts.SizeBytes != row.SizeBytes
            || facts.ModifiedUtc != row.ModifiedUtc;
    }

    /// <summary>What the move recorded of <paramref name="row"/>: what the write contract must find before it writes.</summary>
    private static FileFacts FactsOf(ReplacementJobNameEntity row) => new(row.SizeBytes, row.ModifiedUtc, row.DeviceId, row.Inode, row.LinkCount);

    /// <summary>The file's own name, quoted: what a stop's sentence names.</summary>
    private static string NameOf(ReplacementJobNameEntity row) => ReplacementText.Quote(ReplacementText.FileNameOf(row.AppPath));

    /// <summary>
    /// One owner's write over a job's names (the Strategy the loop runs): Restore's or Delete for good's states, audit
    /// actions, gate, call, time stamp and stop sentence.
    /// </summary>
    /// <param name="Running">The job's state while it runs.</param>
    /// <param name="End">The job's state once every name is written.</param>
    /// <param name="NameRunning">A name's state between its intent and its outcome.</param>
    /// <param name="NameEnd">A name's state once written.</param>
    /// <param name="StartedAction">The job's row at the claim.</param>
    /// <param name="NameIntentAction">A name's row before the call.</param>
    /// <param name="NameDoneAction">A name's row once written.</param>
    /// <param name="NameNotDoneAction">A name's row when the call refused.</param>
    /// <param name="EndedAction">The job's row at the end.</param>
    /// <param name="TellsLibrary">Whether the library is told of the names written (a restore; a deletion was told at the
    /// move).</param>
    /// <param name="Closed">The write's own gate on a name, read again before each: the {error} of a stop, or <c>null</c>.</param>
    /// <param name="WriteAsync">The one call through <see cref="IFileQuarantine"/>, within the roots given.</param>
    /// <param name="Stamp">Records when a name was written.</param>
    /// <param name="SwappedProblem">A name's problem when its write moved another file put at it after the last look.</param>
    /// <param name="StopReason">The job's reason when a name stopped it: the name, the {error}, how many of how many were
    /// written (<see cref="ReplacementNameTally"/>).</param>
    private sealed record OwnerWrite(
        ReplacementJobState Running,
        ReplacementJobState End,
        ReplacementNameState NameRunning,
        ReplacementNameState NameEnd,
        AuditAction StartedAction,
        AuditAction NameIntentAction,
        AuditAction NameDoneAction,
        AuditAction NameNotDoneAction,
        AuditAction EndedAction,
        bool TellsLibrary,
        Func<ReplacementJobNameEntity, ReplacementGateState, string?> Closed,
        Func<ReplacementJobNameEntity, IReadOnlyList<string>, Task<FileQuarantineResult<FileFacts>>> WriteAsync,
        Action<ReplacementJobNameEntity, DateTime> Stamp,
        Func<ReplacementJobNameEntity, string> SwappedProblem,
        Func<ReplacementJobNameEntity, string, int, int, string> StopReason);
}
