using System.Globalization;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using Microsoft.Win32.SafeHandles;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// MAM-26 — the Linux system calls the reader (<see cref="LinuxFileStatusReader"/>) and the quarantine
/// (<see cref="LocalFileQuarantine"/>) share, with their constants: one place for the flags that differ between
/// architectures, so the two can never disagree about what <c>O_NOFOLLOW</c> is.
/// </summary>
/// <remarks>
/// <para>
/// Declared here, called there: the reader and <see cref="LocalFileInventory"/> use only the calls that read
/// (<c>openat</c> with <c>O_PATH</c> or <c>O_RDONLY</c>, <c>statx</c>, <c>close</c>), so the inventory stays
/// read-only by construction (ADR 0030); the calls that write — <c>renameat2</c>, <c>mkdirat</c>, <c>unlinkat</c>,
/// <c>fsync</c> — are the quarantine's alone.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c>, which needs unsafe code this project doesn't allow; glibc 2.28
/// or later (<c>renameat2</c>'s wrapper, <c>statx</c>), Linux 4.11 or later and <c>/proc</c> mounted, which every
/// supported base image and Docker have; <c>libc.so.6</c> by name, because the bare <c>libc.so</c> is a linker
/// script that can't be loaded.
/// </para>
/// </remarks>
internal static class LinuxCalls
{
    /// <summary><c>AT_FDCWD</c>: a relative path would be read against the working folder; ours are absolute.</summary>
    public const int CurrentFolderDescriptor = -100;

    /// <summary><c>AT_EMPTY_PATH</c>: statx describes the descriptor itself. The same number as
    /// <see cref="MountIdReported"/>, which is a mask bit, not a flag.</summary>
    public const int DescriptorItself = 0x1000;

    /// <summary><c>AT_SYMLINK_NOFOLLOW</c>: statx describes a link at the name as the link, never where it leads.</summary>
    public const int LinkItself = 0x100;

    /// <summary><c>AT_EACCESS</c>: faccessat asks for the process's effective ids, which the kernel decides a rename
    /// by, not its real ones.</summary>
    public const int EffectiveIds = 0x200;

    public const int ReadOnly = 0x0;                  // O_RDONLY
    public const int NoControllingTerminal = 0x100;   // O_NOCTTY
    public const int NonBlocking = 0x800;             // O_NONBLOCK
    public const int CloseOnExec = 0x80000;           // O_CLOEXEC
    public const int PathOnly = 0x200000;             // O_PATH

    /// <summary><c>STATX_BASIC_STATS</c>: everything <c>stat</c> gives.</summary>
    public const uint BasicStats = 0x7ff;

    /// <summary><c>STATX_NLINK</c>: the link count is in the answer.</summary>
    public const uint LinkCountReported = 0x4;

    /// <summary><c>STATX_INO</c>: the inode is in the answer. Some network file systems leave it out.</summary>
    public const uint InodeReported = 0x100;

    /// <summary>
    /// <c>STATX_MNT_ID</c>: the mount is in the answer — the id the mount table's first field gives, which tells two
    /// bind mounts of one file system apart where the device can't (ADR 0030). Linux 5.8 or later; an older kernel
    /// leaves the bit out of the answer's mask. Not <c>STATX_MNT_ID_UNIQUE</c>, which the mount table doesn't list.
    /// </summary>
    public const uint MountIdReported = 0x1000;

    public const int FileTypeMask = 0xF000;
    public const int RegularFileType = 0x8000;
    public const int FolderType = 0x4000;
    public const int LinkType = 0xA000;

    /// <summary><c>RENAME_NOREPLACE</c>: renameat2 fails with <c>EEXIST</c> rather than replace a name already there.</summary>
    public const uint NoReplace = 1;

    /// <summary>0755, before the umask: the mode qBittorrent's own folders have on the reference installation, so a
    /// quarantine folder reads like everything around it (ADR 0033).</summary>
    public const uint NewFolderMode = 0x1ED;

    public const int MaySearch = 1;   // X_OK
    public const int MayWrite = 2;    // W_OK

    // asm-generic numbers, the same on amd64 and arm64.
    public const int NotPermitted = 1;          // EPERM
    public const int NoSuchEntry = 2;           // ENOENT
    public const int AccessDenied = 13;         // EACCES
    public const int NameExists = 17;           // EEXIST
    public const int OtherDevice = 18;          // EXDEV
    public const int NotAFolder = 20;           // ENOTDIR
    public const int InvalidArgument = 22;      // EINVAL
    public const int ReadOnlyFileSystem = 30;   // EROFS
    public const int TooManyLinks = 40;         // ELOOP
    public const int ValueTooLarge = 75;        // EOVERFLOW

    /// <summary>ARM and POWER define <c>O_DIRECTORY</c> and <c>O_NOFOLLOW</c> their own way; amd64 (like riscv64
    /// and s390x) takes them from asm-generic.</summary>
    private static readonly bool HasOwnOpenFlags =
        RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.Ppc64le;

    public static readonly int FolderOnly = HasOwnOpenFlags ? 0x4000 : 0x10000;      // O_DIRECTORY
    public static readonly int DontFollowLink = HasOwnOpenFlags ? 0x8000 : 0x20000;  // O_NOFOLLOW

