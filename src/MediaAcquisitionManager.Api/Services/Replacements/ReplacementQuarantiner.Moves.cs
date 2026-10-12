using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The move of a verified job: the checks' stop, or the claim with the whole plan written as name rows; then each name's
// intent, rename and outcome, the gates read again before every one; then the job's end and the library's notice.
public sealed partial class ReplacementQuarantiner
{
    /// <summary>
    /// Takes the verified <paramref name="job"/> one step on: left as it is while replacement is switched off; otherwise
    /// checked, then stopped for review or moved name by name into the quarantine and settled, and the library told — the
    /// job added to <paramref name="told"/> once it was.
    /// </summary>
    private async Task MoveAsync(QuarantineJob job, HashSet<Guid> told, CancellationToken cancellationToken)
    {
        // Defence in depth, as the verifier's: only a done download the strict check let through gets this far. Anything
        // else is a hand-edited row. Warning, so it ships off the box.
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

        // ADR 0033 — while the switch is off a job moves nothing and writes nothing: it stays Verified, and the Jobs line
        // already says nothing will move while it is off. Its checks don't run either: check 6 alone would ask every
        // download client.
        var gate = await gates.ReadAsync(cancellationToken);
        if (!gate.IsEnabled)
        {
            // Debug, not Information: it is said on every pass while the switch is off.
            logger.LogDebug("Replacement is switched off; replacement job {ReplacementJobId} stays Verified.", job.Replacement.Id);
            return;
        }

        var check = await CheckAsync(job, gate, cancellationToken);
        if (check.Reason is { } reason)
        {
            await StopAsync(job, reason, check, time.GetUtcNow().UtcDateTime);
            return;
        }

        if (await ClaimAsync(job, check, time.GetUtcNow().UtcDateTime) is not { } names)
            return;

        if (await MoveNamesAsync(job, names, cancellationToken))
        {
            // Outside any transaction: the library hears the names that left it, in its own terms, never a quarantine path.
            told.Add(job.Replacement.Id);
            await notice.SendDueAsync(job.Replacement.Id, cancellationToken);
        }
    }

