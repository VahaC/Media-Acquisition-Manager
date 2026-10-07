namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// The app's own read-only look at files on its mounts (MAM-9, Phase B) — local, so not an
/// integration connection. MAM-9 reads a file's identity and its streams, MAM-18 walks the allowed roots
/// for their media files, MAM-19 hashes a file's bytes; Phase C adds the quarantine.
/// </summary>
/// <remarks>
/// <para>
/// Every member takes the allowed roots it may read inside: the installation owner's setting, read by
/// the caller once per inspection, so a check never depends on a value cached somewhere else. A path,
/// or a link on the way to it, outside those roots is refused, and with no roots nothing is read
/// (specification §4).
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (not found, outside the
/// allowed roots, unreadable); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence.
/// </para>
/// </remarks>
public interface IFileInventory
{
    /// <summary>What this platform can report about a file.</summary>
    FileInventoryCapabilities Capabilities { get; }

    /// <summary>
    /// What the file system says about the file at <paramref name="appPath"/>, links inside
    /// <paramref name="allowedRoots"/> followed. Refuses a path outside them and never follows a link
    /// out of them.
    /// </summary>
    /// <param name="appPath">The file's absolute path inside the app.</param>
    /// <param name="allowedRoots">The folders it may read inside, in canonical form; none refuses every
    /// path.</param>
    /// <param name="cancellationToken">Cancels the look.</param>
    Task<FileInventoryResult<FileFacts>> DescribeAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the streams of the media file at <paramref name="appPath"/> with the media inspector,
    /// after the checks of <see cref="DescribeAsync"/>. The file facts that come with the streams were
    /// read before and after the inspector ran and agreed; a file that changed meanwhile is
    /// <see cref="FileInventoryStatus.ChangedWhileReading"/>, never facts of one version stored under the
    /// key of the other.
    /// </summary>
    /// <param name="appPath">The file's absolute path inside the app.</param>
    /// <param name="allowedRoots">The folders it may read inside, in canonical form.</param>
    /// <param name="cancellationToken">Cancels the read, stopping the inspector.</param>
    Task<FileInventoryResult<MediaInspection>> InspectMediaAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every byte of the file at <paramref name="appPath"/>, after the checks of
    /// <see cref="DescribeAsync"/>, and gives their SHA-256 (MAM-19, ADR 0022). The file facts that come
    /// with the hash were read before and after the read and agreed, and the read ended at the size they
    /// give; a file that changed meanwhile is <see cref="FileInventoryStatus.ChangedWhileReading"/>, never
    /// a hash of one version kept under the key of the other.
    /// </summary>
    /// <param name="appPath">The file's absolute path inside the app.</param>
    /// <param name="allowedRoots">The folders it may read inside, in canonical form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<FileInventoryResult<FileContentHash>> HashAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every one of <paramref name="allowedRoots"/>, in their order, and yields each media file name
    /// it finds (<see cref="Core.Files.MediaFileNames"/>) and each folder, link or name it didn't go into or
    /// through, with why (MAM-18, ADR 0021). Links inside the roots are followed and one leading out is
    /// never; a loop ends; nothing is opened for reading. A walk that reaches its limit yields
    /// <see cref="FileListingStatus.LimitReached"/> last.
    /// </summary>
    /// <param name="allowedRoots">The folders to walk, in canonical form; none yields nothing.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    IAsyncEnumerable<FileListingEntry> ListMediaFilesAsync(
        IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-20, ADR 0028 — the type of file system each of <paramref name="deviceIds"/> holds, as the system's mount table
    /// names it: one answer per device asked, in the order asked. What decides whether removing a byte-identical copy
    /// frees its room — a file system that can share blocks between files (reflinks, snapshots) may give none back.
    /// </summary>
    /// <remarks>
    /// Reads the system's mount table, no file of the library, so it takes no allowed roots. An unknown type is
    /// <c>null</c> — no mount table on this platform, none it may read, no line for the device, or lines that
    /// disagree — never a guess, and never an exception.
    /// </remarks>
    /// <param name="deviceIds">The devices, as <see cref="FileFacts.DeviceId"/> gives them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<DeviceFileSystem>> DescribeFileSystemsAsync(
        IReadOnlyCollection<long> deviceIds, CancellationToken cancellationToken = default);
}