    /// <summary>
    /// The last of <paramref name="names"/> opened with <c>O_PATH</c>, reached from <c>/</c> one name at a time
    /// with <c>O_NOFOLLOW</c>, every name before it as a folder — and the last one too when
    /// <paramref name="asFolder"/>, so a folder swapped for a link or a file is refused rather than opened.
    /// </summary>
    /// <remarks>Every descriptor with <c>O_CLOEXEC</c>: an ffprobe the inspection starts meanwhile inherits only
    /// the file it is meant to read, never a folder the walk holds.</remarks>
    /// <returns>0 and the descriptor, or the <c>errno</c> of the step that failed and none.</returns>
    public static int OpenPathOnly(IReadOnlyList<string> names, bool asFolder, out SafeFileHandle? handle)
    {
        handle = null;
        var current = OpenAt(CurrentFolderDescriptor, "/", PathOnly | FolderOnly | CloseOnExec);
        if (current < 0)
            return Marshal.GetLastPInvokeError();

        for (var index = 0; index < names.Count; index++)
        {
            var isLast = index == names.Count - 1;
            var next = OpenAt(current, names[index], PathOnly | DontFollowLink | CloseOnExec | (isLast && !asFolder ? 0 : FolderOnly));
            var error = Marshal.GetLastPInvokeError();
            Close(current);
            if (next < 0)
                return error;
            current = next;
        }

        handle = new SafeFileHandle(current, ownsHandle: true);
        return 0;
    }

    /// <summary>
    /// What <paramref name="status"/> says about a regular file — size, modification time, device, inode and link
    /// count — or <c>null</c> when its modification time is past what a <see cref="DateTime"/> holds. The time is
    /// cut to the tick (100 ns) the same way every time: the quarantine compares it with what a look stored.
    /// </summary>
    public static FileFacts? FactsOf(in StatxBuffer status)
    {
        // A modification time past 9999 can be set (touch -d), but not held in a DateTime.
        var ticks = (Int128)status.ModifiedSeconds * TimeSpan.TicksPerSecond + status.ModifiedNanoseconds / 100;
        if (ticks < DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks || ticks > DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks)
            return null;

        // The identity is the pair: a device without its inode identifies nothing.
        var hasInode = (status.Mask & InodeReported) != 0;
        return new FileFacts(
            unchecked((long)status.Size),
            DateTime.UnixEpoch.AddTicks((long)ticks),
            hasInode ? DeviceId(status.DeviceMajor, status.DeviceMinor) : null,
            hasInode ? unchecked((long)status.Inode) : null,
            (status.Mask & LinkCountReported) != 0 ? (int)Math.Min(status.LinkCount, int.MaxValue) : null);
    }

    /// <summary>
    /// glibc's <c>makedev</c>: the <c>st_dev</c> that <c>stat</c> and <c>stat -c %d</c> report for the same file,
    /// so the number can be checked against them on the host. Stored as a signed 64-bit number, bit for bit.
    /// </summary>
    public static long DeviceId(uint major, uint minor) => unchecked((long)(
        (((ulong)major & 0xfffff000UL) << 32)
        | (((ulong)major & 0xfffUL) << 8)
        | (((ulong)minor & 0xffffff00UL) << 12)
        | ((ulong)minor & 0xffUL)));

    /// <summary>The path through which a process opens its own descriptor again.</summary>
    public static string OwnDescriptorPath(SafeFileHandle file) =>
        string.Create(CultureInfo.InvariantCulture, $"/proc/self/fd/{(int)file.DangerousGetHandle()}");

    /// <summary>The descriptor's number, for a call that takes one.</summary>
    public static int Number(SafeFileHandle handle) => (int)handle.DangerousGetHandle();

    // openat is variadic in C; its mode argument is read only with O_CREAT or O_TMPFILE, which are never passed.
    [DllImport("libc.so.6", EntryPoint = "openat", SetLastError = true)]
    public static extern int OpenAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc.so.6", EntryPoint = "close", SetLastError = true)]
    public static extern int Close(int descriptor);

    [DllImport("libc.so.6", EntryPoint = "statx", SetLastError = true)]
    public static extern int Statx(
        int directoryDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out StatxBuffer buffer);

    /// <summary>
    /// glibc's wrapper, not a raw <c>syscall</c>: the system call's number differs between architectures (316 on
    /// amd64, 276 on arm64), the wrapper's name doesn't.
    /// </summary>
    [DllImport("libc.so.6", EntryPoint = "renameat2", SetLastError = true)]
    public static extern int RenameAt2(
        int oldDirectoryDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldName,
        int newDirectoryDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newName,
        uint flags);

    [DllImport("libc.so.6", EntryPoint = "mkdirat", SetLastError = true)]
    public static extern int MakeFolderAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);

    [DllImport("libc.so.6", EntryPoint = "unlinkat", SetLastError = true)]
    public static extern int UnlinkAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    [DllImport("libc.so.6", EntryPoint = "fsync", SetLastError = true)]
    public static extern int Sync(int descriptor);

    [DllImport("libc.so.6", EntryPoint = "faccessat", SetLastError = true)]
    public static extern int AccessAt(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode, int flags);
}
