namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// The app's own read-only look at files on its mounts (MAM-9, Phase B) — local, so not an
/// integration connection. Phase A members only: MAM-9 designs the inspection's verified properties,
/// Phase B the scan and hash, Phase C the quarantine.
/// </summary>
/// <remarks>
/// All methods return result types and never throw on expected failures (not found, outside the
/// allowed roots, unreadable); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence.
/// </remarks>
public interface IFileInventory
{
    /// <summary>What this platform can report about a file.</summary>
    FileInventoryCapabilities Capabilities { get; }

    /// <summary>
    /// What the file system says about the file at <paramref name="appPath"/>. Refuses a path outside
    /// the configured roots and never follows a symbolic link out of them (specification §4).
    /// </summary>
    Task<FileInventoryResult<FileFacts>> DescribeAsync(string appPath, CancellationToken cancellationToken = default);
}
