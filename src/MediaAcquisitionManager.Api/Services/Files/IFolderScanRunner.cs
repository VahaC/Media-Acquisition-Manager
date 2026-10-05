namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Runs one queued scan of the media folders (MAM-18, ADR 0021): walks every allowed folder, then stores
/// in one go a name for each file no library reported and forgets the ones gone, and queues the
/// inspection that reads them.
/// </summary>
public interface IFolderScanRunner
{
    /// <summary>
    /// Claims the queued run <paramref name="runId"/> and runs it to its outcome; does nothing when the
    /// run isn't queued any more.
    /// </summary>
    Task RunAsync(Guid runId, CancellationToken cancellationToken = default);
}
