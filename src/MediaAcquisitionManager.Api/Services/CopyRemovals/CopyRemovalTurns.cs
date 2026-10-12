namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — one removal at a time in this process, from its first check to its last rename (Vasyl, 2026-10-12): two
/// requests to remove the last two copies of one group would each find the other copy still there, and both would go. The
/// second waits for the first to end, then checks against what the first left — the copy it removed no longer counts.
/// </summary>
/// <remarks>
/// A singleton over one <see cref="SemaphoreSlim"/>, in memory only: the app runs as one process, and a restart ends every
/// removal it held (the quarantine's pass settles the cut ones). Restore and Delete for good don't wait here: neither takes
/// a copy out of the library.
/// </remarks>
public sealed class CopyRemovalTurns : IDisposable
{
    private readonly SemaphoreSlim _turn = new(1, 1);

    /// <summary>Waits for this request's turn; the caller releases it in a <c>finally</c>.</summary>
    public Task WaitAsync(CancellationToken cancellationToken) => _turn.WaitAsync(cancellationToken);

    /// <summary>Ends this request's turn, letting the next waiting one in.</summary>
    public void Release() => _turn.Release();

    public void Dispose() => _turn.Dispose();
}
