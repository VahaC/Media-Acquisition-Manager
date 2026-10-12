using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Duplicates;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

// MAM-40 — Remove this copy: the request read, its turn taken, and every check before anything is written — the stored
// rules (CopyRemovalRules), then the platform, the identity the page showed, the disk (check 5's half), another copy still
// there, the companions and every download client (check 6). A refusal keeps no row; the claim and the moves are
// CopyRemovalService.Moves.cs'.
public sealed partial class CopyRemovalService
{
    /// <summary>What a Remove body MVC can't read gets (<c>UnreadableBodyErrorAttribute</c>): what it must be.</summary>
    public const string UnreadableBodyError =
        "The body must be JSON with the group and the file, such as { \"groupKey\": \"<key>\", \"fileId\": \"<id>\" }.";

    public async Task<CopyRemovalResult> RemoveAsync(RemoveCopyRequest? request, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (request is not { GroupKey: { } groupKey, FileId: { } fileId } || string.IsNullOrWhiteSpace(groupKey))
            return CopyRemovalResult.Invalid(RequestIncompleteError);
        // A key of another shape names no group: the same bare not-found as a key no group has, without reading the library.
        if (!DuplicateDecisionRules.IsGroupKey(groupKey))
            return CopyRemovalResult.NotFound;

        // One removal at a time, from the first check to the last rename: the second of two requests over one group's last
        // two copies checks against what the first left (CopyRemovalTurns).
        await turns.WaitAsync(cancellationToken);
        RemovalEnd end;
        try
        {
            var plan = await CheckAsync(groupKey, fileId, request, cancellationToken);
            if (plan.NotFound)
                return CopyRemovalResult.NotFound;
            if (plan.Refusal is { } refusal)
                return CopyRemovalResult.Refused(refusal);

            var actor = await ActorAsync(callerUserId, cancellationToken);
            // Decided: from here on nothing is cancelled, the claim included.
            end = await MoveAsync(plan.Removal!, actor);
        }
        finally
        {
            turns.Release();
        }

        // Outside the turn and any transaction: the library hears the names that left it, never a quarantine path.
        if (end.NoticeDue)
            await TellLibraryAsync(end.CopyRemovalId);
        return end.Refusal is { } stopped ? CopyRemovalResult.Refused(stopped) : await AnswerAsync(end.CopyRemovalId);
    }

