using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26, MAM-40 — the one write loop of an owner's Restore or Delete for good over names in the quarantine (ADR 0030), a
/// replacement's or a removal's: the owner row claimed with its audit row, then name by name the gates read again, the
/// intent committed, the one call through <see cref="IFileQuarantine"/>, the outcome committed; then the owner row's end,
/// done or stopped. Each step its own transaction, none held across a write to the disk: a restart between two steps leaves
/// rows the quarantine's recovery reads.
/// </summary>
/// <remarks>
/// <para>
/// Template Method: the loop, its transactions and each name's intent and outcome are here, once, so a replacement's and a
/// removal's writes can't record a name two ways; the owner row's claim, its hold, its end and its log, the names' table and
/// audit target, and the library's notice are the subclass's. What the write does to each name — Restore's or Delete for
/// good's — is the Strategy it runs (<see cref="QuarantineNameWrite"/>).
/// </para>
/// <para>
/// Never cancelled once the claim is made: a write decided is carried through, whether or not the browser is still there.
/// </para>
/// </remarks>
internal abstract class QuarantineWriteLoop<TName>(ApplicationDbContext db, IReplacementGates gates, IAuditTrail auditTrail, TimeProvider time)
    where TName : QuarantinedNameEntity
{
    /// <summary>The context every step writes through, for the hooks' own writes in the same transactions.</summary>
    protected ApplicationDbContext Db => db;

    /// <summary>The audit trail the steps record to, for the hooks' own rows in the same saves.</summary>
    protected IAuditTrail AuditTrail => auditTrail;

    /// <summary>The sentence a write ends with when its owner row left the state the request claimed it in: never written
    /// over.</summary>
    protected abstract string ChangedMeanwhileError { get; }

    /// <summary>The table of the owner's names, tracked: each name's state is written through its row.</summary>
    protected abstract IQueryable<TName> Names { get; }

    /// <summary>
    /// Runs <paramref name="write"/> over <paramref name="names"/>, every check already passed and the owner row taken by
    /// the caller: <c>null</c> when every name was written and the owner row ended; otherwise the sentence the owner gets —
    /// the stop's reason, with the names already written kept so, or <see cref="ChangedMeanwhileError"/>.
    /// </summary>
    public async Task<string?> RunAsync(IReadOnlyList<TName> names, QuarantineNameWrite write, AuditActor actor)
    {
        if (!await ClaimAsync(actor))
            return ChangedMeanwhileError;

        // The checked names again, tracked, now the owner row is claimed: each name's state is written through them, in the
        // order the checks read them. Ordered here, not by SQL: the kind is stored by name, and "Companion" sorts before
        // "Video".
        List<Guid> ids = [.. names.Select(name => name.Id)];
        var rows = (await Names
                .Where(name => EF.Parameter(ids).Contains(name.Id) && name.State == ReplacementNameState.Quarantined)
                .ToListAsync(CancellationToken.None))
            .OrderBy(name => name.Kind)
            .ThenBy(name => name.Position)
            .ToList();

        var done = 0;
        string? stopped = null;
        foreach (var row in rows)
        {
            // ADR 0033 — the gates again right before every write: switched off, the name's folder unticked, or its
            // quarantine out of the Media folders since the checks, and the rest stays where it is.
            var gate = await gates.ReadAsync(CancellationToken.None);
            if (Closed(row, gate, write, out var roots) is { } closed)
            {
                stopped = write.StopReason(row, closed, done, rows.Count);
                break;
            }

            if (!await IntentAsync(row, write, actor))
                return ChangedMeanwhileError;

            var result = await write.WriteAsync(row, roots);
            if (await OutcomeAsync(row, result, write, actor) is { } problem)
            {
                stopped = write.StopReason(row, problem, done, rows.Count);
                break;
            }

            done++;
        }

        if (!await EndAsync(stopped, write.TellsLibrary && done > 0, actor))
            return ChangedMeanwhileError;
        if (write.TellsLibrary && done > 0)
            await TellLibraryAsync();
        return stopped;
    }

    // ── the hooks ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Claims the owner row for the write — its running state, read as the request read it — and records the
    /// start's audit row, inside the claim's transaction; whether it did: <c>false</c> when it moved on meanwhile.</summary>
    protected abstract Task<bool> StartAsync(AuditActor actor, DateTime now);

    /// <summary>After the claim's commit: its log line.</summary>
    protected abstract void Started();

    /// <summary>Holds the owner row in its running state, inside a name's intent's transaction — a claim of its state to
    /// itself, which holds SQLite's write lock until the commit; whether it still has it.</summary>
    protected abstract Task<bool> HoldAsync();

    /// <summary>A name's audit target.</summary>
    protected abstract AuditTarget TargetOf(TName name);

    /// <summary>Ends the owner row, inside the end's transaction: its end state when nothing <paramref name="stopped"/> it,
    /// otherwise as the subclass's rules say, with the end's audit row; the library owed a notice from
    /// <paramref name="now"/> when <paramref name="noticeDue"/>. Whether it was written.</summary>
    protected abstract Task<bool> FinishAsync(string? stopped, bool noticeDue, AuditActor actor, DateTime now);

    /// <summary>After the end's commit: its log line.</summary>
    protected abstract void Finished(string? stopped);

    /// <summary>Tells the library the names came back, after the commit and outside any transaction: best effort.</summary>
    protected abstract Task TellLibraryAsync();

    // ── the steps ───────────────────────────────────────────────────────────────────────────────

    private async Task<bool> ClaimAsync(AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (!await StartAsync(actor, time.GetUtcNow().UtcDateTime))
            return false;

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        Started();
        return true;
    }

    /// <summary>
    /// The intent of one name's write, committed before the call (ADR 0030): the name in its running state with its row,
    /// while the owner row is still claimed; whether it was written.
    /// </summary>
    private async Task<bool> IntentAsync(TName row, QuarantineNameWrite write, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (!await HoldAsync())
            return false;

        row.State = write.NameRunning;
        auditTrail.Record(write.NameIntentAction, actor, TargetOf(row));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// The outcome of one name's write, committed after the call whatever became of the owner row meanwhile — the disk did
    /// what it did: written, with its time; back in the quarantine, untouched, with the file system's sentence; or, when the
    /// rename moved another file put at the name after the last look (<see cref="FileQuarantineStatus.Swapped"/>), lost,
    /// with why. The name's problem, <c>null</c> when it was written.
    /// </summary>
    private async Task<string?> OutcomeAsync(TName row, FileQuarantineResult<FileFacts> result, QuarantineNameWrite write, AuditActor actor)
    {
        string? problem = null;
        if (result.Status == FileQuarantineStatus.Done)
        {
            row.State = write.NameEnd;
            row.Problem = null;
            write.Stamp(row, time.GetUtcNow().UtcDateTime);
            auditTrail.Record(write.NameDoneAction, actor, TargetOf(row));
        }
        else
        {
            // A swap left another file where this one was going and this one nowhere the app knows: never Quarantined,
            // which Restore and Delete for good would act on.
            var swapped = result.Status == FileQuarantineStatus.Swapped;
            problem = swapped ? write.SwappedProblem(row) : result.Error ?? QuarantineNameChecks.NoReasonError;
            row.State = swapped ? ReplacementNameState.Lost : ReplacementNameState.Quarantined;
            row.Problem = FileInspectionRunUpdates.Cut(problem, QuarantinedNameEntity.ProblemMaximumLength);
            auditTrail.Record(write.NameNotDoneAction, actor, TargetOf(row));
        }

        // One save, so the name and its row commit together.
        await db.SaveChangesAsync(CancellationToken.None);
        return problem;
    }

    private async Task<bool> EndAsync(string? stopped, bool noticeDue, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (!await FinishAsync(stopped, noticeDue, actor, time.GetUtcNow().UtcDateTime))
            return false;

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        Finished(stopped);
        return true;
    }

    /// <summary>
    /// Why the next write of <paramref name="row"/> can't go now — switched off, its quarantine out of the Media folders, or
    /// the write's own gate (<see cref="QuarantineNameWrite.Closed"/>) — as the {error} of a stop; <c>null</c> when it can,
    /// with the <paramref name="roots"/> to write inside.
    /// </summary>
    private static string? Closed(TName row, ReplacementGateState gate, QuarantineNameWrite write, out IReadOnlyList<string> roots)
    {
        roots = [];
        if (!gate.IsEnabled)
            return write.SwitchedOffError;
        if (QuarantineNameChecks.RootsOf(row, gate) is not { } reachable)
            return $"{ReplacementText.Quote(QuarantineNameChecks.QuarantineOf(row))} is no longer inside the Media folders.";

        roots = reachable;
        return write.Closed(row, gate);
    }
}
