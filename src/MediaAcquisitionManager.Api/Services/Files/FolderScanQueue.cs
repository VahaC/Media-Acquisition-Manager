using System.Threading.Channels;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// The scans of the media folders waiting for <see cref="FolderScanWorker"/>, by run id (Queue/Worker,
/// MAM-18). The run row is the record; the queue only wakes the worker, and the reconciler queues again
/// whatever a restart left queued.
/// </summary>
public sealed class FolderScanQueue(ILogger<FolderScanQueue> logger)
{
    /// <summary>
    /// Far above need, the inspection queue's number: a unique index allows one queued scan at a time, and
    /// a Scan folders that finds one joins it, so the queue holds one id, or two for a moment when the
    /// reconciler queues a scan again.
    /// </summary>
    public const int Capacity = 16;

    // Wait, not DropOldest: a dropped id would leave its run queued until the next restart. A full queue
    // refuses the new run instead (TryEnqueue), which is then recorded as failed.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });

    /// <summary>What the worker reads.</summary>
    public ChannelReader<Guid> Reader => _channel.Reader;

    /// <summary>Queues <paramref name="runId"/>; <c>false</c>, logged as a Warning, when the queue is
    /// full.</summary>
    public bool TryEnqueue(Guid runId)
    {
        if (_channel.Writer.TryWrite(runId))
            return true;

        // Warning, so it ships off the box: a full queue means the worker is stuck, not busy.
        logger.LogWarning("The folder scan queue is full ({Capacity} waiting); run {RunId} was not queued.", Capacity, runId);
        return false;
    }
}
