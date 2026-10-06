using System.Threading.Channels;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — wakes <see cref="LibraryLinkWriteWorker"/> when links are waiting to be written into their libraries
/// ("Accept all confident"). A signal, not a list (R13): the links in <c>Queued</c> are the queue, so nothing is ever
/// dropped, refused or left behind by a restart, however many links one click queues.
/// </summary>
public sealed class LibraryLinkWriteQueue
{
    // One slot, the extra signals dropped: a waiting signal already makes the worker write every queued link, the ones
    // queued after it included, so a second one would only wake it to find nothing left.
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>What the worker reads.</summary>
    public ChannelReader<bool> Reader => _channel.Reader;

    /// <summary>Tells the worker links are waiting; never refused.</summary>
    public void Signal() => _channel.Writer.TryWrite(true);
}
