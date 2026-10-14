using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <inheritdoc cref="ICopyRemovalRecovery"/>
/// <remarks>
/// <para>
/// A replacement's recovery's rules (<c>ReplacementQuarantiner.Recovery.cs</c>) and its finder
/// (<see cref="QuarantineNameFinder"/>): each name in the middle of a write looked up where it was and where it was going,
/// what was found recorded with the outcome row the write would have written, and the removal settled in one transaction,
/// with no actor. One deviation, as a removal has no Retry: it waits for review only while a name of it is still in the
/// quarantine — Restore or Delete for good settles it — or a move's video was found nowhere, and otherwise ends:
/// <c>NotMoved</c> from a move, <c>Restored</c> or <c>Deleted</c> from the owner's write, the reason saying how far it got
/// when it didn't finish.
/// </para>
/// <para>
/// Information: a removal's id, its states and counts, never a path, a title or a sentence.
/// </para>
/// </remarks>
public sealed class CopyRemovalRecovery(
    ApplicationDbContext db,
    IFileInventory files,
    ICopyRemovalLibraryNotice notice,
    CopyRemovalsInHand inHand,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<CopyRemovalRecovery> logger) : ICopyRemovalRecovery
{
    public async Task SettleAsync(CancellationToken cancellationToken = default)
    {
        // ── the removals a restart or a crash cut ──
        var cut = await db.CopyRemovals
            .AsNoTracking()
            .Where(removal => EF.Constant(CopyRemovalStates.Unsettled).Contains(removal.State))
            .OrderBy(removal => removal.CreatedUtc)
            .ThenBy(removal => removal.Id)
            .ToListAsync(cancellationToken);
        foreach (var removal in cut)
            await TakeAsync(removal, cancellationToken);

        // ── the library notices still owed ──
        var due = await db.CopyRemovals
            .AsNoTracking()
            .Where(removal => removal.LibraryNoticeDueUtc != null)
            .OrderBy(removal => removal.CreatedUtc)
            .ThenBy(removal => removal.Id)
            .Select(removal => removal.Id)
            .ToListAsync(cancellationToken);
        foreach (var copyRemovalId in due)
            await TellAsync(copyRemovalId, cancellationToken);

        // Debug, not Information: a pass runs every 15 s.
        if (cut.Count + due.Count > 0)
            logger.LogDebug("Looked at {CutCount} cut copy removals and owed {NoticeCount} library notices.", cut.Count, due.Count);
    }

    /// <summary>
    /// Settles <paramref name="removal"/> while holding it — or not at all, when someone in this process already holds it:
    /// the owner's request is writing for it. One at a time: a bug on one costs that one this pass, never the others'.
    /// </summary>
    private async Task TakeAsync(CopyRemovalEntity removal, CancellationToken cancellationToken)
    {
        if (!inHand.TryTake(removal.Id))
            return;

        try
        {
            await RecoverAsync(removal, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Left as the cut left it, and looked at again on the next pass. Error, with the type and the stack, never the
            // message.
            logger.LogError(
                "Settling the cut copy removal {CopyRemovalId} threw {ExceptionType} {StackTrace}",
                removal.Id,
                exception.GetType().FullName,
                exception.StackTrace);
        }
        finally
        {
            inHand.Release(removal.Id);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Settles the cut <paramref name="removal"/>: every name it was writing for found again and recorded with its outcome
    /// row, then the removal's end (<see cref="EndOf"/>), in one transaction. Nothing when it was settled meanwhile, or when
    /// a name couldn't be looked up — the next pass looks again.
    /// </summary>
    private async Task RecoverAsync(CopyRemovalEntity removal, CancellationToken cancellationToken)
    {
        var state = removal.State;
        // Read again now that the pass holds it: an owner's write that ended between the pass's read and the take left it
        // settled, and its names with it.
        var current = await db.CopyRemovals
            .AsNoTracking()
            .Where(row => row.Id == removal.Id)
            .Select(row => (CopyRemovalState?)row.State)
            .SingleOrDefaultAsync(cancellationToken);
        if (current != state)
            return;

        var names = await db.CopyRemovalNames
            .AsNoTracking()
            .Where(name => name.CopyRemovalId == removal.Id)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken);
        var roots = await db.AllowedRoots.AsNoTracking().OrderBy(root => root.Position).Select(root => root.Path).ToListAsync(cancellationToken);

        // Every disk read before the transaction: none is held across one.
        var found = new List<(CopyRemovalNameEntity Name, ReplacementNameState State, string? Problem)>();
        var undecided = 0;
        foreach (var name in names.Where(name => name.State is ReplacementNameState.Moving or ReplacementNameState.Restoring or ReplacementNameState.Deleting))
        {
            if (await QuarantineNameFinder.FindAsync(files, name, roots, cancellationToken) is { } outcome)
                found.Add((name, outcome.State, outcome.Problem));
            else
                undecided++;
        }

        // A look that failed says nothing of where a name is: recording it lost or deleted would be for good. Warning, so
        // it ships off the box: a removal can't wait here unseen.
        if (undecided > 0)
        {
            logger.LogWarning(
                "Copy removal {CopyRemovalId} was cut while {State}, and {UndecidedCount} of its {NameCount} names couldn't be looked up; the next pass looks again.",
                removal.Id,
                state,
                undecided,
                names.Count);
            return;
        }

        var states = names.Select(name => found.FirstOrDefault(entry => entry.Name.Id == name.Id) is { Name: not null } entry ? entry.State : name.State).ToList();
        var lostVideo = names.Where((name, index) => name.Kind == ReplacementNameKind.Video && states[index] == ReplacementNameState.Lost).FirstOrDefault();
        var (end, reason, noticeDue, endAction) = EndOf(state, states, lostVideo);

        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.CopyRemovals.HoldAsync(removal.Id, state) == 0)
            return;

        foreach (var (name, foundState, problem) in found)
        {
            if (await db.CopyRemovalNames.RecordNameFoundAsync(name.Id, name.State, foundState, problem, now) == 1)
                auditTrail.Record(FoundActionOf(name.State, foundState), null, AuditTarget.CopyRemovalName(name));
        }

        // The names' outcomes in a save of their own before the removal's end, so the trail reads in that order.
        await db.SaveChangesAsync(CancellationToken.None);
        await db.CopyRemovals.MoveAsync(removal.Id, state, end, reason, now, noticeDue ? now : null);
        auditTrail.Record(endAction, null, AuditTarget.CopyRemoval(removal));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        logger.LogInformation(
            "Copy removal {CopyRemovalId} was cut while {State}: {FoundCount} of its {NameCount} names were found again; it went to {End}.",
            removal.Id,
            state,
            found.Count,
            names.Count,
            end);
    }

    /// <summary>
    /// Where a write cut while <paramref name="state"/>, its names now in <paramref name="states"/>, ends: where the owner's
    /// write would have ended it when the cut came after its last name; in review with how far it got while a name is still
    /// in the quarantine — or, for a move, with where to look when its video was found nowhere (<paramref name="lostVideo"/>);
    /// otherwise at the write's own end — <c>NotMoved</c> for a move —, the reason saying how far it got. The library is owed
    /// a notice when a name left it or came back.
    /// </summary>
    private static (CopyRemovalState End, string? Reason, bool NoticeDue, AuditAction EndAction) EndOf(
        CopyRemovalState state, IReadOnlyList<ReplacementNameState> states, CopyRemovalNameEntity? lostVideo)
    {
        var held = states.Any(name => name == ReplacementNameState.Quarantined);
        switch (state)
        {
            case CopyRemovalState.Moving:
                var moved = QuarantineNameCounts.Moved(states);
                // A video found neither where it was nor in the quarantine was never "not moved": NotMoved would end the
                // removal and take it off Removed copies while the owner has yet to look for the file.
                if (lostVideo is not null)
                    return (CopyRemovalState.NeedsReview, LostWhileMovingReason(lostVideo.AppPath, lostVideo.QuarantinePath), held, AuditAction.CopyRemovalNeedsReview);
                return held
                    ? (CopyRemovalState.NeedsReview, CutWhileMovingReason(moved), true, AuditAction.CopyRemovalNeedsReview)
                    : (CopyRemovalState.NotMoved, CutWhileMovingReason(moved), false, AuditAction.CopyRemovalNotMoved);
            case CopyRemovalState.Restoring:
                var restored = QuarantineNameCounts.Restored(states);
                if (restored.Total > 0 && restored.Count == restored.Total)
                    return (CopyRemovalState.Restored, null, true, AuditAction.CopyRemovalRestored);
                return held
                    ? (CopyRemovalState.NeedsReview, CutWhileRestoringReason(restored), restored.Count > 0, AuditAction.CopyRemovalNeedsReview)
                    : (CopyRemovalState.Restored, CutWhileRestoringReason(restored), restored.Count > 0, AuditAction.CopyRemovalRestored);
            default:
                var deleted = QuarantineNameCounts.Deleted(states);
                if (deleted.Total > 0 && deleted.Count == deleted.Total)
                    return (CopyRemovalState.Deleted, null, false, AuditAction.CopyRemovalDeletedForGood);
                return held
                    ? (CopyRemovalState.NeedsReview, CutWhileDeletingReason(deleted), false, AuditAction.CopyRemovalNeedsReview)
                    : (CopyRemovalState.Deleted, CutWhileDeletingReason(deleted), false, AuditAction.CopyRemovalDeletedForGood);
        }
    }

    /// <summary>The outcome row of a write a restart cut, as the write itself would have written it.</summary>
    private static AuditAction FoundActionOf(ReplacementNameState intent, ReplacementNameState found) => (intent, found) switch
    {
        (ReplacementNameState.Moving, ReplacementNameState.Quarantined) => AuditAction.CopyRemovalNameMoved,
        (ReplacementNameState.Moving, _) => AuditAction.CopyRemovalNameNotMoved,
        (ReplacementNameState.Restoring, ReplacementNameState.Restored) => AuditAction.CopyRemovalNameRestored,
        (ReplacementNameState.Restoring, _) => AuditAction.CopyRemovalNameNotRestored,
        (ReplacementNameState.Deleting, ReplacementNameState.Deleted) => AuditAction.CopyRemovalNameDeleted,
        (ReplacementNameState.Deleting, _) => AuditAction.CopyRemovalNameNotDeleted,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Only a write's intent is found again."),
    };

    /// <summary>
    /// Sends the library notice <paramref name="copyRemovalId"/> owes, on its own: the notice never throws but for the
    /// caller's cancellation, and a bug in it costs that notice this pass, never the others'.
    /// </summary>
    private async Task TellAsync(Guid copyRemovalId, CancellationToken cancellationToken)
    {
        try
        {
            await notice.SendDueAsync(copyRemovalId, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogError(
                "Telling the library about copy removal {CopyRemovalId} threw {ExceptionType} {StackTrace}",
                copyRemovalId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // ── a restart's cut ──

    private static string CutWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon("The app stopped while moving this copy", ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    // The video looked up by device and inode where it was and where it was going, and found in neither: both paths in
    // full, as that is where the owner looks — the removal waits for review with no button to press.
    private static string LostWhileMovingReason(string path, string quarantinePath) =>
        $"The app stopped while moving this copy, and found its video neither at {ReplacementText.Quote(path)} nor in the quarantine at "
        + $"{ReplacementText.Quote(quarantinePath)}. Look in both places to see where it went.";

    private static string CutWhileRestoringReason((int Count, int Total) restored) =>
        ReplacementNameTally.AfterColon("The app stopped while putting this copy back", ReplacementNameTally.BackWhereTheyWere(restored.Count, restored.Total));

    private static string CutWhileDeletingReason((int Count, int Total) deleted) =>
        ReplacementNameTally.AfterColon("The app stopped while deleting this copy permanently", ReplacementNameTally.Deleted(deleted.Count, deleted.Total));
}
