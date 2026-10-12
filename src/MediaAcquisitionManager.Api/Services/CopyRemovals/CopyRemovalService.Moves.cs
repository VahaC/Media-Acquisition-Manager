using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

// MAM-40 — the move of a removal every check let through (ADR 0030): the claim with the whole plan written as name rows;
// then each name's intent, rename and outcome, the gates read again before every one — the video first, so a video that
// can't move leaves nothing in the quarantine —; then the removal's end. As a replacement's mover moves its names
// (ReplacementQuarantiner.Moves.cs), through the same conditional writes of the names.
public sealed partial class CopyRemovalService
{
    /// <summary>
    /// Claims <paramref name="planned"/> and moves its names, held in this process for as long, so the quarantine's pass
    /// never mistakes it for one a restart cut: how it ended. Never cancelled: everything was decided by the checks.
    /// </summary>
    private async Task<RemovalEnd> MoveAsync(PlannedRemoval planned, AuditActor actor)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var removal = new CopyRemovalEntity
        {
            RequestedByUserId = actor.UserId,
            RequestedByUserName = actor.UserName,
            State = CopyRemovalState.Moving,
            StateChangedUtc = now,
            CreatedUtc = now,
            Title = FileInspectionRunUpdates.Cut(planned.Group.Heading, CopyRemovalEntity.TitleMaximumLength),
            GroupClass = planned.Group.Group.Class,
            GroupKey = planned.Group.Group.Key,
            PhysicalFileId = planned.File.FileId,
            AppPath = planned.AppPath.ToString(),
            ServicePath = planned.File.ServicePath,
        };
        List<CopyRemovalNameEntity> names =
        [
            NameRow(removal.Id, planned, planned.AppPath, planned.Fresh, ReplacementNameKind.Video, null, 0),
            .. planned.Companions.Select((companion, index) =>
                NameRow(removal.Id, planned, companion.AppPath, companion.Facts, ReplacementNameKind.Companion, companion.HeldBy, index + 1)),
        ];

