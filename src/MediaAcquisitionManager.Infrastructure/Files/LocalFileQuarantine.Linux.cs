using System.Globalization;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.Win32.SafeHandles;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// MAM-26 — the quarantine's writes on Linux, through folder descriptors (ADR 0030): the folder a name is in is opened
/// with <c>O_PATH</c> from <c>/</c>, one name at a time and no link followed, and held; the name is read, renamed or
/// deleted relative to that descriptor, never by its path again.
/// </summary>
internal sealed partial class LocalFileQuarantine
{
    /// <summary><see cref="Retrace"/>'s answer when the path leads to a folder, but not the one held.</summary>
    private const int ElsewhereNow = -1;

    /// <summary>
    /// The move: the source's folder and the root held, the name read and matched, both on one mount, the quarantine's
    /// folders made one level at a time under the root, the leaf on that mount too, everything looked at again, and
    /// one <c>renameat2(RENAME_NOREPLACE)</c>.
    /// </summary>
    private FileQuarantineResult<FileFacts> MoveOnLinux(
        QuarantineEnds ends, FileFacts expected, NormalizedPath sourceFolder, CancellationToken cancellationToken)
    {
        var appPath = ends.AppPath.ToString();
        var quarantinePath = ends.QuarantinePath.ToString();
        var name = ends.AppPath.Segments[^1];
        var relative = ends.QuarantinePath.SegmentsAfter(ends.Root);

        var source = Hold(sourceFolder.Segments, FolderOf(ends.AppPath).ToString(), appPath);
        if (source.Value is not { } sourceHeld)
            return Relay<HeldFolder, FileFacts>(source);

        using var from = sourceHeld;
        var rootOpened = Hold(ends.ResolvedRoot.Segments, ends.Root.ToString(), ends.Root.ToString());
        if (rootOpened.Value is not { } rootHeld)
            return Relay<HeldFolder, FileFacts>(rootOpened);

        using var root = rootHeld;
        if (Matching(from, name, appPath, expected) is { } changed)
            return changed;

        // Before anything is made: a name on another mount than its root's quarantine can't be renamed into it, only
        // copied, which the app never does.
        if (NotOnOneMount(from, root, appPath) is { } apart)
            return apart;

        var leaf = QuarantineFolderOf(root, ends.Root, relative.SkipLast(1), create: true, quarantinePath);
        if (leaf.Value is not { } leafHeld)
            return Relay<HeldFolder, FileFacts>(leaf);

        using var into = leafHeld;
        // Something mounted inside the root, at the quarantine or below it, puts the quarantine on another mount as well.
        if (NotOnOneMount(into, root, quarantinePath) is { } mountedInside)
            return mountedInside;

        cancellationToken.ThrowIfCancellationRequested();
        BeforeWrite?.Invoke(appPath);
        if (Retrace(from) != 0)
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.ChangedWhileChecking(appPath));
        if (Retrace(into) != 0)
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.ChangedWhileChecking(quarantinePath));
        if (Matching(from, name, appPath, expected) is { } changedSince)
            return changedSince;

        BeforeRenameCall?.Invoke(appPath);
        if (LinuxCalls.RenameAt2(LinuxCalls.Number(from.Handle), name, LinuxCalls.Number(into.Handle), relative[^1], LinuxCalls.NoReplace) != 0)
            return Refusal<FileFacts>(Marshal.GetLastPInvokeError(), appPath, quarantinePath);

        return Settled(from, into, relative[^1], expected, appPath, quarantinePath);
    }

    /// <summary>
    /// The restore: the quarantine's folder reached from the root with no link followed, the name read and matched, the
    /// folder it came from held — never made —, both on one mount, everything looked at again, and one
    /// <c>renameat2(RENAME_NOREPLACE)</c> back.
    /// </summary>
    private FileQuarantineResult<FileFacts> RestoreOnLinux(
        QuarantineEnds ends, FileFacts expected, NormalizedPath destinationFolder, CancellationToken cancellationToken)
    {
        var appPath = ends.AppPath.ToString();
        var quarantinePath = ends.QuarantinePath.ToString();
        var folderAppPath = FolderOf(ends.AppPath).ToString();
        var relative = ends.QuarantinePath.SegmentsAfter(ends.Root);
        var quarantinedName = relative[^1];

        var rootOpened = Hold(ends.ResolvedRoot.Segments, ends.Root.ToString(), ends.Root.ToString());
        if (rootOpened.Value is not { } rootHeld)
            return Relay<HeldFolder, FileFacts>(rootOpened);

        using var root = rootHeld;
        var leaf = QuarantineFolderOf(root, ends.Root, relative.SkipLast(1), create: false, quarantinePath);
        if (leaf.Value is not { } leafHeld)
            return Relay<HeldFolder, FileFacts>(leaf);

        using var from = leafHeld;
        if (Matching(from, quarantinedName, quarantinePath, expected) is { } changed)
            return changed;

        var destination = Hold(destinationFolder.Segments, folderAppPath, folderAppPath);
        if (destination.Value is not { } destinationHeld)
        {
            return destination.Status == FileQuarantineStatus.NotFound
                ? Refused<FileFacts>(FileQuarantineStatus.FolderGone, FileQuarantineMessages.FolderGone(folderAppPath))
                : Relay<HeldFolder, FileFacts>(destination);
        }

        using var into = destinationHeld;
        if (NotOnOneMount(from, into, appPath) is { } apart)
            return apart;

        cancellationToken.ThrowIfCancellationRequested();
        BeforeWrite?.Invoke(quarantinePath);
        if (Retrace(from) != 0)
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.ChangedWhileChecking(quarantinePath));
        switch (Retrace(into))
        {
            case 0:
                break;
            case LinuxCalls.NoSuchEntry:
                return Refused<FileFacts>(FileQuarantineStatus.FolderGone, FileQuarantineMessages.FolderGone(folderAppPath));
            default:
                return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.ChangedWhileChecking(appPath));
        }

        if (Matching(from, quarantinedName, quarantinePath, expected) is { } changedSince)
            return changedSince;

        BeforeRenameCall?.Invoke(quarantinePath);
        if (LinuxCalls.RenameAt2(LinuxCalls.Number(from.Handle), quarantinedName, LinuxCalls.Number(into.Handle), ends.AppPath.Segments[^1], LinuxCalls.NoReplace) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            // ENOENT names either end: the quarantined name gone, or the folder held removed since it was looked at.
            if (error == LinuxCalls.NoSuchEntry && Read(from, quarantinedName, quarantinePath).Status == FileQuarantineStatus.Done)
                return Refused<FileFacts>(FileQuarantineStatus.FolderGone, FileQuarantineMessages.FolderGone(folderAppPath));
            return Refusal<FileFacts>(error, quarantinePath, appPath);
        }

        return Settled(from, into, ends.AppPath.Segments[^1], expected, quarantinePath, appPath);
    }

    /// <summary>
    /// The deletion: the quarantine's folder reached from the root with no link followed, the name read and matched,
    /// looked at again, and one <c>unlinkat</c>. There is no "unlink if this inode", so the second look is as close as
    /// the check gets to the call.
    /// </summary>
    private FileQuarantineResult<FileFacts> DeleteOnLinux(
        NormalizedPath quarantined, NormalizedPath root, NormalizedPath resolvedRoot, FileFacts expected, CancellationToken cancellationToken)
    {
        var quarantinePath = quarantined.ToString();
        var relative = quarantined.SegmentsAfter(root);

        var rootOpened = Hold(resolvedRoot.Segments, root.ToString(), root.ToString());
        if (rootOpened.Value is not { } rootHeld)
            return Relay<HeldFolder, FileFacts>(rootOpened);

        using var rootFolder = rootHeld;
        var leaf = QuarantineFolderOf(rootFolder, root, relative.SkipLast(1), create: false, quarantinePath);
        if (leaf.Value is not { } leafHeld)
            return Relay<HeldFolder, FileFacts>(leaf);

        using var folder = leafHeld;
        if (Matching(folder, relative[^1], quarantinePath, expected) is { } changed)
            return changed;

        cancellationToken.ThrowIfCancellationRequested();
        BeforeWrite?.Invoke(quarantinePath);
        if (Retrace(folder) != 0)
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.ChangedWhileChecking(quarantinePath));

        var last = Read(folder, relative[^1], quarantinePath);
        if (last.Value is not { } facts)
            return last;
        if (!IsSameFile(expected, facts))
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.Changed(quarantinePath));

        if (LinuxCalls.UnlinkAt(LinuxCalls.Number(folder.Handle), relative[^1], 0) != 0)
            return Refusal<FileFacts>(Marshal.GetLastPInvokeError(), quarantinePath);

        Flush(folder);
        return Done(facts);
    }

    /// <summary>
    /// The regular files of the folder held, listed through its descriptor as the walk lists one
    /// (<see cref="FolderWalker"/>) and each read with <c>statx</c> relative to it, the name never followed: a link,
    /// a folder, a pipe or a device is left out, and so is a name that went since the listing.
    /// </summary>
    private static FileQuarantineResult<IReadOnlyList<FolderFile>> ListOnLinux(NormalizedPath folder, NormalizedPath resolvedFolder)
    {
        var folderAppPath = folder.ToString();
        var opened = Hold(resolvedFolder.Segments, folderAppPath, folderAppPath);
        if (opened.Value is not { } folderHeld)
            return Relay<HeldFolder, IReadOnlyList<FolderFile>>(opened);

        using var held = folderHeld;
        var listPath = LinuxCalls.OwnDescriptorPath(held.Handle);
        var names = new List<string>();
        try
        {
            foreach (var entry in new DirectoryInfo(listPath).EnumerateFileSystemInfos("*", FolderWalker.ListingOptions))
                names.Add(entry.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ListingFailed<IReadOnlyList<FolderFile>>(exception, folderAppPath, listPath);
        }

        names.Sort(string.CompareOrdinal);
        var files = new List<FolderFile>();
        foreach (var name in names)
        {
            // A name that isn't UTF-8 reads back as another name than the disk's, which no call could rename: like a
            // link, it never moves.
            if (!FolderWalker.IsUsableName(name))
                continue;

            var appPath = folder.Append([name]).ToString();
            if (LinuxCalls.Statx(LinuxCalls.Number(held.Handle), name, LinuxCalls.LinkItself, LinuxCalls.BasicStats, out var status) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == LinuxCalls.NoSuchEntry)
                    continue;

                return error is LinuxCalls.AccessDenied or LinuxCalls.NotPermitted
                    ? Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.NotAllowed, FileInventoryMessages.NotAllowedToRead(appPath))
                    : Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.Failed, FileQuarantineMessages.CouldNotRead(appPath, ErrorText(error)));
            }

            if ((status.Mode & LinuxCalls.FileTypeMask) != LinuxCalls.RegularFileType)
                continue;
            if (LinuxCalls.FactsOf(status) is not { } facts)
                return Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.Failed, FileQuarantineMessages.CouldNotRead(appPath, OutOfRangeTime));

            files.Add(new FolderFile(name, appPath, facts));
        }

        return Done<IReadOnlyList<FolderFile>>(files);
    }

    /// <summary>Every name of the folder held, listed through its descriptor as <see cref="ListOnLinux"/> lists it, none
    /// read or followed.</summary>
    private static FileQuarantineResult<IReadOnlyList<string>> NamesOnLinux(NormalizedPath folder, NormalizedPath resolvedFolder)
    {
        var folderAppPath = folder.ToString();
        var opened = Hold(resolvedFolder.Segments, folderAppPath, folderAppPath);
        if (opened.Value is not { } folderHeld)
        {
            return opened.Status == FileQuarantineStatus.NotFound
                ? Refused<IReadOnlyList<string>>(FileQuarantineStatus.NotFound, FileQuarantineMessages.FolderGone(folderAppPath))
                : Relay<HeldFolder, IReadOnlyList<string>>(opened);
        }

        using var held = folderHeld;
        return NamesIn(folderAppPath, LinuxCalls.OwnDescriptorPath(held.Handle));
    }

    // ── the descriptors ────────────────────────────────────────────────────────────────────────

    /// <summary>The folder at the link-free <paramref name="names"/>, opened and held; a refusal speaks of
    /// <paramref name="about"/>.</summary>
    /// <param name="names">The folder's names from <c>/</c>, no link among them.</param>
    /// <param name="folderAppPath">The folder as the owner knows it.</param>
    /// <param name="about">The path a refusal's sentence names: the file the caller wants, mostly.</param>
    private static FileQuarantineResult<HeldFolder> Hold(IReadOnlyList<string> names, string folderAppPath, string about)
    {
        var error = LinuxCalls.OpenPathOnly(names, asFolder: true, out var handle);
        return error == 0 ? Done(new HeldFolder(handle!, [.. names], folderAppPath)) : Refusal<HeldFolder>(error, about);
    }

    /// <summary>
    /// The folder <paramref name="folders"/> name below the root held — <c>.mam-quarantine</c>, the job's folder and the
    /// copy's own folders —, reached one at a time with no link followed, and with <paramref name="create"/> each made
    /// first where it isn't there. A link or a file planted at one of those names is refused, never followed: the
    /// app makes folders in its own quarantine and nowhere else.
    /// </summary>
    /// <param name="root">The allowed root, held.</param>
    /// <param name="typedRoot">The root as the owner typed it, for the sentences.</param>
    /// <param name="folders">The quarantine path's folders below the root.</param>
    /// <param name="create">Whether to make the folders that aren't there: for a move only.</param>
    /// <param name="quarantinePath">The name in the quarantine, which a folder not there means isn't there.</param>
    private static FileQuarantineResult<HeldFolder> QuarantineFolderOf(
        HeldFolder root, NormalizedPath typedRoot, IEnumerable<string> folders, bool create, string quarantinePath)
    {
        var names = new List<string>(root.Names);
        var appPath = typedRoot;
        SafeFileHandle? current = null;
        foreach (var name in folders)
        {
            appPath = appPath.Append([name]);
            var parent = LinuxCalls.Number(current ?? root.Handle);
            // EEXIST is the folder an earlier move made, or anything else at that name, which the open then refuses
            // unless it is a folder.
            if (create
                && LinuxCalls.MakeFolderAt(parent, name, LinuxCalls.NewFolderMode) != 0
                && Marshal.GetLastPInvokeError() is var made and not LinuxCalls.NameExists)
            {
                current?.Dispose();
                return Refusal<HeldFolder>(made, appPath.ToString());
            }

            var next = LinuxCalls.OpenAt(parent, name, LinuxCalls.PathOnly | LinuxCalls.FolderOnly | LinuxCalls.DontFollowLink | LinuxCalls.CloseOnExec);
            var error = Marshal.GetLastPInvokeError();
            current?.Dispose();
            if (next < 0)
                return Refusal<HeldFolder>(error, error == LinuxCalls.NoSuchEntry ? quarantinePath : appPath.ToString());

            current = new SafeFileHandle(next, ownsHandle: true);
            names.Add(name);
        }

        return Done(new HeldFolder(current!, names, appPath.ToString()));
    }

    /// <summary>
    /// Whether the folder's path still leads to the folder held: 0 when it does, the walk's <c>errno</c> when it no
    /// longer leads anywhere, <see cref="ElsewhereNow"/> when it leads to another folder. Asked right before the
    /// call, so the path the caller records is where the name really was.
    /// </summary>
    private static int Retrace(HeldFolder folder)
    {
        var error = LinuxCalls.OpenPathOnly(folder.Names, asFolder: true, out var again);
        if (error != 0)
            return error;

        using (again)
            return IdentityOf(again!) is { } now && IdentityOf(folder.Handle) == now ? 0 : ElsewhereNow;
    }

    private static (long Device, ulong Inode)? IdentityOf(SafeFileHandle folder) =>
        LinuxCalls.Statx(LinuxCalls.Number(folder), "", LinuxCalls.DescriptorItself, LinuxCalls.BasicStats, out var status) == 0
        && (status.Mask & LinuxCalls.InodeReported) != 0
            ? (LinuxCalls.DeviceId(status.DeviceMajor, status.DeviceMinor), status.Inode)
            : null;

    /// <summary>The mount the folder held is on (<c>stx_mnt_id</c>), or <c>null</c> when the kernel doesn't say.</summary>
    private static ulong? MountIdOf(SafeFileHandle folder) =>
        LinuxCalls.Statx(LinuxCalls.Number(folder), "", LinuxCalls.DescriptorItself, LinuxCalls.MountIdReported, out var status) == 0
        && (status.Mask & LinuxCalls.MountIdReported) != 0
            ? status.MountId
            : null;

    /// <summary>
    /// The refusal when the two folders held aren't on one mount, or <c>null</c> when they are. Not the device: two bind
    /// mounts of one file system share it, and a rename between them is <c>EXDEV</c> all the same (ADR 0030). A
    /// kernel that doesn't report the mount fails closed.
    /// </summary>
    private static FileQuarantineResult<FileFacts>? NotOnOneMount(HeldFolder first, HeldFolder second, string appPath)
    {
        if (MountIdOf(first.Handle) is not { } firstMount || MountIdOf(second.Handle) is not { } secondMount)
            return Refused<FileFacts>(FileQuarantineStatus.NotSupported, FileQuarantineMessages.NoMountId(appPath));

        return firstMount == secondMount
            ? null
            : Refused<FileFacts>(FileQuarantineStatus.OtherMount, FileQuarantineMessages.OtherMount(first.AppPath, second.AppPath));
    }

    /// <summary>
    /// fsync of a folder held, through a descriptor opened for reading: fsync on an <c>O_PATH</c> one is <c>EBADF</c>.
    /// </summary>
    /// <remarks>
    /// Its failure changes no answer: the rename or the deletion happened and is what the caller must record, and a
    /// power cut before the disk had it is what a restart's look by device and inode finds out (ADR 0030).
    /// </remarks>
    private static void Flush(HeldFolder folder)
    {
        var readable = LinuxCalls.OpenAt(
            LinuxCalls.Number(folder.Handle), ".", LinuxCalls.ReadOnly | LinuxCalls.FolderOnly | LinuxCalls.CloseOnExec);
        if (readable < 0)
            return;

        LinuxCalls.Sync(readable);
        LinuxCalls.Close(readable);
    }

    // ── the name ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the file system says about <paramref name="name"/> in the folder held, the name itself never followed: a
    /// regular file with its identity, or why it isn't one.
    /// </summary>
    private static FileQuarantineResult<FileFacts> Read(HeldFolder folder, string name, string appPath)
    {
        if (LinuxCalls.Statx(LinuxCalls.Number(folder.Handle), name, LinuxCalls.LinkItself, LinuxCalls.BasicStats, out var status) != 0)
            return Refusal<FileFacts>(Marshal.GetLastPInvokeError(), appPath);

        // A link is a name of its own, which never moves: renaming it would leave the file it leads to where it is.
        if ((status.Mode & LinuxCalls.FileTypeMask) != LinuxCalls.RegularFileType)
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.NotARegularFile(appPath));
        if (LinuxCalls.FactsOf(status) is not { } facts)
            return Refused<FileFacts>(FileQuarantineStatus.Failed, FileQuarantineMessages.CouldNotRead(appPath, OutOfRangeTime));
        if (facts.Inode is null)
            return Refused<FileFacts>(FileQuarantineStatus.NotSupported, FileQuarantineMessages.NoIdentity(appPath));

        return Done(facts);
    }

    /// <summary>The refusal when <paramref name="name"/> isn't the file <paramref name="expected"/> describes now, or
    /// <c>null</c> when it is.</summary>
    private static FileQuarantineResult<FileFacts>? Matching(HeldFolder folder, string name, string appPath, FileFacts expected)
    {
        var read = Read(folder, name, appPath);
        if (read.Value is not { } facts)
            return read;

        return IsSameFile(expected, facts) ? null : Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.Changed(appPath));
    }

    /// <summary>
    /// After a rename: both folders flushed, and the new name must be the file renamed — the same device and inode —,
    /// or something was put at the old name between the last look and the call, and that is what moved:
    /// <see cref="FileQuarantineStatus.Swapped"/>, its own status rather than a refusal's, as a rename did happen.
    /// </summary>
    private static FileQuarantineResult<FileFacts> Settled(
        HeldFolder from, HeldFolder into, string newName, FileFacts expected, string fromAppPath, string toAppPath)
    {
        // Flushed whatever sits at the new name: the rename happened either way, and where the names are must outlive
        // a power cut.
        Flush(from);
        Flush(into);
        var after = Read(into, newName, toAppPath);
        return after.Value is { } facts && facts.DeviceId == expected.DeviceId && facts.Inode == expected.Inode
            ? after
            : Refused<FileFacts>(FileQuarantineStatus.Swapped, FileQuarantineMessages.Swapped(fromAppPath, toAppPath));
    }

    /// <summary>
    /// The same file as the caller read: size, modification time to the tick, device and inode. The link count is left
    /// out — a second name made meanwhile changes nothing about which file this is —, as for a look
    /// (<see cref="LocalFileInventory"/>).
    /// </summary>
    private static bool IsSameFile(FileFacts expected, FileFacts actual) =>
        expected.SizeBytes == actual.SizeBytes
        && expected.ModifiedUtc == actual.ModifiedUtc
        && expected.DeviceId == actual.DeviceId
        && expected.Inode == actual.Inode;

    // ── refusals ───────────────────────────────────────────────────────────────────────────────

    private const string OutOfRangeTime = "its modification time is outside the years 1 to 9999";

    /// <summary>
    /// What a failed call's <c>errno</c> means for the name at <paramref name="appPath"/>, on its way to
    /// <paramref name="otherAppPath"/> when it is a rename (ADR 0030's list: each leaves the name where it was).
    /// </summary>
    private static FileQuarantineResult<T> Refusal<T>(int errno, string appPath, string? otherAppPath = null) where T : class => errno switch
    {
        LinuxCalls.NameExists => Refused<T>(FileQuarantineStatus.NameTaken, FileQuarantineMessages.NameTaken(otherAppPath ?? appPath)),
        LinuxCalls.OtherDevice => Refused<T>(FileQuarantineStatus.OtherMount, FileQuarantineMessages.OtherMount(appPath, otherAppPath ?? appPath)),
        // Where the file system lacks RENAME_NOREPLACE (NFS, OpenZFS before 2.2, some FUSE): never retried without it.
        LinuxCalls.InvalidArgument => Refused<T>(FileQuarantineStatus.NotSupported, FileQuarantineMessages.RenameUnsupported(appPath)),
        LinuxCalls.ReadOnlyFileSystem => Refused<T>(FileQuarantineStatus.ReadOnly, FileQuarantineMessages.ReadOnly(appPath)),
        // EOVERFLOW: an owner the container can't map, whom no one in it may rename for.
        LinuxCalls.AccessDenied or LinuxCalls.NotPermitted or LinuxCalls.ValueTooLarge =>
            Refused<T>(FileQuarantineStatus.NotAllowed, FileQuarantineMessages.NotAllowed(appPath)),
        LinuxCalls.NoSuchEntry => Refused<T>(FileQuarantineStatus.NotFound, FileQuarantineMessages.NotFound(appPath)),
        // A name on the way that is no longer a folder, or is a link now: nothing is done through it.
        LinuxCalls.TooManyLinks or LinuxCalls.NotAFolder => Refused<T>(FileQuarantineStatus.Changed, FileQuarantineMessages.ChangedWhileChecking(appPath)),
        _ => Refused<T>(FileQuarantineStatus.Failed, FileQuarantineMessages.CouldNotChange(appPath, ErrorText(errno))),
    };

    private static string ErrorText(int errno) =>
        string.Create(CultureInfo.InvariantCulture, $"{Marshal.GetPInvokeErrorMessage(errno)} (errno {errno})");

    /// <summary>A folder opened with <c>O_PATH</c> and held while its names are read and changed.</summary>
    /// <param name="handle">The descriptor.</param>
    /// <param name="names">Its link-free names from <c>/</c>, to walk again right before the call.</param>
    /// <param name="appPath">The folder as the owner knows it, for the sentences.</param>
    private sealed class HeldFolder(SafeFileHandle handle, IReadOnlyList<string> names, string appPath) : IDisposable
    {
        public SafeFileHandle Handle => handle;

        public IReadOnlyList<string> Names => names;

        public string AppPath => appPath;

        public void Dispose() => handle.Dispose();
    }
}
