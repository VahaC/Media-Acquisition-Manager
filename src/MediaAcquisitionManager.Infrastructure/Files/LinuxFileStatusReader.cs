using System.Globalization;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using Microsoft.Win32.SafeHandles;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The Linux reader (MAM-9): opens the file one folder name at a time without following a link, takes its
/// facts from <c>statx(2)</c> on what was opened — the device, the inode and the link count that .NET's own
/// file APIs don't give — and hands ffprobe that open file.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0009 — <see cref="LinkResolver"/> gives a path with no link left in it, but a path checked and then
/// opened again by its text leaves a moment in which anyone who may write inside a root can swap a folder or
/// the file for a link leading out. So the path is opened here from <c>/</c>, name by name, each with
/// <c>O_NOFOLLOW</c>: a link met on the way is one swapped in since the check, and nothing is opened through
/// it. The last name is opened with <c>O_PATH</c>, which reads nothing, so a pipe or a device is looked at
/// and never opened — it can't hang the run or rewind a tape; only a regular file is then opened for reading.
/// ffprobe reads that open file as <c>/proc/self/fd/&lt;n&gt;</c>, a descriptor it inherits: the very file
/// checked here, whatever its path leads to by then.
/// </para>
/// <para>
/// MAM-18, ADR 0021 — the walk of the allowed roots opens each folder the same way, every name a folder and
/// none followed, and lists the descriptor through <c>/proc/self/fd/&lt;n&gt;</c>: a folder swapped for a link
/// after its parent was listed is refused, never listed through. The folder's device and inode tell the walk a
/// loop no path shows, such as a bind mount of a folder inside itself.
/// </para>
/// <para>
/// statx rather than <c>stat</c>: glibc's <c>struct stat</c> differs between architectures (amd64 and arm64 lay
/// it out differently), while <c>struct statx</c> is one fixed 256-byte layout everywhere, so one declaration
/// serves both images. <c>O_DIRECTORY</c> and <c>O_NOFOLLOW</c> do differ, so they are picked by architecture.
/// </para>
/// <para>
/// glibc 2.28 or later, Linux 4.11 or later and <c>/proc</c> mounted, which every supported base image and
/// Docker have; <c>libc.so.6</c> by name, because the bare <c>libc.so</c> is a linker script that can't be
/// loaded.
/// </para>
/// <para>
/// MAM-26 — the system calls, the <c>statx</c> layout and their constants live in <see cref="LinuxCalls"/>, shared
/// with the quarantine (<see cref="LocalFileQuarantine"/>); this reader makes only the calls that read.
/// </para>
/// </remarks>
internal sealed class LinuxFileStatusReader : IFileStatusReader
{
    /// <inheritdoc/>
    public bool ReportsFileIdentity => true;

