using System.Threading.Channels;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// The library syncs waiting for <see cref="LibrarySyncWorker"/>, by run id (Queue/Worker, MAM-8). The
/// run row is the record; the queue only wakes the worker, so a restart that empties it loses nothing
/// the reconciler doesn't settle.
/// </summary>
public sealed class LibrarySyncQueue(ILogger<LibrarySyncQueue> logger)
{
    /// <summary>
    /// Far above any installation: a sync can't start while another of the same integration is under
    /// way (a unique index says so), so the queue holds at most one run per media library integration,
    /// and an installation has a handful.
    /// </summary>
    public const int Capacity = 64;

    // Wait, not DropOldest: a dropped run id would leave its run queued until the next restart. A full
    // queue refuses the new run instead (TryEnqueue), and the start answers 503.
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

        // Warning, so it ships off the box: 64 waiting syncs means the worker is stuck, not busy.
        logger.LogWarning("The library sync queue is full ({Capacity} waiting); run {RunId} was not queued.", Capacity, runId);
        return false;
    }
}
