using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using Microsoft.Win32.SafeHandles;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The Windows reader (MAM-9): <c>GetFileInformationByHandle</c> on a handle opened for reading, which gives
/// NTFS's equivalents of a device and an inode — the volume serial number and the file index — and the link
/// count. The app is deployed on Linux; this makes a development box behave the same, cache keys included.
/// </summary>
/// <remarks>
/// ADR 0009 — the handle stays open while ffprobe reads, shared for reading and writing but not for
/// deleting: as long as it is open, NTFS lets no one rename or delete the file or a folder above it, so the
/// path ffprobe opens still leads to the file opened here. The open itself follows a junction swapped in
/// since the check, which only the Linux reader, where the app runs, refuses.
/// </remarks>
internal sealed class WindowsFileStatusReader : IFileStatusReader
{
    /// <inheritdoc/>
    public bool ReportsFileIdentity => true;

    /// <inheritdoc/>
    public FileInventoryResult<IOpenedFile> Open(string systemPath, string appPath)
    {
        SafeFileHandle? handle = null;
        try
        {
            // A folder can't be opened for reading without FILE_FLAG_BACKUP_SEMANTICS, and the refusal would
            // read as "not allowed"; ask first, so a folder is called a folder.
            if (File.GetAttributes(systemPath).HasFlag(FileAttributes.Directory))
                return FileSystemFailure.Refused<IOpenedFile>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.Folder(appPath));

            // The open is the readability check, and the facts come from the handle it gives, so they describe
            // the file that was opened. A download client may still write on; the facts read again show it.
            handle = File.OpenHandle(systemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var described = Describe(handle, appPath);
            if (described.Value is not { } facts)
                return FileSystemFailure.Relay<FileFacts, IOpenedFile>(described);

            var opened = new OpenedFile(handle, facts, systemPath);
            handle = null;
            return new FileInventoryResult<IOpenedFile>(FileInventoryStatus.Found, opened, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileSystemFailure.For<IOpenedFile>(exception, appPath);
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// MAM-18, ADR 0021 — listed again by its path, and with no identity: a junction swapped in at the path since
    /// the checks is followed, which only the Linux reader, where the app runs, refuses; and the walk tells a loop
    /// by the folder's resolved path instead.
    /// </remarks>
    public FileInventoryResult<IOpenedFolder> OpenFolder(string systemPath, string appPath) =>
        new(FileInventoryStatus.Found, new FolderListedByPath(systemPath), null);

    private static FileInventoryResult<FileFacts> Describe(SafeFileHandle handle, string appPath)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            return FileSystemFailure.Refused<FileFacts>(
                FileInventoryStatus.Unreadable, FileInventoryMessages.CouldNotRead(appPath, Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())));
        }

        var modified = ((long)information.LastWriteTimeHigh << 32) | information.LastWriteTimeLow;
        // A FILETIME runs to the year 30828, a DateTime to 9999.
        if (modified > DateTime.MaxValue.ToFileTimeUtc())
        {
            return FileSystemFailure.Refused<FileFacts>(
                FileInventoryStatus.Unreadable, FileInventoryMessages.TimePastYear9999(appPath));
        }

        return new FileInventoryResult<FileFacts>(
            FileInventoryStatus.Found,
            new FileFacts(
                ((long)information.FileSizeHigh << 32) | information.FileSizeLow,
                DateTime.FromFileTimeUtc(modified),
                information.VolumeSerialNumber,
                unchecked(((long)information.FileIndexHigh << 32) | information.FileIndexLow),
                (int)Math.Min(information.NumberOfLinks, int.MaxValue)),
            null);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    /// <summary><c>BY_HANDLE_FILE_INFORMATION</c>, the FILETIMEs split into their two halves.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>The file held open while ffprobe opens it again by the path that can't change meanwhile, and
    /// (MAM-19) which the hash reads directly.</summary>
    private sealed class OpenedFile(SafeFileHandle handle, FileFacts facts, string systemPath) : IOpenedFile
    {
        public FileFacts Facts => facts;

        public string ProbePath => systemPath;

        public FileInventoryResult<FileFacts> ReadFactsAgain(string appPath) => Describe(handle, appPath);

        // RandomAccess never takes the handle over, so it is still open for the facts asked again after the read.
        public ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken) =>
            RandomAccess.ReadAsync(handle, buffer, offset, cancellationToken);

        public void Dispose() => handle.Dispose();
    }
}
