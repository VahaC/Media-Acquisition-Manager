namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// The app's own read-only look at files on its mounts (MAM-9, Phase B) — local, so not an
/// integration connection. Phase A members only: MAM-9 reads a file's identity and its streams, Phase B
/// adds the scan and the hash, Phase C the quarantine.
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
}
