using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

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
/// The sentences name no path: the list is every signed-in user's, and a member sees why the button is off too.
/// </para>
/// </remarks>
public static class CopyRemovalRules
{
    /// <summary>A copy of a group of another class: one file's names free nothing, and a possible match is no copy yet.</summary>
    public const string NotRemovableClassReason =
        "Only a copy in “Exact copies” or “Same film, different files” can be removed.";

    /// <summary>The master switch is off (ADR 0033): the one replacement uses, named as the Media folders page names it.</summary>
    public const string SwitchedOffReason =
        "Removing copies is switched off. Turn on “Replace copies after verification” under Media folders.";

    private const string OutsideMediaFoldersReason = "This file isn't inside the Media folders, so the app can't move it.";

    private const string NotTickedReason = "This file's library folder isn't ticked for replacement under Media folders.";

    private const string NotInspectedReason = "The app hasn't inspected this file yet. Press Inspect files on the Library page first.";

    private const string NotReadableReason = "The app's last look couldn't read this file as media, so it can't tell what would go.";

    private const string DiscClipReason = "This file is part of a disc folder, which can't be removed one file at a time.";

    private const string OtherWorkReason = "This file also holds a film or an episode outside this group, which would go with it.";

    private const string HeldByReplacementReason = "A replacement on the Jobs page holds this file.";

    private const string HeldByRemovalReason = "A removal under Removed copies holds this file.";

    private const string LastReadableCopyReason =
        "No other copy in this group is one the app has read as media, so this one stays: it may be the only one that plays.";

    /// <summary>
    /// Why <paramref name="file"/>, one of <paramref name="members"/> — the files of a group of
    /// <paramref name="groupClass"/> about <paramref name="groupWorkIds"/> —, can't be removed now by what is stored, behind
    /// <paramref name="gate"/> and with <paramref name="holds"/>; <c>null</c> when it may.
    /// </summary>
    public static string? ReasonOf(
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
