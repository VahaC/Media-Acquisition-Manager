using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.Win32.SafeHandles;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// MAM-26, ADR 0033 — the Media folders page's hint of whether a rename into the quarantine can work in each ticked
/// folder: the checks a move makes, made ahead of time and without writing anything. MAM-39 — and, where the app's user
/// may not write, whose folder it is and whom the app runs as, so the page can say which PUID and PGID to set.
/// </summary>
internal sealed partial class LocalFileQuarantine
{
    /// <inheritdoc/>
    /// <remarks>
    /// On Linux each folder and the allowed root that holds it are opened as a move opens them, and asked: the same
    /// mount (<c>STATX_MNT_ID</c>); the folder's mount read-write by its own options in the process's mount table
    /// (<see cref="MountTable.MountOptionsOf"/>), not the file system's; and write and search permission for the app's
    /// user, by its effective ids (<c>faccessat(AT_EACCESS)</c>), on the folder and on where the quarantine's folders are
    /// made — <c>.mam-quarantine</c> once it is there, the root while it isn't. Anything it can't read is
    /// <see cref="RenamePlaceProblem.CouldNotCheck"/>, and so is every folder anywhere but Linux, where no rename is made at
    /// all. MAM-39 — a folder refused is described by the descriptor held, never by its path again: its owner by
    /// <c>statx</c>, beside the process's effective ids.
    /// </remarks>
    public async Task<IReadOnlyList<RenamePlace>> DescribeRenamePlacesAsync(
        IReadOnlyList<string> folderAppPaths, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderAppPaths);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
            return [.. folderAppPaths.Select(folder => new RenamePlace(folder, [RenamePlaceProblem.CouldNotCheck]))];

        string? mountInfo = null;
        if (folderAppPaths.Count > 0)
        {
            try
            {
                mountInfo = await File.ReadAllTextAsync(MountInfoPath, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Unknown, never a failure: each folder's mount then can't be said to be read-write.
            }
        }

        var roots = LocalFileInventory.UsableRoots(allowedRoots);
        return [.. folderAppPaths.Select(folder => PlaceOf(folder, roots, allowedRoots, mountInfo))];
    }

    /// <summary>What stands in the way of a rename into the quarantine in <paramref name="folderAppPath"/>, each once,
    /// in the order the problems are declared, and (MAM-39) whose folder the app's user may not write to.</summary>
    private static RenamePlace PlaceOf(
        string folderAppPath, IReadOnlyList<NormalizedPath> roots, IReadOnlyList<string> allowedRoots, string? mountInfo)
    {
        if (!NormalizedPath.TryParse(folderAppPath, out var folder, out _)
            || roots.FirstOrDefault(folder.IsUnder) is not { } root
            || LocalFileInventory.Locate(folderAppPath, allowedRoots).Value is not { } resolvedFolder
            || LinkResolver.Resolve(root, root.ToString()).Value is not { } resolvedRoot)
        {
            return new RenamePlace(folderAppPath, [RenamePlaceProblem.CouldNotCheck]);
        }

        if (LinuxCalls.OpenPathOnly(resolvedFolder.Segments, asFolder: true, out var folderHandle) != 0)
            return new RenamePlace(folderAppPath, [RenamePlaceProblem.CouldNotCheck]);

        using (folderHandle)
        {
            if (LinuxCalls.OpenPathOnly(resolvedRoot.Segments, asFolder: true, out var rootHandle) != 0)
                return new RenamePlace(folderAppPath, [RenamePlaceProblem.CouldNotCheck]);

            using (rootHandle)
            {
                var problems = new SortedSet<RenamePlaceProblem>();
                var folderMount = MountIdOf(folderHandle!);
                var rootMount = MountIdOf(rootHandle!);
                if (folderMount is null || rootMount is null)
                    problems.Add(RenamePlaceProblem.CouldNotCheck);
                else if (folderMount != rootMount)
                    problems.Add(RenamePlaceProblem.OtherMount);

                var options = mountInfo is not null && folderMount is { } mount ? MountTable.MountOptionsOf(mountInfo, mount) : null;
                if (options is null)
                    problems.Add(RenamePlaceProblem.CouldNotCheck);
                else if (options.Contains("ro", StringComparer.Ordinal))
                    problems.Add(RenamePlaceProblem.ReadOnly);

                var folderRefusal = WriteRefusalOf(folderHandle!, folderAppPath);
                if (folderRefusal is not null)
                    problems.Add(folderRefusal.Problem);
                var quarantineRefusal = QuarantineWriteRefusalOf(rootHandle!, root);
                if (quarantineRefusal is not null)
                    problems.Add(quarantineRefusal.Problem);

                // MAM-39 — the ticked folder's own owner before the quarantine's: its files are the ones a move takes, so
                // its owner is whom the app should run as. The quarantine's side only when the folder's own permissions
                // don't bar the app's user.
                var writeAccess = folderRefusal?.Problem == RenamePlaceProblem.NotWritable
                    ? folderRefusal.WriteAccess
                    : quarantineRefusal?.WriteAccess;
                return new RenamePlace(folderAppPath, [.. problems], writeAccess);
            }
        }
    }

