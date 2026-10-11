using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The recovery of a job a restart or a crash cut in the middle of a write to the media (ADR 0030): each name in the middle
// of one looked up by device and inode where it was and where it was going, what was found recorded, and the job stopped
// for the owner's review — or ended as the write would have ended it, when the cut came after its last name. It renames and
// deletes nothing by itself, and decides nothing on a look that failed.
public sealed partial class ReplacementQuarantiner
{
    /// <summary>
    /// Settles the cut <paramref name="job"/> — still <c>Quarantining</c>, <c>Restoring</c> or <c>Deleting</c>, held by no
    /// one but this pass —: every name it was writing for found again and recorded with its outcome row, then the job
    /// <c>NeedsReview</c> with how far the write got, in one transaction, with a <c>ReplacementNeedsReview</c> row of the
    /// app's own. A restore that put every name back ends <c>Restored</c>, a deletion that deleted every name
    /// <c>Completed</c>, with the row their own end writes: nothing is left for the owner to review, and nothing to retry.
    /// Nothing when it was settled meanwhile, or when a name couldn't be looked up — the next pass looks again.
    /// </summary>
    private async Task RecoverAsync(QuarantineJob job, CancellationToken cancellationToken)
    {
        var replacementJobId = job.Replacement.Id;
        var state = job.Replacement.State;
        // Read again now that the pass holds it: an owner's Restore or Delete for good that ended between the pass's read
        // and the take left it settled, and its names with it.
        var current = await db.ReplacementJobs
            .AsNoTracking()
            .Where(replacement => replacement.Id == replacementJobId)
            .Select(replacement => (ReplacementJobState?)replacement.State)
            .SingleOrDefaultAsync(cancellationToken);
        if (current != state)
            return;

        var names = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken);
        var roots = await db.AllowedRoots.AsNoTracking().OrderBy(root => root.Position).Select(root => root.Path).ToListAsync(cancellationToken);

        // Every disk read before the transaction: none is held across one.
        var found = new List<(ReplacementJobNameEntity Name, ReplacementNameState State, string? Problem)>();
        var undecided = 0;
        foreach (var name in names.Where(name => name.State is ReplacementNameState.Moving or ReplacementNameState.Restoring or ReplacementNameState.Deleting))
        {
            if (await FindAsync(name, roots, cancellationToken) is { } outcome)
                found.Add((name, outcome.State, outcome.Problem));
            else
                undecided++;
        }

        // A look that failed — a read error, a mount not there — says nothing of where a name is: recording it lost or
        // deleted would be for good. The job stays as the cut left it, and the next pass looks again. Warning, so it ships
        // off the box: a job can't wait here unseen.
        if (undecided > 0)
        {
            logger.LogWarning(
                "Replacement job {ReplacementJobId} was cut while {State}, and {UndecidedCount} of its {NameCount} names couldn't be looked up; the next pass looks again.",
                replacementJobId,
                state,
                undecided,
                names.Count);
            return;
        }

        var states = names.Select(name => found.FirstOrDefault(entry => entry.Name.Id == name.Id) is { Name: not null } entry ? entry.State : name.State).ToList();
        var (end, reason, noticeDue, endAction) = EndOf(state, states);

        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        if (await db.ReplacementJobs.ClaimAsync(replacementJobId, state) == 0)
        {
            db.ChangeTracker.Clear();
            return;
        }

        foreach (var (name, foundState, problem) in found)
        {
            if (await db.ReplacementJobNames.RecordNameFoundAsync(name.Id, name.State, foundState, problem, now) == 1)
                auditTrail.Record(FoundActionOf(name.State, foundState), null, AuditTarget.ReplacementJobName(name));
        }

