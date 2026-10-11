using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.Win32.SafeHandles;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// MAM-26, ADR 0033 — the Media folders page's hint of whether a rename into the quarantine can work in each ticked
/// folder: the checks a move makes, made ahead of time and without writing anything.
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
    /// all.
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
        return [.. folderAppPaths.Select(folder => new RenamePlace(folder, ProblemsOf(folder, roots, allowedRoots, mountInfo)))];
    }

    /// <summary>What stands in the way of a rename into the quarantine in <paramref name="folderAppPath"/>, each once,
    /// in the order the problems are declared.</summary>
    private static IReadOnlyList<RenamePlaceProblem> ProblemsOf(
        string folderAppPath, IReadOnlyList<NormalizedPath> roots, IReadOnlyList<string> allowedRoots, string? mountInfo)
    {
        if (!NormalizedPath.TryParse(folderAppPath, out var folder, out _)
            || roots.FirstOrDefault(folder.IsUnder) is not { } root
            || LocalFileInventory.Locate(folderAppPath, allowedRoots).Value is not { } resolvedFolder
            || LinkResolver.Resolve(root, root.ToString()).Value is not { } resolvedRoot)
        {
            return [RenamePlaceProblem.CouldNotCheck];
        }

        if (LinuxCalls.OpenPathOnly(resolvedFolder.Segments, asFolder: true, out var folderHandle) != 0)
            return [RenamePlaceProblem.CouldNotCheck];

        using (folderHandle)
        {
            if (LinuxCalls.OpenPathOnly(resolvedRoot.Segments, asFolder: true, out var rootHandle) != 0)
                return [RenamePlaceProblem.CouldNotCheck];

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

                if (WriteProblemOf(folderHandle!) is { } folderProblem)
                    problems.Add(folderProblem);
                if (QuarantineWriteProblemOf(rootHandle!) is { } quarantineProblem)
                    problems.Add(quarantineProblem);

                return [.. problems];
            }
        }
    }

    /// <summary>
    /// Whether the app's user may make names where a move makes them on the quarantine's side: in <c>.mam-quarantine</c>
    /// once it is there — a move then makes nothing in the root, as <c>mkdirat</c> answers <c>EEXIST</c> before it asks
    /// for write permission —, in the root while it isn't. Anything else at that name (a file, a link) the move refuses,
    /// so this can't tell either.
    /// </summary>
    private static RenamePlaceProblem? QuarantineWriteProblemOf(SafeFileHandle root)
    {
        var quarantineFolder = LinuxCalls.OpenAt(
            LinuxCalls.Number(root),
            QuarantineFolder.Name,
            LinuxCalls.PathOnly | LinuxCalls.FolderOnly | LinuxCalls.DontFollowLink | LinuxCalls.CloseOnExec);
        if (quarantineFolder < 0)
            return Marshal.GetLastPInvokeError() == LinuxCalls.NoSuchEntry ? WriteProblemOf(root) : RenamePlaceProblem.CouldNotCheck;

        using var held = new SafeFileHandle(quarantineFolder, ownsHandle: true);
        return WriteProblemOf(held);
    }

    /// <summary>
    /// Whether the app's user may make and remove names in the folder held, asked of its <c>/proc/self/fd</c> path, which
    /// leads to that very folder: <c>null</c> when it may.
    /// </summary>
    private static RenamePlaceProblem? WriteProblemOf(SafeFileHandle folder)
    {
        if (LinuxCalls.AccessAt(
                LinuxCalls.CurrentFolderDescriptor,
                LinuxCalls.OwnDescriptorPath(folder),
                LinuxCalls.MayWrite | LinuxCalls.MaySearch,
                LinuxCalls.EffectiveIds) == 0)
        {
            return null;
        }

        return Marshal.GetLastPInvokeError() switch
        {
            LinuxCalls.AccessDenied or LinuxCalls.NotPermitted => RenamePlaceProblem.NotWritable,
            // A read-only mount answers a question about writing with EROFS, whatever the mount table said.
            LinuxCalls.ReadOnlyFileSystem => RenamePlaceProblem.ReadOnly,
            _ => RenamePlaceProblem.CouldNotCheck,
        };
    }
}
