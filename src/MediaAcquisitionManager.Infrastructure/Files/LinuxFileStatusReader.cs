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
/// </remarks>
internal sealed class LinuxFileStatusReader : IFileStatusReader
{
    /// <summary><c>AT_FDCWD</c>: a relative path would be read against the working folder; ours are absolute.</summary>
    private const int CurrentFolderDescriptor = -100;

    /// <summary><c>AT_EMPTY_PATH</c>: statx describes the descriptor itself.</summary>
    private const int DescriptorItself = 0x1000;

    private const int ReadOnly = 0x0;                  // O_RDONLY
    private const int NoControllingTerminal = 0x100;   // O_NOCTTY
    private const int NonBlocking = 0x800;             // O_NONBLOCK
    private const int CloseOnExec = 0x80000;           // O_CLOEXEC
    private const int PathOnly = 0x200000;             // O_PATH

    /// <summary><c>STATX_BASIC_STATS</c>: everything <c>stat</c> gives.</summary>
    private const uint BasicStats = 0x7ff;

    /// <summary><c>STATX_NLINK</c>: the link count is in the answer.</summary>
    private const uint LinkCountReported = 0x4;

    /// <summary><c>STATX_INO</c>: the inode is in the answer. Some network file systems leave it out.</summary>
    private const uint InodeReported = 0x100;

    private const int FileTypeMask = 0xF000;
    private const int RegularFileType = 0x8000;
    private const int FolderType = 0x4000;
    private const int LinkType = 0xA000;

    private const int NotPermitted = 1;       // EPERM
    private const int NoSuchEntry = 2;        // ENOENT
    private const int AccessDenied = 13;      // EACCES
    private const int NotAFolder = 20;        // ENOTDIR
    private const int TooManyLinks = 40;      // ELOOP

    /// <summary>ARM and POWER define <c>O_DIRECTORY</c> and <c>O_NOFOLLOW</c> their own way; amd64 (like riscv64
    /// and s390x) takes them from asm-generic.</summary>
    private static readonly bool HasOwnOpenFlags =
        RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.Ppc64le;

    private static readonly int FolderOnly = HasOwnOpenFlags ? 0x4000 : 0x10000;      // O_DIRECTORY
    private static readonly int DontFollowLink = HasOwnOpenFlags ? 0x8000 : 0x20000;  // O_NOFOLLOW

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
            var readable = OpenAt(CurrentFolderDescriptor, OwnDescriptorPath(pathOnly), ReadOnly | NonBlocking | NoControllingTerminal);
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

        if (Statx((int)folder.DangerousGetHandle(), "", DescriptorItself, BasicStats, out var status) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            folder.Dispose();
            return Refusal<IOpenedFolder>(error, appPath);
        }