        // The names' outcomes in a save of their own before the job's end, so the trail reads in that order.
        await db.SaveChangesAsync(CancellationToken.None);
        await db.ReplacementJobs.SettleUnsettledAsync(replacementJobId, state, end, reason, now, noticeDue);
        auditTrail.Record(endAction, null, TargetOf(job));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        logger.LogInformation(
            "Replacement job {ReplacementJobId} was cut while {State}: {FoundCount} of its {NameCount} names were found again.",
            replacementJobId,
            state,
            found.Count,
            names.Count);
        LogMove(replacementJobId, state, end);
    }

    /// <summary>
    /// Where a write cut while <paramref name="state"/>, its names now in <paramref name="states"/>, ends: in review with how
    /// far it got — the library owed a notice when a name left it or came back —, unless a restore put every name back or a
    /// deletion deleted every one, which ends where the owner's write would have ended (MAM-26: a deviation from "cut →
    /// NeedsReview", as a review there could only end in a Retry that moves the restored copies back, or in nothing at
    /// all).
    /// </summary>
    private static (ReplacementJobState End, string? Reason, bool NoticeDue, AuditAction EndAction) EndOf(
        ReplacementJobState state, IReadOnlyList<ReplacementNameState> states)
    {
        switch (state)
        {
            case ReplacementJobState.Quarantining:
                var moved = MovedOf(states);
                return (ReplacementJobState.NeedsReview, CutWhileMovingReason(moved), moved.Count > 0, AuditAction.ReplacementNeedsReview);
            case ReplacementJobState.Restoring:
                var restored = RestoredOf(states);
                return restored.Total > 0 && restored.Count == restored.Total
                    ? (ReplacementJobState.Restored, null, true, AuditAction.ReplacementRestored)
                    : (ReplacementJobState.NeedsReview, CutWhileRestoringReason(restored), restored.Count > 0, AuditAction.ReplacementNeedsReview);
            default:
                var deleted = DeletedOf(states);
                return deleted.Total > 0 && deleted.Count == deleted.Total
                    ? (ReplacementJobState.Completed, null, false, AuditAction.ReplacementCompleted)
                    : (ReplacementJobState.NeedsReview, CutWhileDeletingReason(deleted), false, AuditAction.ReplacementNeedsReview);
        }
    }

    /// <summary>
    /// Where <paramref name="name"/>, cut in the middle of a write, is now, by device and inode, inside
    /// <paramref name="roots"/>: a rename into the quarantine done or not, a rename back done or not, a deletion done or not
    /// — or neither, which loses it, with why. <c>null</c> when that can't be told: a look that didn't answer found or not
    /// found, or a name found nowhere while neither the folder it was in nor the one it was going to is there — a mount
    /// missing, an empty mount point —, where its absence proves nothing.
    /// </summary>
    private async Task<(ReplacementNameState State, string? Problem)?> FindAsync(
        ReplacementJobNameEntity name, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var inQuarantine = await files.DescribeAsync(name.QuarantinePath, roots, cancellationToken);
        if (!IsAnswer(inQuarantine))
            return null;

        switch (name.State)
        {
            case ReplacementNameState.Moving:
            {
                if (IsThere(inQuarantine, name))
                    return (ReplacementNameState.Quarantined, null);
                var atOldPath = await files.DescribeAsync(name.AppPath, roots, cancellationToken);
                if (!IsAnswer(atOldPath))
                    return null;
                if (IsThere(atOldPath, name))
                    return (ReplacementNameState.Planned, null);
                return await PlaceIsThereAsync(name, roots, cancellationToken) ? (ReplacementNameState.Lost, LostWhileMovingProblem(name.AppPath)) : null;
            }

            case ReplacementNameState.Restoring:
            {
                var atOldPath = await files.DescribeAsync(name.AppPath, roots, cancellationToken);
                if (!IsAnswer(atOldPath))
                    return null;
                if (IsThere(atOldPath, name))
                    return (ReplacementNameState.Restored, null);
                if (IsThere(inQuarantine, name))
                    return (ReplacementNameState.Quarantined, null);
                return await PlaceIsThereAsync(name, roots, cancellationToken) ? (ReplacementNameState.Lost, LostWhileRestoringProblem(name.AppPath)) : null;
            }

            default:
                if (inQuarantine.Status == FileInventoryStatus.NotFound)
                    return await PlaceIsThereAsync(name, roots, cancellationToken) ? (ReplacementNameState.Deleted, null) : null;
                return IsThere(inQuarantine, name)
                    ? (ReplacementNameState.Quarantined, null)
                    : (ReplacementNameState.Lost, LostWhileDeletingProblem(name.AppPath));
        }
    }

    /// <summary>Whether the look said found or not found: anything else — unreadable, changed while read, outside the
    /// roots — leaves where the name is unknown.</summary>
    private static bool IsAnswer(FileInventoryResult<FileFacts> described) =>
        described.Status is FileInventoryStatus.Found or FileInventoryStatus.NotFound;

    /// <summary>
    /// Whether <paramref name="name"/>'s places can be read for an absence: the folder it was in, or its folder in the
    /// quarantine, is there — a folder answers a look as no regular file —, so the mount holding both is too. Neither
    /// folder's absence is ever the app's doing: a write never removes a folder.
    /// </summary>
    private async Task<bool> PlaceIsThereAsync(ReplacementJobNameEntity name, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        foreach (var path in new[] { name.AppPath, name.QuarantinePath })
        {
            if (NormalizedPath.TryParse(path, out var parsed, out _)
                && parsed.Segments.Count > 0
                && (await files.DescribeAsync((parsed with { Segments = [.. parsed.Segments.SkipLast(1)] }).ToString(), roots, cancellationToken)).Status
                    == FileInventoryStatus.NotARegularFile)
                return true;
        }

        return false;
    }

    /// <summary>Whether the look found <paramref name="name"/>'s own file: its device and inode, which a rename keeps.</summary>
    private static bool IsThere(FileInventoryResult<FileFacts> described, ReplacementJobNameEntity name) =>
        described is { Status: FileInventoryStatus.Found, Value: { } facts } && facts.DeviceId == name.DeviceId && facts.Inode == name.Inode;

    /// <summary>The outcome row of a write a restart cut, as the write itself would have written it.</summary>
    private static AuditAction FoundActionOf(ReplacementNameState intent, ReplacementNameState found) => (intent, found) switch
    {
        (ReplacementNameState.Moving, ReplacementNameState.Quarantined) => AuditAction.ReplacementNameMoved,
        (ReplacementNameState.Moving, _) => AuditAction.ReplacementNameNotMoved,
        (ReplacementNameState.Restoring, ReplacementNameState.Restored) => AuditAction.ReplacementNameRestored,
        (ReplacementNameState.Restoring, _) => AuditAction.ReplacementNameNotRestored,
        (ReplacementNameState.Deleting, ReplacementNameState.Deleted) => AuditAction.ReplacementNameDeleted,
        (ReplacementNameState.Deleting, _) => AuditAction.ReplacementNameNotDeleted,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Only a write's intent is found again."),
    };

    /// <summary>The names back where they were, of every name a restore was putting back: in the quarantine, back, or
    /// lost on the way.</summary>
    private static (int Count, int Total) RestoredOf(IReadOnlyList<ReplacementNameState> states) =>
        (states.Count(state => state == ReplacementNameState.Restored),
            states.Count(state => state is ReplacementNameState.Quarantined or ReplacementNameState.Restored or ReplacementNameState.Lost));

    /// <summary>The names deleted for good, of every name a deletion was taking: in the quarantine, deleted, or lost on the
    /// way.</summary>
    private static (int Count, int Total) DeletedOf(IReadOnlyList<ReplacementNameState> states) =>
        (states.Count(state => state == ReplacementNameState.Deleted),
            states.Count(state => state is ReplacementNameState.Quarantined or ReplacementNameState.Deleted or ReplacementNameState.Lost));
}