    // ── the stop before the claim ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops the Verified <paramref name="job"/> for review with <paramref name="reason"/> before anything of it moved, in
    /// one transaction: the state, <see cref="ReplacementJobEntity.QuarantineCheckedUtc"/>, each copy's own problem (none
    /// for the copies the stop isn't about), and a <c>ReplacementNeedsReview</c> row of the app's own. Nothing renamed, no
    /// name row written; nothing at all when another writer moved the job first.
    /// </summary>
    private async Task StopAsync(QuarantineJob job, string reason, MoveCheck check, DateTime now)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.StopBeforeQuarantineAsync(job.Replacement.Id, reason, now) == 0)
        {
            db.ChangeTracker.Clear();
            return;
        }

        await SetProblemsAsync([.. check.Considered.Select(copy => copy.Id)], check.Problems);
        auditTrail.Record(AuditAction.ReplacementNeedsReview, null, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(job.Replacement.Id, ReplacementJobState.Verified, ReplacementJobState.NeedsReview);
    }

    // ── the claim ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Claims the Verified <paramref name="job"/> for its move and writes its whole plan, in one transaction:
    /// <c>Quarantining</c>, an earlier run's names of the checked copies that never moved replaced, the companions an
    /// earlier run left waiting kept waiting or given the state their check found, every name of the checked copies — each
    /// video, then its companions — as a row of its own after the job's others, the copies' problems of an earlier stop
    /// cleared, and a <c>ReplacementQuarantineStarted</c> row of the app's own. The names to rename, in their order — the
    /// companions left waiting first —; <c>null</c> when another writer moved the job first.
    /// </summary>
    private async Task<IReadOnlyList<ReplacementJobNameEntity>?> ClaimAsync(QuarantineJob job, MoveCheck check, DateTime now)
    {
        var replacementJobId = job.Replacement.Id;
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.StartQuarantineAsync(replacementJobId, now) == 0)
        {
            db.ChangeTracker.Clear();
            return null;
        }

        // A Retry's run plans these copies afresh from what the disk says now: an earlier run's names of them that never
        // moved — still waiting, or left where they were — make way. Names that moved stay, and so do the names of copies
        // that moved before (a companion a torrent held, say): the job keeps saying where each is.
        List<Guid> copyIds = [.. check.Copies.Select(copy => copy.Copy.Id)];
        await db.ReplacementJobNames
            .Where(name => name.ReplacementJobId == replacementJobId
                && copyIds.Contains(name.ReplacementJobFileId)
                && (name.State == ReplacementNameState.Planned || name.State == ReplacementNameState.Stayed))
            .ExecuteDeleteAsync(CancellationToken.None);

        // A companion left waiting keeps its row and its place, waiting still, or staying or lost as its check found.
        var waiting = new List<ReplacementJobNameEntity>();
        foreach (var leftover in check.Waiting)
        {
            if (leftover.State == ReplacementNameState.Planned)
                waiting.Add(leftover.Name);
            else
                await db.ReplacementJobNames.RecordNameLeftWaitingAsync(leftover.Name.Id, leftover.State, leftover.Problem!);
        }

        var position = (await db.ReplacementJobNames
            .Where(name => name.ReplacementJobId == replacementJobId)
            .MaxAsync(name => (int?)name.Position, CancellationToken.None) ?? -1) + 1;

        var names = new List<ReplacementJobNameEntity>();
        foreach (var copy in check.Copies)
        {
            names.Add(NameRow(replacementJobId, copy, copy.AppPath, copy.Fresh, ReplacementNameKind.Video, null, position++));
            foreach (var companion in copy.Companions)
                names.Add(NameRow(replacementJobId, copy, companion.AppPath, companion.Facts, ReplacementNameKind.Companion, companion.HeldBy, position++));
        }

        db.ReplacementJobNames.AddRange(names);
        await SetProblemsAsync(copyIds, new Dictionary<Guid, string>());
        auditTrail.Record(AuditAction.ReplacementQuarantineStarted, null, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        // The rows go on as the plan's snapshot; their states are written by conditional updates from here on.
        db.ChangeTracker.Clear();
        LogMove(replacementJobId, ReplacementJobState.Verified, ReplacementJobState.Quarantining);
        return [.. waiting, .. names];
    }

    /// <summary>
    /// A name of the plan: where it is, where it goes in the quarantine of the allowed root holding the copy
    /// (<see cref="QuarantineFolder.PathOf"/>), and the identity it was read with, which the rename expects; waiting, or
    /// staying where it is when <paramref name="heldBy"/> says a torrent holds it.
    /// </summary>
    private static ReplacementJobNameEntity NameRow(
        Guid replacementJobId, CheckedCopy copy, NormalizedPath appPath, FileFacts facts, ReplacementNameKind kind, string? heldBy, int position) =>
        new()
        {
            ReplacementJobId = replacementJobId,
            Position = position,
            ReplacementJobFileId = copy.Copy.Id,
            Kind = kind,
            AppPath = appPath.ToString(),
            QuarantinePath = QuarantineFolder.PathOf(copy.Root, replacementJobId, appPath).ToString(),
            AllowedRoot = copy.RootText,
            DeviceId = facts.DeviceId!.Value,
            Inode = facts.Inode!.Value,
            SizeBytes = facts.SizeBytes,
            ModifiedUtc = facts.ModifiedUtc,
            LinkCount = facts.LinkCount,
            State = heldBy is null ? ReplacementNameState.Planned : ReplacementNameState.Stayed,
            Problem = heldBy is null ? null : FileInspectionRunUpdates.Cut(heldBy, ReplacementJobNameEntity.ProblemMaximumLength),
        };

    // ── the names ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the claimed <paramref name="job"/>'s planned <paramref name="names"/> into the quarantine, one at a time in
    /// their order, then settles the job: once every video is there, <c>Quarantined</c> — or <c>Completed</c> when nothing
    /// of it is left in the quarantine, every video deleted from it for good —; <c>NeedsReview</c> once a video couldn't
    /// move, any name's rename moved another file, or the gates closed on the way. Whether it settled the job —
    /// <c>false</c> when another writer moved it first, which leaves the rest to it.
    /// </summary>
    private async Task<bool> MoveNamesAsync(QuarantineJob job, IReadOnlyList<ReplacementJobNameEntity> names, CancellationToken cancellationToken)
    {
        var replacementJobId = job.Replacement.Id;
        var moved = 0;
        string? reason = null;
        Guid? stoppedCopyId = null;
        foreach (var name in names.Where(name => name.State == ReplacementNameState.Planned))
        {
            // A shutdown stops between two names, never inside one: the job stays Quarantining, and the next start finds
            // it cut and stops it for review with the names already moved.
            cancellationToken.ThrowIfCancellationRequested();

            var (intent, gate) = await IntendAsync(job, name);
            if (intent == NameIntent.Taken)
                return false;
            if (intent == NameIntent.SwitchedOff)
            {
                reason = SwitchedOffWhileMovingReason(await CountMovedAsync(replacementJobId, cancellationToken));
                break;
            }

            if (intent == NameIntent.LeftTicked)
            {
                reason = LeftTickedWhileMovingReason(name.AppPath, await CountMovedAsync(replacementJobId, cancellationToken));
                break;
            }

            // Once its intent is committed the rename is decided: never cancelled. Inside the roots read under the intent's
            // lock, so a Save of the Media folders can't have taken this one's away in between.
            var result = await quarantine.MoveIntoQuarantineAsync(
                name.AppPath, QuarantineNameChecks.FactsOf(name), name.QuarantinePath, gate!.AllowedRoots, CancellationToken.None);
            if (result is { Status: FileQuarantineStatus.Done, Value: { } facts })
            {
                await RecordMovedAsync(name, facts, time.GetUtcNow().UtcDateTime);
                moved++;
                continue;
            }

            // A rename that moved another file — one put at the name after the last look — leaves that file in the
            // quarantine and the old copy's own file nowhere the app knows: the name is lost, and the owner must look,
            // whichever name it was.
            if (result.Status == FileQuarantineStatus.Swapped)
            {
                var problem = SwappedWhileMovingProblem(name.AppPath);
                await RecordNotMovedAsync(name, ReplacementNameState.Lost, problem);
                reason = NameNotMovedReason(name.AppPath, problem, await CountMovedAsync(replacementJobId, cancellationToken));
                stoppedCopyId = name.ReplacementJobFileId;
                break;
            }

            var error = result.Error ?? NoSentenceError(result.Status);
            await RecordNotMovedAsync(name, ReplacementNameState.Stayed, error);
            // A companion that can't move stays, named on the job, and its video stays moved; a video that can't move stops
            // the job, its companions and every later name left waiting where they are.
            if (name.Kind == ReplacementNameKind.Video)
            {
                reason = NameNotMovedReason(name.AppPath, error, await CountMovedAsync(replacementJobId, cancellationToken));
                stoppedCopyId = name.ReplacementJobFileId;
                break;
            }
        }

        // Owed only when a name left the library now: a Retry's run that found everything moved already told it nothing new.
        return await SettleAsync(job, reason, stoppedCopyId, noticeDue: moved > 0, time.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// The intent of <paramref name="name"/>'s rename, committed before the call while <paramref name="job"/> is still
    /// <c>Quarantining</c>: the name <c>Moving</c>, with a <c>ReplacementNameMoving</c> row of the app's own — once the
    /// gates, read under the transaction's write lock, still let it move. What became of it, with the gates it read.
    /// </summary>
    /// <remarks>
    /// ADR 0033 — the switch and the ticks are read again right before every rename, and here inside the intent's own
    /// transaction (BEGIN IMMEDIATE): a Save of the Media folders committed before it is seen, and one that comes after
    /// finds the name <c>Moving</c> and is refused (<see cref="AllowedRootsService"/>), so a root can't leave the list
    /// between the read and the rename. A closed gate rolls the intent back: nothing is written for the name.
    /// </remarks>
    private async Task<(NameIntent Intent, ReplacementGateState? Gate)> IntendAsync(QuarantineJob job, ReplacementJobNameEntity name)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.ClaimAsync(job.Replacement.Id, ReplacementJobState.Quarantining) == 0)
        {
            db.ChangeTracker.Clear();
            return (NameIntent.Taken, null);
        }

        var gate = await gates.ReadAsync(CancellationToken.None);
        if (!gate.IsEnabled || !QuarantineNameChecks.StillTicked(name, gate))
        {
            db.ChangeTracker.Clear();
            return (gate.IsEnabled ? NameIntent.LeftTicked : NameIntent.SwitchedOff, gate);
        }

        if (await db.ReplacementJobNames.StartNameMoveAsync(name.Id) == 0)
        {
            db.ChangeTracker.Clear();
            return (NameIntent.Taken, null);
        }

        auditTrail.Record(AuditAction.ReplacementNameMoving, null, AuditTarget.ReplacementJobName(name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return (NameIntent.Intended, gate);
    }

    /// <summary>The outcome of <paramref name="name"/>'s rename that was done, committed after it: <c>Quarantined</c> with
    /// what the file system said at its new name, and a <c>ReplacementNameMoved</c> row of the app's own.</summary>
    private async Task RecordMovedAsync(ReplacementJobNameEntity name, FileFacts facts, DateTime now)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobNames.RecordNameMovedAsync(name.Id, facts, now) == 0)
        {
            db.ChangeTracker.Clear();
            return;
        }

        auditTrail.Record(AuditAction.ReplacementNameMoved, null, AuditTarget.ReplacementJobName(name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    /// <summary>The outcome of <paramref name="name"/>'s rename that wasn't done — <c>Stayed</c> where it was — or that
    /// moved another file — <c>Lost</c> —, committed after it with <paramref name="problem"/>, and a
    /// <c>ReplacementNameNotMoved</c> row of the app's own.</summary>
    private async Task RecordNotMovedAsync(ReplacementJobNameEntity name, ReplacementNameState state, string problem)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobNames.RecordNameNotMovedAsync(name.Id, state, problem) == 0)
        {
            db.ChangeTracker.Clear();
            return;
        }

        auditTrail.Record(AuditAction.ReplacementNameNotMoved, null, AuditTarget.ReplacementJobName(name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    /// <summary>
    /// Ends the move of <paramref name="job"/>, in one transaction while it is still <c>Quarantining</c>: when no
    /// <paramref name="reason"/> stopped it, <c>Quarantined</c> while any of its names is in the quarantine, or
    /// <c>Completed</c> when none is and its videos were deleted from it for good — a Retry of a deletion that stopped
    /// part-way, with nothing left to move —; <c>NeedsReview</c> with the reason otherwise, the copy whose name stopped it
    /// (<paramref name="stoppedCopyId"/>) given it as its problem. A library notice owed from then when
    /// <paramref name="noticeDue"/>, and the audit row of the app's own. Whether it was written.
    /// </summary>
    private async Task<bool> SettleAsync(QuarantineJob job, string? reason, Guid? stoppedCopyId, bool noticeDue, DateTime now)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var state = reason is not null ? ReplacementJobState.NeedsReview : await RestingStateAsync(job.Replacement.Id);
        if (await db.ReplacementJobs.SettleQuarantineAsync(job.Replacement.Id, state, reason, now, noticeDue) == 0)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        if (stoppedCopyId is { } copyId)
            await SetProblemsAsync([copyId], new Dictionary<Guid, string> { [copyId] = reason! });
        auditTrail.Record(
            state switch
            {
                ReplacementJobState.Quarantined => AuditAction.ReplacementQuarantined,
                ReplacementJobState.Completed => AuditAction.ReplacementCompleted,
                _ => AuditAction.ReplacementNeedsReview,
            },
            null,
            TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(job.Replacement.Id, ReplacementJobState.Quarantining, state);
        return true;
    }

    /// <summary>
    /// Where a move nothing stopped leaves <paramref name="replacementJobId"/>: <c>Quarantined</c> while a name of it is in
    /// the quarantine — what Restore and Delete for good act on —, <c>Completed</c> when none is and a video of it was
    /// deleted from the quarantine for good, which only an owner's Delete for good does. Read inside the settle's
    /// transaction.
    /// </summary>
    private async Task<ReplacementJobState> RestingStateAsync(Guid replacementJobId)
    {
        var states = await db.ReplacementJobNames
            .Where(name => name.ReplacementJobId == replacementJobId)
            .Select(name => new { name.Kind, name.State })
            .ToListAsync(CancellationToken.None);
        return states.Any(name => name.State == ReplacementNameState.Quarantined)
            || !states.Any(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Deleted })
                ? ReplacementJobState.Quarantined
                : ReplacementJobState.Completed;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes each of <paramref name="copyIds"/>' problem: its sentence in <paramref name="problems"/>, cut to the column,
    /// or none — a problem of an earlier stop no longer holds. Inside the caller's transaction.
    /// </summary>
    private async Task SetProblemsAsync(IReadOnlyList<Guid> copyIds, IReadOnlyDictionary<Guid, string> problems)
    {
        List<Guid> cleared = [.. copyIds.Where(copyId => !problems.ContainsKey(copyId))];
        await db.ReplacementJobFiles
            .Where(copy => cleared.Contains(copy.Id) && copy.Problem != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.Problem, (string?)null), CancellationToken.None);
        foreach (var (copyId, problem) in problems)
        {
            var stored = FileInspectionRunUpdates.Cut(problem, ReplacementJobFileEntity.ProblemMaximumLength);
            await db.ReplacementJobFiles
                .Where(copy => copy.Id == copyId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(copy => copy.Problem, stored), CancellationToken.None);
        }
    }

    /// <summary>
    /// How many of the job's names are in the quarantine now, of every name its moves planned — waiting, moving, moved,
    /// staying or lost; a name put back or deleted for good is no longer part of a move.
    /// </summary>
    private async Task<(int Count, int Total)> CountMovedAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        var states = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .Select(name => name.State)
            .ToListAsync(cancellationToken);
        return QuarantineNameCounts.Moved(states);
    }

    /// <summary>What a name's intent came to: written, or not — the job moved on, or the gates read under its lock closed
    /// on it.</summary>
    private enum NameIntent
    {
        /// <summary>Written: the name is <c>Moving</c>, and its rename is decided.</summary>
        Intended = 1,

        /// <summary>Another writer moved the job, or the name, first.</summary>
        Taken = 2,

        /// <summary>Replacement was switched off.</summary>
        SwitchedOff = 3,

        /// <summary>The name's folder is no longer ticked, or its root no longer allowed.</summary>
        LeftTicked = 4,
    }
}