    /// <summary>
    /// Every check of the owner's Remove of <paramref name="fileId"/> in <paramref name="groupKey"/>'s group, in order, before
    /// anything is written: the group and the file as the list reads them now (not found otherwise), the stored rules, the
    /// platform, the identity <paramref name="request"/> carries as the page showed it, the disk, another copy still on it,
    /// the companions, and every download client. What would move, or why nothing may. Reads the database, the disk and the
    /// download clients; writes nothing.
    /// </summary>
    private async Task<RemovalCheck> CheckAsync(string groupKey, Guid fileId, RemoveCopyRequest request, CancellationToken cancellationToken)
    {
        if (await duplicates.FindForRemovalAsync(groupKey, cancellationToken) is not { } found
            || found.Files.FirstOrDefault(candidate => candidate.FileId == fileId) is not { } file)
            return RemovalCheck.Missing;

        // Rules 1–8: what the list's verdict says, read again now.
        var judge = await verdicts.ReadAsync(cancellationToken);
        if (judge.ReasonOf(found.Group.Class, found.Group.WorkIds.ToHashSet(), file, found.Files) is { } reason)
            return RemovalCheck.Refused(reason);
        // Before anything is claimed, so a platform that renames nothing keeps no row (Restore's rule).
        if (!quarantine.Capabilities.CanRename)
            return RemovalCheck.Refused(NotSupportedError);
        // The rules let only a file with its identity through.
        var (deviceId, inode, sizeBytes, modifiedUtc) = (file.DeviceId!.Value, file.Inode!.Value, file.SizeBytes!.Value, file.ModifiedUtc!.Value);

        // 9 — the file the owner saw: the identity the page showed is still the one the app read …
        if (request.DeviceId != deviceId || request.Inode != inode || request.SizeBytes != sizeBytes)
            return RemovalCheck.Refused(PageChangedReason);
        // … and the disk still says so, within the root whose quarantine takes it.
        var look = await QuarantineCopyCheck.LookAsync(
            files, file.AppPath!.ToString(), deviceId, inode, sizeBytes, modifiedUtc, judge.Gate, QuarantineCopyCheck.RootsOf(judge.Gate), cancellationToken);
        if (look.Stop is { } stop)
            return RemovalCheck.Refused(StopReasonOf(stop, file.AppPath.ToString(), look.Error));

        // 10 — another copy of the group is on the disk as the app read it: the one that stays.
        if (!await AnotherCopyOnDiskAsync(file, found.Files, judge.Gate, cancellationToken))
            return RemovalCheck.Refused(NoOtherCopyOnDiskReason);

        // 11 — its own companions, as a replacement's mover picks them.
        var appPath = look.AppPath!;
        var (companions, companionReason) = await QuarantineCompanionListing.ListAsync(quarantine, appPath, look.RootText!, cancellationToken);
        if (companionReason is not null)
            return RemovalCheck.Refused(companionReason);

        // 12 — check 6: one read of every download client for every name the move would rename.
        List<ReplacementHoldingTarget> targets =
        [
            new(appPath, look.Fresh!),
            .. companions.Select(companion => new ReplacementHoldingTarget(companion.AppPath, companion.Facts)),
        ];
        var holdings = await QuarantineHoldingCheck.ReadAsync(db, holdingReader, targets, judge.Gate.AllowedRoots, cancellationToken);
        // Anything uncertain is never an all-clear (ADR 0031).
        var uncertain = QuarantineHoldingCheck.UncertainOf(holdings);
        if (uncertain.Count > 0)
            return RemovalCheck.Refused(QuarantineHoldingCheck.UncertainReason(uncertain, QuarantineWording.Removal));
        // A video a torrent lists stays until the owner removes the torrent without its files.
        if (QuarantineHoldingCheck.HoldsOf(holdings, appPath) is { Count: > 0 } held)
            return RemovalCheck.Refused(QuarantineHoldingCheck.HeldReason(held, QuarantineWording.Removal));

        // A companion a torrent lists stays, named on the removal; its video still moves. A second name of a torrent's file
        // moves: the torrent's own name stays, and the row's link count says deleting it frees nothing.
        return RemovalCheck.Planned(new PlannedRemoval(
            found,
            file,
            appPath,
            look.RootText!,
            look.Root!,
            look.Fresh!,
            [
                .. companions.Select(companion => new PlannedRemovalCompanion(
                    companion.AppPath,
                    companion.Facts,
                    QuarantineHoldingCheck.HoldsOf(holdings, companion.AppPath) is { Count: > 0 } holds
                        ? QuarantineHoldingCheck.CompanionHeldReason(holds)
                        : null)),
            ]));
    }

    /// <summary>
    /// Whether a file of the group other than <paramref name="file"/> — one the app read as media, another device and inode
    /// than it (a second name of it is no other copy), inside the allowed roots — is on the disk as the app read it: device
    /// and inode, size and modification time. Each looked at in turn, the first found ending the look.
    /// </summary>
    private async Task<bool> AnotherCopyOnDiskAsync(
        CopyRemovalCandidate file, IReadOnlyList<CopyRemovalCandidate> members, ReplacementGateState gate, CancellationToken cancellationToken)
    {
        foreach (var other in members.Where(other => other.IsAnotherReadableFileThan(file) && other.AppPath is not null))
        {
            if (await files.DescribeAsync(other.AppPath!.ToString(), gate.AllowedRoots, cancellationToken) is { Status: FileInventoryStatus.Found, Value: { } facts }
                && facts.DeviceId == other.DeviceId
                && facts.Inode == other.Inode
                && facts.SizeBytes == other.SizeBytes
                && facts.ModifiedUtc == other.ModifiedUtc)
                return true;
        }

        return false;
    }

    /// <summary>What the checks found: the removal to make, or why none — a group or file not found, or a refusal.</summary>
    private sealed record RemovalCheck(bool NotFound, string? Refusal, PlannedRemoval? Removal)
    {
        public static RemovalCheck Missing { get; } = new(true, null, null);

        public static RemovalCheck Refused(string refusal) => new(false, refusal, null);

        public static RemovalCheck Planned(PlannedRemoval removal) => new(false, null, removal);
    }

    /// <summary>A removal every check let through: the group and the file as read, where the video is, the allowed root
    /// whose quarantine takes it, what the disk said of it now, and its companions.</summary>
    private sealed record PlannedRemoval(
        DuplicateGroupFiles Group,
        CopyRemovalCandidate File,
        NormalizedPath AppPath,
        string RootText,
        NormalizedPath Root,
        FileFacts Fresh,
        IReadOnlyList<PlannedRemovalCompanion> Companions);

    /// <summary>One companion of the video as listed now, and the hold that keeps it where it is (<c>null</c> when none
    /// does).</summary>
    private sealed record PlannedRemovalCompanion(NormalizedPath AppPath, FileFacts Facts, string? HeldBy);
}
