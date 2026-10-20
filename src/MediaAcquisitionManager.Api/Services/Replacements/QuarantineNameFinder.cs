using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — where a name a restart or a crash cut in the middle of a write is now (ADR 0030), looked up by device and inode
/// where it was and where it was going: a replacement's (MAM-26's recovery) or a removal's alike. It reads the disk only,
/// and decides nothing on a look that failed.
/// </summary>
internal static class QuarantineNameFinder
{
    /// <summary>
    /// Where <paramref name="name"/>, cut in the middle of a write, is now, by device and inode, inside
    /// <paramref name="roots"/>: a rename into the quarantine done or not, a rename back done or not, a deletion done or not
    /// — or neither, which loses it, with why. <c>null</c> when that can't be told: a look that didn't answer found or not
    /// found, or a name found nowhere while neither the folder it was in nor the one it was going to is there — a mount
    /// missing, an empty mount point —, where its absence proves nothing.
    /// </summary>
    public static async Task<(ReplacementNameState State, Message? Problem)?> FindAsync(
        IFileInventory files, QuarantinedNameEntity name, IReadOnlyList<string> roots, CancellationToken cancellationToken)
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
                return await PlaceIsThereAsync(files, name, roots, cancellationToken)
                    ? (ReplacementNameState.Lost, LostWhileMovingProblem(name.AppPath))
                    : null;
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
                return await PlaceIsThereAsync(files, name, roots, cancellationToken)
                    ? (ReplacementNameState.Lost, LostWhileRestoringProblem(name.AppPath))
                    : null;
            }

            default:
                if (inQuarantine.Status == FileInventoryStatus.NotFound)
                    return await PlaceIsThereAsync(files, name, roots, cancellationToken) ? (ReplacementNameState.Deleted, null) : null;
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
    private static async Task<bool> PlaceIsThereAsync(
        IFileInventory files, QuarantinedNameEntity name, IReadOnlyList<string> roots, CancellationToken cancellationToken)
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
    private static bool IsThere(FileInventoryResult<FileFacts> described, QuarantinedNameEntity name) =>
        described is { Status: FileInventoryStatus.Found, Value: { } facts } && facts.DeviceId == name.DeviceId && facts.Inode == name.Inode;

    // MAM-47 (ADR 0042) — why a name is lost, as a message the Api renders; the file's own name, the template's quotes.

    private static Message LostWhileMovingProblem(string path) =>
        Message.Of("replacements.quarantine.lost.moving", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));

    private static Message LostWhileRestoringProblem(string path) =>
        Message.Of("replacements.quarantine.lost.restoring", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));

    private static Message LostWhileDeletingProblem(string path) =>
        Message.Of("replacements.quarantine.lost.deleting", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));
}