        // As for a file: the identity is the pair, and a file system that leaves the inode out gives none, so the
        // walk falls back to the folder's path.
        var hasInode = (status.Mask & InodeReported) != 0;
        return new FileInventoryResult<IOpenedFolder>(
            FileInventoryStatus.Found,
            new OpenedFolder(
                folder,
                hasInode ? DeviceId(status.DeviceMajor, status.DeviceMinor) : null,
                hasInode ? unchecked((long)status.Inode) : null),
            null);
    }

    /// <summary>
    /// The last of <paramref name="names"/> opened with <c>O_PATH</c>, reached from <c>/</c> one name at a time
    /// with <c>O_NOFOLLOW</c>, every name before it as a folder — and the last one too when
    /// <paramref name="asFolder"/>, so a folder swapped for a link or a file is refused rather than opened.
    /// </summary>
    /// <remarks>Every descriptor with <c>O_CLOEXEC</c>: an ffprobe the inspection starts meanwhile inherits only
    /// the file it is meant to read, never a folder the walk holds.</remarks>
    private static FileInventoryResult<SafeFileHandle> OpenPathOnly(string[] names, string appPath, bool asFolder)
    {
        var current = OpenAt(CurrentFolderDescriptor, "/", PathOnly | FolderOnly | CloseOnExec);
        if (current < 0)
            return Refusal<SafeFileHandle>(Marshal.GetLastPInvokeError(), appPath);

        for (var index = 0; index < names.Length; index++)
        {
            var isLast = index == names.Length - 1;
            var next = OpenAt(current, names[index], PathOnly | DontFollowLink | CloseOnExec | (isLast && !asFolder ? 0 : FolderOnly));
            var error = Marshal.GetLastPInvokeError();
            Close(current);
            if (next < 0)
                return Refusal<SafeFileHandle>(error, appPath);
            current = next;
        }

        return new FileInventoryResult<SafeFileHandle>(FileInventoryStatus.Found, new SafeFileHandle(current, ownsHandle: true), null);
    }

    /// <summary>The facts of the regular file <paramref name="file"/> holds, or why it isn't one.</summary>
    private static FileInventoryResult<FileFacts> Describe(SafeFileHandle file, string appPath)
    {
        if (Statx((int)file.DangerousGetHandle(), "", DescriptorItself, BasicStats, out var status) != 0)
            return Refusal<FileFacts>(Marshal.GetLastPInvokeError(), appPath);

        switch (status.Mode & FileTypeMask)
        {
            case RegularFileType:
                break;
            case FolderType:
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.Folder(appPath));
            case LinkType:
                // O_PATH with O_NOFOLLOW opens a link as itself: the file was swapped for one since the check.
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.Unreadable, FileInventoryMessages.ChangedWhileChecking(appPath));
            default:
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.NotARegularFile(appPath));
        }

        // A modification time past 9999 can be set (touch -d), but not held in a DateTime.
        var ticks = (Int128)status.ModifiedSeconds * TimeSpan.TicksPerSecond + status.ModifiedNanoseconds / 100;
        if (ticks < DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks || ticks > DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks)
        {
            return FileSystemFailure.Refused<FileFacts>(
                FileInventoryStatus.Unreadable, FileInventoryMessages.CouldNotRead(appPath, "its modification time is outside the years 1 to 9999"));
        }

        // The identity is the pair: a device without its inode identifies nothing.
        var hasInode = (status.Mask & InodeReported) != 0;
        return new FileInventoryResult<FileFacts>(
            FileInventoryStatus.Found,
            new FileFacts(
                unchecked((long)status.Size),
                DateTime.UnixEpoch.AddTicks((long)ticks),
                hasInode ? DeviceId(status.DeviceMajor, status.DeviceMinor) : null,
                hasInode ? unchecked((long)status.Inode) : null,
                (status.Mask & LinkCountReported) != 0 ? (int)Math.Min(status.LinkCount, int.MaxValue) : null),
            null);
    }

    /// <summary>The path through which a process opens its own descriptor again.</summary>
    private static string OwnDescriptorPath(SafeFileHandle file) =>
        string.Create(CultureInfo.InvariantCulture, $"/proc/self/fd/{(int)file.DangerousGetHandle()}");

    private static FileInventoryResult<T> Refusal<T>(int errno, string appPath) where T : class => errno switch
    {
        NoSuchEntry => FileSystemFailure.Refused<T>(FileInventoryStatus.NotFound, FileInventoryMessages.NotFound(appPath)),
        // A name on the way that is no longer a folder, or is a link now: the path changed since LinkResolver
        // walked it, and the app opens nothing through it.
        NotAFolder or TooManyLinks => FileSystemFailure.Refused<T>(FileInventoryStatus.Unreadable, FileInventoryMessages.ChangedWhileChecking(appPath)),
        AccessDenied or NotPermitted => FileSystemFailure.Refused<T>(FileInventoryStatus.Unreadable, FileInventoryMessages.NotAllowedToRead(appPath)),
        _ => FileSystemFailure.Refused<T>(
            FileInventoryStatus.Unreadable,
            FileInventoryMessages.CouldNotRead(appPath, string.Create(CultureInfo.InvariantCulture, $"{Marshal.GetPInvokeErrorMessage(errno)} (errno {errno})"))),
    };

    /// <summary>
    /// glibc's <c>makedev</c>: the <c>st_dev</c> that <c>stat</c> and <c>stat -c %d</c> report for the same file,
    /// so the number can be checked against them on the host. Stored as a signed 64-bit number, bit for bit.
    /// </summary>
    private static long DeviceId(uint major, uint minor) => unchecked((long)(
        (((ulong)major & 0xfffff000UL) << 32)
        | (((ulong)major & 0xfffUL) << 8)
        | (((ulong)minor & 0xffffff00UL) << 12)
        | ((ulong)minor & 0xffUL)));

    // openat is variadic in C; its mode argument is read only with O_CREAT or O_TMPFILE, which are never passed.
    [DllImport("libc.so.6", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc.so.6", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);

    [DllImport("libc.so.6", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out StatxBuffer buffer);

    /// <summary>
    /// <c>struct statx</c> from <c>linux/stat.h</c>: 256 bytes, the same on every architecture. Only the fields
    /// read are declared; the size keeps room for the rest, which the kernel fills.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    /// <summary>The file opened for reading, which ffprobe inherits and opens again through <c>/proc/self</c>, and
    /// (MAM-19) which the hash reads directly.</summary>
    private sealed class OpenedFile(SafeFileHandle handle, FileFacts facts) : IOpenedFile
    {
        public FileFacts Facts => facts;

        public string ProbePath => OwnDescriptorPath(handle);

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
        public string ListPath => OwnDescriptorPath(handle);

        public long? DeviceId => deviceId;

        public long? Inode => inode;

        public void Dispose() => handle.Dispose();
    }
}
