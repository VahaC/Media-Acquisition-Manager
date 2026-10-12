using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The checks right before a move, which only read: check 5 of every ticked copy still to move, its own companions, the
// companions an earlier run left waiting, and check 6 — every download client's torrents, for every name the move would
// rename, the clients the app sent a replacement to whatever their mappings (ADR 0030, ADR 0031, ADR 0033).
public sealed partial class ReplacementQuarantiner
{
    /// <summary>
    /// What <paramref name="job"/>'s move would be, read now inside <paramref name="gate"/>'s allowed roots: each ticked copy
    /// the owner didn't drop whose video isn't in the quarantine yet (or deleted from it) by an earlier run, with its
    /// identity read fresh, the allowed root that holds it and its own companions, each marked when a torrent holds it; and
    /// the companions an earlier run left waiting beside a video it moved, each checked again — or why nothing of it may
    /// move, with the copies the stop is about. Reads the database, the disk and the download clients; writes nothing.
    /// </summary>
    private async Task<MoveCheck> CheckAsync(QuarantineJob job, ReplacementGateState gate, CancellationToken cancellationToken)
    {
        var replacementJobId = job.Replacement.Id;
        // MAM-27 — a copy the owner dropped stays where it is: never checked, planned or renamed, and a torrent holding it
        // stops nothing. It holds no name still to move or in the quarantine — the drop removes the names that never moved
        // and refuses a copy one of whose names left its folder —, so the names below need no filter.
        var copies = await db.ReplacementJobFiles
            .AsNoTracking()
            .Where(copy => copy.ReplacementJobId == replacementJobId && copy.DroppedUtc == null)
            .OrderBy(copy => copy.Position)
            .ToListAsync(cancellationToken);
        var earlier = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken);
        var moved = earlier
            .Where(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Quarantined or ReplacementNameState.Deleted })
            .Select(name => name.ReplacementJobFileId)
            .ToHashSet();
        // A stop between a video and its companions — the switch, a tick, a restart — left them waiting where they are; a
        // Retry's run takes them up again, or they'd stay in the library for good while their video waits in the quarantine.
        var inQuarantine = earlier
            .Where(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Quarantined })
            .Select(name => name.ReplacementJobFileId)
            .ToHashSet();
        var waiting = earlier
            .Where(name => name is { Kind: ReplacementNameKind.Companion, State: ReplacementNameState.Planned } && inQuarantine.Contains(name.ReplacementJobFileId))
            .ToList();
        var newFiles = await db.ReplacementJobNewFiles
            .AsNoTracking()
            .Where(file => file.ReplacementJobId == replacementJobId)
            .ToListAsync(cancellationToken);
        var roots = QuarantineCopyCheck.RootsOf(gate);
        var toMove = copies.Where(copy => !moved.Contains(copy.Id)).ToList();

        // ── check 5, copy by copy ──
        var checkedCopies = new List<CheckedCopy>();
        foreach (var copy in toMove)
        {
            var (checkedCopy, reason) = await CheckCopyAsync(copy, gate, roots, newFiles, cancellationToken);
            if (reason is not null)
                return MoveCheck.Stopped(toMove, reason, new Dictionary<Guid, string> { [copy.Id] = reason });
            checkedCopies.Add(checkedCopy!);
        }

        // ── the companions ──
        var planned = new HashSet<NormalizedPath>(checkedCopies.Select(copy => copy.AppPath));
        var companionsOf = new Dictionary<Guid, List<PlannedCompanion>>();
        foreach (var copy in checkedCopies)
        {
            // MAM-40 — listed as a removal lists its copy's (QuarantineCompanionListing): the folder, the link, the identities.
            var (listed, reason) = await QuarantineCompanionListing.ListAsync(quarantine, copy.AppPath, copy.RootText, cancellationToken);
            if (reason is not null)
                return MoveCheck.Stopped(toMove, reason, new Dictionary<Guid, string> { [copy.Copy.Id] = reason });

            var companions = companionsOf[copy.Copy.Id] = [];
            foreach (var (path, facts) in listed)
            {
                // Each name once: two ticked videos of one folder never share a companion by the rule, and this keeps it so.
                if (planned.Add(path))
                    companions.Add(new PlannedCompanion(path, facts, null));
            }
        }

        // ── the companions left waiting ──
        var leftovers = new List<WaitingCompanion>();
        foreach (var name in waiting)
            leftovers.Add(await CheckWaitingAsync(name, gate, cancellationToken));

        // ── check 6: one read of every client for every name ──
        List<ReplacementHoldingTarget> targets =
        [
            .. checkedCopies.SelectMany(copy => companionsOf[copy.Copy.Id]
                .Select(companion => new ReplacementHoldingTarget(companion.AppPath, companion.Facts))
                .Prepend(new ReplacementHoldingTarget(copy.AppPath, copy.Fresh))),
            // MAM-28 — with its facts read now, as a video's are: its link count may have changed since the plan wrote
            // its row, and a file outside the Media folders only of its size counts for nothing while it has other names.
            .. leftovers
                .Where(leftover => leftover.State == ReplacementNameState.Planned)
                .Select(leftover => new ReplacementHoldingTarget(leftover.AppPath!, leftover.Fresh!)),
        ];
        // Nothing left to rename — a Retry of a job whose every video is in the quarantine already or deleted from it —
        // asks no client: no torrent can hold what doesn't move, and a client that is down would stop the job for nothing.
        if (targets.Count == 0)
            return MoveCheck.Planned([], leftovers);

        // MAM-28 — every client the app sent a replacement to that still exists, asked whatever its mappings say now
        // (QuarantineHoldingCheck.ReadAsync says why); MAM-40 — read as a removal reads them.
        var holdings = await QuarantineHoldingCheck.ReadAsync(db, holdingReader, targets, gate.AllowedRoots, cancellationToken);

        // Anything uncertain is never an all-clear (ADR 0031): a client not read, or read in part, stops the job.
        var uncertain = QuarantineHoldingCheck.UncertainOf(holdings);
        if (uncertain.Count > 0)
            return MoveCheck.Stopped(toMove, QuarantineHoldingCheck.UncertainReason(uncertain, QuarantineWording.Replacement), new Dictionary<Guid, string>());

        // A video a torrent lists — its own path, or reached through a link — stays until the owner removes the torrent.
        var heldCopies = checkedCopies
            .Select(copy => (CopyId: copy.Copy.Id, Holds: QuarantineHoldingCheck.HoldsOf(holdings, copy.AppPath)))
            .Where(held => held.Holds.Count > 0)
            .ToList();
        if (heldCopies.Count > 0)
        {
            return MoveCheck.Stopped(
                toMove,
                QuarantineHoldingCheck.HeldReason([.. heldCopies.SelectMany(held => held.Holds)], QuarantineWording.Replacement),
                heldCopies.ToDictionary(held => held.CopyId, held => QuarantineHoldingCheck.HeldReason(held.Holds, QuarantineWording.Replacement)));
        }

        // A companion a torrent lists stays, named on the job; its video still moves. A second name of a torrent's file
        // moves: the torrent's own name stays, and the row's link count says moving it frees nothing.
        return MoveCheck.Planned(
            [
                .. checkedCopies.Select(copy => copy with
                {
                    Companions =
                    [
                        .. companionsOf[copy.Copy.Id].Select(companion =>
                            QuarantineHoldingCheck.HoldsOf(holdings, companion.AppPath) is { Count: > 0 } holds
                                ? companion with { HeldBy = QuarantineHoldingCheck.CompanionHeldReason(holds) }
                                : companion),
                    ],
                }),
            ],
            [
                .. leftovers.Select(leftover =>
                    leftover.State == ReplacementNameState.Planned && QuarantineHoldingCheck.HoldsOf(holdings, leftover.AppPath!) is { Count: > 0 } holds
                        ? leftover with { State = ReplacementNameState.Stayed, Problem = QuarantineHoldingCheck.CompanionHeldReason(holds) }
                        : leftover),
            ]);
    }

    /// <summary>
    /// Check 5 of one ticked copy, right before the move: still inside the Media folders and in a ticked folder, no clip of
    /// a disc, still the file picked (path, device and inode, size, modification time), never one of the new files, and
    /// its new file still the one verified. The copy as read now, or the first reason it stays.
    /// </summary>
    private async Task<(CheckedCopy? Copy, string? Reason)> CheckCopyAsync(
        ReplacementJobFileEntity copy,
        ReplacementGateState gate,
        IReadOnlyList<(string Text, NormalizedPath Path)> roots,
        IReadOnlyList<ReplacementJobNewFileEntity> newFiles,
        CancellationToken cancellationToken)
    {
        // MAM-40 — the half a removed copy is checked by too (QuarantineCopyCheck); the sentences are the mover's own.
        var look = await QuarantineCopyCheck.LookAsync(files, copy.AppPath, copy.DeviceId, copy.Inode, copy.SizeBytes, copy.ModifiedUtc, gate, roots, cancellationToken);
        if (look.Stop is { } stop)
        {
            return (null, stop switch
            {
                QuarantineCopyStop.OutsideMediaFolders => OutsideMediaFoldersReason(copy.AppPath),
                QuarantineCopyStop.NotTicked => NotTickedReason(copy.AppPath),
                QuarantineCopyStop.DiscClip => DiscClipReason(copy.AppPath),
                QuarantineCopyStop.Gone => GoneReason(copy.AppPath),
                QuarantineCopyStop.Changed => ChangedReason(copy.AppPath),
                _ => NotReadReason(copy.AppPath, look.Error),
            });
        }

        var appPath = look.AppPath!;

        // Never the new file itself, by its path or its identity: moving it would take the replacement away.
        if (newFiles.Any(file => (file.AppPath is { } newPath && NormalizedPath.TryParse(newPath, out var parsed, out _) && parsed.IsSameAs(appPath))
            || (file.DeviceId == copy.DeviceId && file.Inode == copy.Inode)))
            return (null, NewFileReason(copy.AppPath));

        // The pair the check found still holds: its new file is there, still the file the check read.
        var pair = copy.PairedNewFileId is { } pairId ? newFiles.FirstOrDefault(file => file.Id == pairId) : null;
        if (pair is not { AppPath: not null } || await ReplacementNewFileCheck.ChangedAsync(files, pair, gate.AllowedRoots, cancellationToken) is not null)
            return (null, PairChangedReason(copy.AppPath));

        return (new CheckedCopy(copy, appPath, look.RootText!, look.Root!, look.Fresh!, []), null);
    }

    /// <summary>
    /// A companion an earlier run left waiting beside the video it moved, checked again as its video was: still in a ticked
    /// folder inside the allowed root whose quarantine takes it, and still the file the plan read (device and inode, size,
    /// modification time), looked at within that root alone. Still waiting to move, or why it stays — or, gone, why it is
    /// lost; never a stop of the job, whose video is in the quarantine already.
    /// </summary>
    private async Task<WaitingCompanion> CheckWaitingAsync(ReplacementJobNameEntity name, ReplacementGateState gate, CancellationToken cancellationToken)
    {
        if (!NormalizedPath.TryParse(name.AppPath, out var appPath, out _) || !QuarantineNameChecks.StillTicked(name, gate))
            return new WaitingCompanion(name, null, ReplacementNameState.Stayed, NotTickedReason(name.AppPath));

        var described = await files.DescribeAsync(name.AppPath, [name.AllowedRoot], cancellationToken);
        return described switch
        {
            { Status: FileInventoryStatus.NotFound } => new WaitingCompanion(name, appPath, ReplacementNameState.Lost, GoneReason(name.AppPath)),
            { Status: FileInventoryStatus.Found, Value: { } fresh }
                when fresh.DeviceId == name.DeviceId && fresh.Inode == name.Inode && fresh.SizeBytes == name.SizeBytes && fresh.ModifiedUtc == name.ModifiedUtc =>
                new WaitingCompanion(name, appPath, ReplacementNameState.Planned, null, fresh),
            { Status: FileInventoryStatus.Found } => new WaitingCompanion(name, appPath, ReplacementNameState.Stayed, ChangedReason(name.AppPath)),
            _ => new WaitingCompanion(name, appPath, ReplacementNameState.Stayed, NotReadReason(name.AppPath, described.Error)),
        };
    }

    /// <summary>One ticked copy as check 5 read it now: where it is, the allowed root whose quarantine takes it, its fresh
    /// identity, and the companions that go with it.</summary>
    private sealed record CheckedCopy(
        ReplacementJobFileEntity Copy,
        NormalizedPath AppPath,
        string RootText,
        NormalizedPath Root,
        FileFacts Fresh,
        IReadOnlyList<PlannedCompanion> Companions);

    /// <summary>One companion of a copy as listed now, and the hold that keeps it where it is (<c>null</c> when none
    /// does).</summary>
    private sealed record PlannedCompanion(NormalizedPath AppPath, FileFacts Facts, string? HeldBy);

    /// <summary>A companion an earlier run left waiting, as checked now (its path parsed, <c>null</c> when it no longer
    /// does): still <c>Planned</c> to move, with what the disk says of it now (<paramref name="Fresh"/>, set only then), or
    /// the state it takes instead — <c>Stayed</c> or <c>Lost</c> — with why.</summary>
    private sealed record WaitingCompanion(
        ReplacementJobNameEntity Name, NormalizedPath? AppPath, ReplacementNameState State, string? Problem, FileFacts? Fresh = null);

    /// <summary>The checks' word on a job: the copies to move with their companions and the companions an earlier run left
    /// waiting (<paramref name="Waiting"/>), or why nothing moves — the job's reason, and each copy's own
    /// (<paramref name="Problems"/>) among the ones it read (<paramref name="Considered"/>).</summary>
    private sealed record MoveCheck(
        IReadOnlyList<CheckedCopy> Copies,
        IReadOnlyList<WaitingCompanion> Waiting,
        string? Reason,
        IReadOnlyList<ReplacementJobFileEntity> Considered,
        IReadOnlyDictionary<Guid, string> Problems)
    {
        public static MoveCheck Planned(IReadOnlyList<CheckedCopy> copies, IReadOnlyList<WaitingCompanion> waiting) =>
            new(copies, waiting, null, [], new Dictionary<Guid, string>());

        public static MoveCheck Stopped(IReadOnlyList<ReplacementJobFileEntity> considered, string reason, IReadOnlyDictionary<Guid, string> problems) =>
            new([], [], reason, considered, problems);
    }
}