        // Taken before the claim commits: a pass between the commit and a later take would find it Moving with no one
        // holding it, and settle it as cut. A new id, so it is always free.
        inHand.TryTake(removal.Id);
        try
        {
            await ClaimAsync(removal, names, actor);
            return await MoveNamesAsync(removal, names, actor);
        }
        finally
        {
            inHand.Release(removal.Id);
        }
    }

    /// <summary>
    /// Writes the claim in one transaction: the removal <c>Moving</c>, every name of its plan — the video, then its
    /// companions, a held one staying where it is —, and a <c>CopyRemovalStarted</c> row, the owner its actor.
    /// </summary>
    private async Task ClaimAsync(CopyRemovalEntity removal, IReadOnlyList<CopyRemovalNameEntity> names, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        db.CopyRemovals.Add(removal);
        db.CopyRemovalNames.AddRange(names);
        auditTrail.Record(AuditAction.CopyRemovalStarted, actor, AuditTarget.CopyRemoval(removal));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        // The rows go on as the plan's snapshot; their states are written by conditional updates from here on.
        db.ChangeTracker.Clear();
        LogMove(removal.Id, null, CopyRemovalState.Moving);
    }

    /// <summary>
    /// A name of the plan: where it is, where it goes in the quarantine of the allowed root holding the copy
    /// (<see cref="QuarantineFolder.PathOf"/>, under the removal's own id), and the identity it was read with, which the
    /// rename expects; waiting, or staying where it is when <paramref name="heldBy"/> says a torrent holds it.
    /// </summary>
    private static CopyRemovalNameEntity NameRow(
        Guid copyRemovalId, PlannedRemoval planned, NormalizedPath appPath, FileFacts facts, ReplacementNameKind kind, string? heldBy, int position) =>
        new()
        {
            CopyRemovalId = copyRemovalId,
            Position = position,
            Kind = kind,
            AppPath = appPath.ToString(),
            QuarantinePath = QuarantineFolder.PathOf(planned.Root, copyRemovalId, appPath).ToString(),
            AllowedRoot = planned.RootText,
            DeviceId = facts.DeviceId!.Value,
            Inode = facts.Inode!.Value,
            SizeBytes = facts.SizeBytes,
            ModifiedUtc = facts.ModifiedUtc,
            LinkCount = facts.LinkCount,
            State = heldBy is null ? ReplacementNameState.Planned : ReplacementNameState.Stayed,
            Problem = heldBy is null ? null : FileInspectionRunUpdates.Cut(heldBy, QuarantinedNameEntity.ProblemMaximumLength),
        };

    /// <summary>
    /// Moves the claimed <paramref name="removal"/>'s planned <paramref name="names"/> into the quarantine, one at a time in
    /// their order, then settles it: <c>Quarantined</c> once the video is there; when something stopped it, <c>NeedsReview</c>
    /// while a name is in the quarantine or the video is lost, and <c>NotMoved</c> otherwise. A companion that can't move
    /// stays, named on its row; a video that can't move, a rename that moved another file, or the gates closing on the way
    /// stop it.
    /// </summary>
    private async Task<RemovalEnd> MoveNamesAsync(CopyRemovalEntity removal, IReadOnlyList<CopyRemovalNameEntity> names, AuditActor actor)
    {
        var moved = 0;
        string? reason = null;
        foreach (var name in names.Where(name => name.State == ReplacementNameState.Planned))
        {
            var (intent, gate) = await IntendAsync(removal, name, actor);
            if (intent == NameIntent.Taken)
                return new RemovalEnd(removal.Id, ChangedMeanwhileError, NoticeDue: false);
            if (intent == NameIntent.SwitchedOff)
            {
                reason = SwitchedOffWhileMovingReason(await CountMovedAsync(removal.Id));
                break;
            }

            if (intent == NameIntent.LeftTicked)
            {
                reason = LeftTickedWhileMovingReason(name.AppPath, await CountMovedAsync(removal.Id));
                break;
            }

            // Once its intent is committed the rename is decided: never cancelled. Inside the roots read under the intent's
            // lock, so a Save of the Media folders can't have taken this one's away in between.
            var result = await quarantine.MoveIntoQuarantineAsync(
                name.AppPath, QuarantineNameChecks.FactsOf(name), name.QuarantinePath, gate!.AllowedRoots, CancellationToken.None);
            if (result is { Status: FileQuarantineStatus.Done, Value: { } facts })
            {
                await RecordMovedAsync(name, facts, actor);
                moved++;
                continue;
            }

            // A rename that moved another file — one put at the name after the last look — leaves that file in the
            // quarantine and the copy's own file nowhere the app knows: the name is lost, and the owner must look — at the
            // quarantine path the sentence names.
            if (result.Status == FileQuarantineStatus.Swapped)
            {
                var problem = SwappedWhileMovingProblem(name.AppPath, name.QuarantinePath);
                await RecordNotMovedAsync(name, ReplacementNameState.Lost, problem, actor);
                reason = NameNotMovedReason(name.AppPath, problem, await CountMovedAsync(removal.Id));
                break;
            }

            var error = result.Error ?? QuarantineNameChecks.NoReasonError;
            await RecordNotMovedAsync(name, ReplacementNameState.Stayed, error, actor);
            // A companion that can't move stays, named on its row, and its video stays moved; a video that can't move stops
            // the removal with nothing in the quarantine.
            if (name.Kind == ReplacementNameKind.Video)
            {
                reason = NameNotMovedReason(name.AppPath, error, await CountMovedAsync(removal.Id));
                break;
            }
        }

        return await SettleAsync(removal, reason, noticeDue: moved > 0, actor);
    }

    /// <summary>
    /// The intent of <paramref name="name"/>'s rename, committed before the call while <paramref name="removal"/> is still
    /// <c>Moving</c>: the name <c>Moving</c>, with a <c>CopyRemovalNameMoving</c> row — once the gates, read under the
    /// transaction's write lock, still let it move (ADR 0033, the mover's rule). What became of it, with the gates it read.
    /// </summary>
    private async Task<(NameIntent Intent, ReplacementGateState? Gate)> IntendAsync(CopyRemovalEntity removal, CopyRemovalNameEntity name, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.CopyRemovals.HoldAsync(removal.Id, CopyRemovalState.Moving) == 0)
            return (NameIntent.Taken, null);

        var gate = await gates.ReadAsync(CancellationToken.None);
        if (!gate.IsEnabled || !QuarantineNameChecks.StillTicked(name, gate))
            return (gate.IsEnabled ? NameIntent.LeftTicked : NameIntent.SwitchedOff, gate);

        if (await db.CopyRemovalNames.StartNameMoveAsync(name.Id) == 0)
            return (NameIntent.Taken, null);

        auditTrail.Record(AuditAction.CopyRemovalNameMoving, actor, AuditTarget.CopyRemovalName(name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return (NameIntent.Intended, gate);
    }

    /// <summary>The outcome of <paramref name="name"/>'s rename that was done, committed after it: <c>Quarantined</c> with
    /// what the file system said at its new name, and a <c>CopyRemovalNameMoved</c> row.</summary>
    private async Task RecordMovedAsync(CopyRemovalNameEntity name, FileFacts facts, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.CopyRemovalNames.RecordNameMovedAsync(name.Id, facts, time.GetUtcNow().UtcDateTime) == 0)
            return;

        auditTrail.Record(AuditAction.CopyRemovalNameMoved, actor, AuditTarget.CopyRemovalName(name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    /// <summary>The outcome of <paramref name="name"/>'s rename that wasn't done — <c>Stayed</c> where it was — or that
    /// moved another file — <c>Lost</c> —, committed after it with <paramref name="problem"/>, and a
    /// <c>CopyRemovalNameNotMoved</c> row.</summary>
    private async Task RecordNotMovedAsync(CopyRemovalNameEntity name, ReplacementNameState state, string problem, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.CopyRemovalNames.RecordNameNotMovedAsync(name.Id, state, problem) == 0)
            return;

        auditTrail.Record(AuditAction.CopyRemovalNameNotMoved, actor, AuditTarget.CopyRemovalName(name));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    /// <summary>
    /// Ends the move of <paramref name="removal"/>, in one transaction while it is still <c>Moving</c>: <c>Quarantined</c>
    /// when no <paramref name="reason"/> stopped it; otherwise <c>NeedsReview</c> with the reason while a name of it is in the
    /// quarantine — Restore or Delete for good settles it — or its video is lost, and <c>NotMoved</c> with the reason when
    /// the video's rename was refused with nothing in the quarantine, which ends it. A library notice owed from then when
    /// <paramref name="noticeDue"/>, and the end's audit row.
    /// </summary>
    private async Task<RemovalEnd> SettleAsync(CopyRemovalEntity removal, string? reason, bool noticeDue, AuditActor actor)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        var now = time.GetUtcNow().UtcDateTime;
        var names = await db.CopyRemovalNames
            .Where(name => name.CopyRemovalId == removal.Id)
            .Select(name => new { name.Kind, name.State })
            .ToListAsync(CancellationToken.None);
        // A video whose rename moved another file is lost: what sits at its quarantine name is no part of the copy, and the
        // copy's own file is nowhere the app knows. Never NotMoved, which ends the removal and takes it off Removed copies,
        // while the owner has yet to look for it; the request is still refused, with the sentence saying where to look.
        var videoLost = names.Any(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Lost });
        var state = reason is null
            ? CopyRemovalState.Quarantined
            : videoLost || names.Any(name => name.State == ReplacementNameState.Quarantined)
                ? CopyRemovalState.NeedsReview
                : CopyRemovalState.NotMoved;
        if (await db.CopyRemovals.MoveAsync(removal.Id, CopyRemovalState.Moving, state, reason, now, noticeDue ? now : null) == 0)
            return new RemovalEnd(removal.Id, ChangedMeanwhileError, NoticeDue: false);

        auditTrail.Record(
            state switch
            {
                CopyRemovalState.Quarantined => AuditAction.CopyRemoved,
                CopyRemovalState.NeedsReview => AuditAction.CopyRemovalNeedsReview,
                _ => AuditAction.CopyRemovalNotMoved,
            },
            actor,
            AuditTarget.CopyRemoval(removal));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        LogMove(removal.Id, CopyRemovalState.Moving, state);
        // A removal that waits for review is an answer like any other: the page shows it under Removed copies, with its
        // reason. Only one whose video didn't go into the quarantine is refused.
        return new RemovalEnd(removal.Id, state == CopyRemovalState.NotMoved || videoLost ? reason : null, noticeDue);
    }

    /// <summary>How many of the removal's names are in the quarantine now, of every name its move planned.</summary>
    private async Task<(int Count, int Total)> CountMovedAsync(Guid copyRemovalId) =>
        QuarantineNameCounts.Moved(await db.CopyRemovalNames
            .AsNoTracking()
            .Where(name => name.CopyRemovalId == copyRemovalId)
            .Select(name => name.State)
            .ToListAsync(CancellationToken.None));

    /// <summary>How a Remove ended: the removal, why the owner is refused — it moved nothing, or changed under it — and
    /// whether the library is owed a notice.</summary>
    private sealed record RemovalEnd(Guid CopyRemovalId, string? Refusal, bool NoticeDue);

    /// <summary>What a name's intent came to: written, or not — the removal moved on, or the gates read under its lock closed
    /// on it.</summary>
    private enum NameIntent
    {
        /// <summary>Written: the name is <c>Moving</c>, and its rename is decided.</summary>
        Intended = 1,

        /// <summary>Another writer moved the removal, or the name, first.</summary>
        Taken = 2,

        /// <summary>Replacing and removing copies was switched off.</summary>
        SwitchedOff = 3,

        /// <summary>The name's folder is no longer ticked, or its root no longer allowed.</summary>
        LeftTicked = 4,
    }
}