    /// <summary>
    /// Whether the app's user may make names where a move makes them on the quarantine's side: in <c>.mam-quarantine</c>
    /// once it is there — a move then makes nothing in the root, as <c>mkdirat</c> answers <c>EEXIST</c> before it asks
    /// for write permission —, in the root while it isn't. Anything else at that name (a file, a link) the move refuses,
    /// so this can't tell either. MAM-39 — named as the page knows it: the allowed root's app path, with
    /// <c>.mam-quarantine</c> when that is the folder asked.
    /// </summary>
    private static WriteRefusal? QuarantineWriteRefusalOf(SafeFileHandle root, NormalizedPath rootAppPath)
    {
        var quarantineFolder = LinuxCalls.OpenAt(
            LinuxCalls.Number(root),
            QuarantineFolder.Name,
            LinuxCalls.PathOnly | LinuxCalls.FolderOnly | LinuxCalls.DontFollowLink | LinuxCalls.CloseOnExec);
        if (quarantineFolder < 0)
        {
            return Marshal.GetLastPInvokeError() == LinuxCalls.NoSuchEntry
                ? WriteRefusalOf(root, rootAppPath.ToString())
                : new WriteRefusal(RenamePlaceProblem.CouldNotCheck);
        }

        using var held = new SafeFileHandle(quarantineFolder, ownsHandle: true);
        return WriteRefusalOf(held, QuarantineFolder.Of(rootAppPath).ToString());
    }

    /// <summary>
    /// Whether the app's user may make and remove names in the folder held, asked of its <c>/proc/self/fd</c> path, which
    /// leads to that very folder: <c>null</c> when it may. MAM-39 — when it may not, with whose folder it is.
    /// </summary>
    private static WriteRefusal? WriteRefusalOf(SafeFileHandle folder, string appPath)
    {
        if (LinuxCalls.AccessAt(
                LinuxCalls.CurrentFolderDescriptor,
                LinuxCalls.OwnDescriptorPath(folder),
                LinuxCalls.MayWrite | LinuxCalls.MaySearch,
                LinuxCalls.EffectiveIds) == 0)
        {
            return null;
        }

        // Read before anything else is called: the statx that describes the folder would leave its own errno.
        var error = Marshal.GetLastPInvokeError();
        return error switch
        {
            LinuxCalls.AccessDenied or LinuxCalls.NotPermitted => new WriteRefusal(RenamePlaceProblem.NotWritable, WriteAccessOf(folder, appPath)),
            // A read-only mount answers a question about writing with EROFS, whatever the mount table said.
            LinuxCalls.ReadOnlyFileSystem => new WriteRefusal(RenamePlaceProblem.ReadOnly),
            _ => new WriteRefusal(RenamePlaceProblem.CouldNotCheck),
        };
    }

    /// <summary>
    /// MAM-39 — whose the folder held is, asked of the descriptor itself, so it is the very folder just refused whatever
    /// its path leads to by now; beside the process's effective ids, the ones <c>faccessat(AT_EACCESS)</c> judged by.
    /// <c>null</c> when the file system doesn't give the owner: the page then says less, and the hint is no failure.
    /// </summary>
    private static RenamePlaceWriteAccess? WriteAccessOf(SafeFileHandle folder, string appPath) =>
        LinuxCalls.Statx(LinuxCalls.Number(folder), "", LinuxCalls.DescriptorItself, LinuxCalls.OwnerReported, out var status) == 0
        && (status.Mask & LinuxCalls.OwnerReported) == LinuxCalls.OwnerReported
            ? new RenamePlaceWriteAccess(appPath, status.UserId, status.GroupId, LinuxCalls.EffectiveUserId(), LinuxCalls.EffectiveGroupId())
            : null;

    /// <summary>What a write check refused: the problem, and for <see cref="RenamePlaceProblem.NotWritable"/> whose folder
    /// it is when that could be read.</summary>
    private sealed record WriteRefusal(RenamePlaceProblem Problem, RenamePlaceWriteAccess? WriteAccess = null);
}
