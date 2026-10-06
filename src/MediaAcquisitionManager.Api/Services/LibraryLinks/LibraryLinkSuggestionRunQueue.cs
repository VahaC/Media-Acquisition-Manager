using System.Threading.Channels;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the preparations of the TMDb suggestions waiting for <see cref="LibraryLinkSuggestionRunWorker"/>, by run id
/// (Queue/Worker, the <c>RecommendationRunQueue</c> pattern). The run row is the record; the queue only wakes the worker,
/// and the reconciler queues again whatever a restart left queued.
/// </summary>
public sealed class LibraryLinkSuggestionRunQueue(ILogger<LibraryLinkSuggestionRunQueue> logger)
{
    /// <summary>
    /// Far above need: a unique index allows one queued run at a time, and a trigger that finds one joins it, so the
    /// queue holds one id, or two for a moment when the reconciler queues a run again.
    /// </summary>
    public const int Capacity = 16;

    // Wait, not DropOldest: a dropped id would leave its run queued until the next restart. A full queue refuses the
    // new run instead (TryEnqueue), which is then recorded as failed.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });

    /// <summary>What the worker reads.</summary>
    public ChannelReader<Guid> Reader => _channel.Reader;

    /// <summary>Queues <paramref name="runId"/>; <c>false</c>, logged as a Warning, when the queue is full.</summary>
    public bool TryEnqueue(Guid runId)
    {
        if (_channel.Writer.TryWrite(runId))
            return true;

        // Warning, so it ships off the box: a full queue means the worker is stuck, not busy.
        logger.LogWarning("The TMDb suggestion run queue is full ({Capacity} waiting); run {RunId} was not queued.", Capacity, runId);
        return false;
    }
}