    /// <inheritdoc/>
    public FileInventoryResult<IOpenedFile> Open(string systemPath, string appPath)
    {
        var names = systemPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0)
            return FileSystemFailure.Refused<IOpenedFile>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.Folder(appPath));

        var located = OpenPathOnly(names, appPath, asFolder: false);
        if (located.Value is not { } pathOnly)
            return FileSystemFailure.Relay<SafeFileHandle, IOpenedFile>(located);

        using (pathOnly)
        {
            var described = Describe(pathOnly, appPath);
            if (described.Value is not { } facts)
                return FileSystemFailure.Relay<FileFacts, IOpenedFile>(described);

            // Opened for reading through /proc: the same file the O_PATH descriptor holds, whatever its path
            // leads to by now, and the proof the app may read it. Without O_CLOEXEC, so ffprobe inherits it;
            // non-blocking, though only a regular file gets here.
            var readable = LinuxCalls.OpenAt(
                LinuxCalls.CurrentFolderDescriptor,
                LinuxCalls.OwnDescriptorPath(pathOnly),
                LinuxCalls.ReadOnly | LinuxCalls.NonBlocking | LinuxCalls.NoControllingTerminal);
            if (readable < 0)
                return Refusal<IOpenedFile>(Marshal.GetLastPInvokeError(), appPath);

            return new FileInventoryResult<IOpenedFile>(
                FileInventoryStatus.Found, new OpenedFile(new SafeFileHandle(readable, ownsHandle: true), facts), null);
        }
    }

    /// <inheritdoc/>
    public FileInventoryResult<IOpenedFolder> OpenFolder(string systemPath, string appPath)
    {
        var located = OpenPathOnly(systemPath.Split('/', StringSplitOptions.RemoveEmptyEntries), appPath, asFolder: true);
        if (located.Value is not { } folder)
            return FileSystemFailure.Relay<SafeFileHandle, IOpenedFolder>(located);

        if (LinuxCalls.Statx(LinuxCalls.Number(folder), "", LinuxCalls.DescriptorItself, LinuxCalls.BasicStats, out var status) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            folder.Dispose();
            return Refusal<IOpenedFolder>(error, appPath);
        }

        // As for a file: the identity is the pair, and a file system that leaves the inode out gives none, so the
        // walk falls back to the folder's path.
        var hasInode = (status.Mask & LinuxCalls.InodeReported) != 0;
        return new FileInventoryResult<IOpenedFolder>(
            FileInventoryStatus.Found,
            new OpenedFolder(
                folder,
                hasInode ? LinuxCalls.DeviceId(status.DeviceMajor, status.DeviceMinor) : null,
                hasInode ? unchecked((long)status.Inode) : null),
            null);
    }

    /// <summary>
    /// The last of <paramref name="names"/> opened with <c>O_PATH</c>, from <c>/</c> one name at a time with
    /// <c>O_NOFOLLOW</c> (<see cref="LinuxCalls.OpenPathOnly"/>), or why it couldn't be.
    /// </summary>
    private static FileInventoryResult<SafeFileHandle> OpenPathOnly(string[] names, string appPath, bool asFolder) =>
        LinuxCalls.OpenPathOnly(names, asFolder, out var handle) is var error and not 0
            ? Refusal<SafeFileHandle>(error, appPath)
            : new FileInventoryResult<SafeFileHandle>(FileInventoryStatus.Found, handle, null);

    /// <summary>The facts of the regular file <paramref name="file"/> holds, or why it isn't one.</summary>
    private static FileInventoryResult<FileFacts> Describe(SafeFileHandle file, string appPath)
    {
        if (LinuxCalls.Statx(LinuxCalls.Number(file), "", LinuxCalls.DescriptorItself, LinuxCalls.BasicStats, out var status) != 0)
            return Refusal<FileFacts>(Marshal.GetLastPInvokeError(), appPath);

        switch (status.Mode & LinuxCalls.FileTypeMask)
        {
            case LinuxCalls.RegularFileType:
                break;
            case LinuxCalls.FolderType:
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.Folder(appPath));
            case LinuxCalls.LinkType:
                // O_PATH with O_NOFOLLOW opens a link as itself: the file was swapped for one since the check.
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.Unreadable, FileInventoryMessages.ChangedWhileChecking(appPath));
            default:
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.NotARegularFile(appPath));
        }

        // A modification time past 9999 can be set (touch -d), but not held in a DateTime.
        if (LinuxCalls.FactsOf(status) is not { } facts)
        {
            return FileSystemFailure.Refused<FileFacts>(
                FileInventoryStatus.Unreadable, FileInventoryMessages.TimeOutOfRange(appPath));
        }

        return new FileInventoryResult<FileFacts>(FileInventoryStatus.Found, facts, null);
    }

    private static FileInventoryResult<T> Refusal<T>(int errno, string appPath) where T : class => errno switch
    {
        LinuxCalls.NoSuchEntry => FileSystemFailure.Refused<T>(FileInventoryStatus.NotFound, FileInventoryMessages.NotFound(appPath)),
        // A name on the way that is no longer a folder, or is a link now: the path changed since LinkResolver
        // walked it, and the app opens nothing through it.
        LinuxCalls.NotAFolder or LinuxCalls.TooManyLinks => FileSystemFailure.Refused<T>(FileInventoryStatus.Unreadable, FileInventoryMessages.ChangedWhileChecking(appPath)),
        LinuxCalls.AccessDenied or LinuxCalls.NotPermitted => FileSystemFailure.Refused<T>(FileInventoryStatus.Unreadable, FileInventoryMessages.NotAllowedToRead(appPath)),
        _ => FileSystemFailure.Refused<T>(
            FileInventoryStatus.Unreadable,
            FileInventoryMessages.CouldNotRead(appPath, string.Create(CultureInfo.InvariantCulture, $"{Marshal.GetPInvokeErrorMessage(errno)} (errno {errno})"))),
    };

    /// <summary>The file opened for reading, which ffprobe inherits and opens again through <c>/proc/self</c>, and
    /// (MAM-19) which the hash reads directly.</summary>
    private sealed class OpenedFile(SafeFileHandle handle, FileFacts facts) : IOpenedFile
    {
        public FileFacts Facts => facts;

        public string ProbePath => LinuxCalls.OwnDescriptorPath(handle);

        public FileInventoryResult<FileFacts> ReadFactsAgain(string appPath) => Describe(handle, appPath);

        // pread on the descriptor itself: a read at an offset moves no shared position, and RandomAccess never
        // takes the handle over, so it is still open for the facts asked again after the read.
        public ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken) =>
            RandomAccess.ReadAsync(handle, buffer, offset, cancellationToken);

        public void Dispose() => handle.Dispose();
    }

    /// <summary>
    /// The folder held with <c>O_PATH</c>, listed through <c>/proc/self</c>: opening that path again opens this
    /// very folder, and the read permission is checked then, so a folder the app may not list fails its listing.
    /// </summary>
    private sealed class OpenedFolder(SafeFileHandle handle, long? deviceId, long? inode) : IOpenedFolder
    {
        public string ListPath => LinuxCalls.OwnDescriptorPath(handle);

        public long? DeviceId => deviceId;

        public long? Inode => inode;

        public void Dispose() => handle.Dispose();
    }
}
