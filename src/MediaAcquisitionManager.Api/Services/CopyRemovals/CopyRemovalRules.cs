using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — whether one copy of a duplicate group may be removed by what the app has stored (Vasyl, 2026-10-12, under ADR
/// 0030, 0031 and 0033): the one place both the Duplicates list's verdict and the Remove request judge it, so the page
/// never offers what the request then refuses, nor the other way round. Pure: it judges what the caller read.
/// </summary>
/// <remarks>
/// <para>
/// The rules, in the order they are checked, the first that fails giving its sentence: the group is one of exact copies or
/// of one work's different files; replacing and removing copies is switched on; the file is inside an allowed root, in a
/// ticked library folder; the app's last look read it as media; it is no clip of a disc folder; every work holding it is
/// one of the group's; no open replacement nor open removal holds it; and another file of the group — another device and
/// inode, not a second name of this one — is one the app read as media. The Remove request then checks the disk and the download clients (<see cref="CopyRemovalService"/>).
/// </para>
/// <para>
/// The sentences name no path: the list is every signed-in user's, and a member sees why the button is off too. MAM-47
/// (ADR 0042) — each a message of the <c>duplicates.removal.rule</c> keys, which the list renders in the reader's language.
/// </para>
/// </remarks>
public static class CopyRemovalRules
{
    /// <summary>A copy of a group of another class: one file's names free nothing, and a possible match is no copy yet.</summary>
    public static readonly Message NotRemovableClassReason = Message.Of("duplicates.removal.rule.not-removable-class");

    /// <summary>The master switch is off (ADR 0033): the one replacement uses, named as the Media folders page names it.</summary>
    public static readonly Message SwitchedOffReason = Message.Of("duplicates.removal.rule.switched-off");

    private static readonly Message OutsideMediaFoldersReason = Message.Of("duplicates.removal.rule.outside-media-folders");

    private static readonly Message NotTickedReason = Message.Of("duplicates.removal.rule.not-ticked");

    private static readonly Message NotInspectedReason = Message.Of("duplicates.removal.rule.not-inspected");

    private static readonly Message NotReadableReason = Message.Of("duplicates.removal.rule.not-readable");

    private static readonly Message DiscClipReason = Message.Of("duplicates.removal.rule.disc-clip");

    private static readonly Message OtherWorkReason = Message.Of("duplicates.removal.rule.other-work");

    private static readonly Message HeldByReplacementReason = Message.Of("duplicates.removal.rule.held-by-replacement");

    private static readonly Message HeldByRemovalReason = Message.Of("duplicates.removal.rule.held-by-removal");

    private static readonly Message LastReadableCopyReason = Message.Of("duplicates.removal.rule.last-readable-copy");

    /// <summary>
    /// Why <paramref name="file"/>, one of <paramref name="members"/> — the files of a group of
    /// <paramref name="groupClass"/> about <paramref name="groupWorkIds"/> —, can't be removed now by what is stored, behind
    /// <paramref name="gate"/> and with <paramref name="holds"/>; <c>null</c> when it may.
    /// </summary>
    public static Message? ReasonOf(
        DuplicateClass groupClass,
        IReadOnlyCollection<Guid> groupWorkIds,
        CopyRemovalCandidate file,
        IReadOnlyList<CopyRemovalCandidate> members,
        ReplacementGateState gate,
        CopyRemovalHolds holds)
    {
        ArgumentNullException.ThrowIfNull(groupWorkIds);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(holds);

        // 1. Only copies: one file's two names free nothing, and a possible match may be no copy at all.
        if (groupClass is not (DuplicateClass.SameWork or DuplicateClass.ByteIdentical))
            return NotRemovableClassReason;
        // 2. ADR 0033 — the same switch as replacement, read fresh by the caller.
        if (!gate.IsEnabled)
            return SwitchedOffReason;
        // 3. Inside an allowed root and in a ticked folder: the quarantine is the allowed root's, and the tick the owner's
        // word on the folder (ADR 0033).
        if (file.AppPath is not { } appPath
            || !gate.AllowedRoots.Any(root => NormalizedPath.TryParse(root, out var parsed, out _) && appPath.IsUnder(parsed)))
            return OutsideMediaFoldersReason;
        if (!gate.IsInTickedFolder(appPath))
            return NotTickedReason;
        // 4. The identity the move expects is the one the last look read; a file it couldn't read as media may be the
        // only one that differs.
        if (file.InspectionStatus is null)
            return NotInspectedReason;
        if (!file.IsReadable)
            return NotReadableReason;
        // 5. A disc folder's clips are one film together.
        if (DiscFolders.HoldsClip(appPath))
            return DiscClipReason;
        // 6. A file holding a work outside the group — a two-episode file in an episode's group — takes that work along.
        if (file.HolderWorkIds.Any(workId => !groupWorkIds.Contains(workId)))
            return OtherWorkReason;
        // 7. One write per file: an open replacement would find it gone, and a second removal has nothing to move.
        if (holds.ReplacementHolds(appPath, file.DeviceId, file.Inode))
            return HeldByReplacementReason;
        if (holds.RemovalHolds(appPath, file.DeviceId, file.Inode))
            return HeldByRemovalReason;
        // 8. Never the last copy the app knows plays — counting files, not names: a second name of this file goes nowhere
        // else when this one goes.
        if (!members.Any(other => other.IsAnotherReadableFileThan(file)))
            return LastReadableCopyReason;
        return null;
    }
}
