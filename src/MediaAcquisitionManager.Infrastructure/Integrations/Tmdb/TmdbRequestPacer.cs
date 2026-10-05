using System.Net.Http.Headers;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

/// <summary>
/// MAM-33 — the gate every request of the TMDb adapter passes before it is sent: two requests never
/// start less than <see cref="MinimumSpacing"/> apart, whoever sends them, and once TMDb has answered
/// 429 none starts before the wait it asked for is over (<see cref="HoldOff"/>).
/// </summary>
/// <remarks>
/// <para>
/// TMDb limits requests per IP address, not per caller: a recommendation page fans out into a work's
/// details, its collection and its directors, and several pages may be built at once. One gate in the
/// one adapter instance (a singleton) keeps all of them under the limit together; a gate per call would
/// let ten callers send ten times the rate. For the same reason a 429 holds back every caller, not only
/// the one that got it: the next request from anyone would be over the limit too.
/// </para>
/// <para>
/// Elapsed time comes from the injected <see cref="TimeProvider"/>'s timestamps, never from its wall
/// clock: the test host's clock stands still until a test moves it (<c>TestTimeProvider</c>), while its
/// timestamps, like the system's, are the real monotonic ones — so the spacing is real in tests too, and
/// a wall clock set back by the operating system can't stall the gate.
/// </para>
/// </remarks>
internal sealed class TmdbRequestPacer(TimeProvider time)
{
    /// <summary>
    /// TMDb's documentation ("Rate Limiting") puts its limit only roughly, "somewhere in the 50 requests
    /// per second range" from one address. 25 ms between starts is at most 40 a second: a margin under a
    /// limit nobody states exactly, and still a whole recommendation page (a few dozen requests) in about
    /// a second.
    /// </summary>
    public static readonly TimeSpan MinimumSpacing = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// The longest <c>Retry-After</c> the adapter waits out: a request is waited on by a person looking
    /// at a page, and past 10 seconds the sentence saying TMDb is busy is the better answer.
    /// </summary>
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// What a 429 without a usable <c>Retry-After</c> waits: TMDb's limit is per second, so two seconds
    /// let the window it counted in pass with room to spare.
    /// </summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    // One request at a time books its start; the others queue on the semaphore in arrival order.
    private readonly SemaphoreSlim _turn = new(1, 1);

    // HoldOff runs outside the semaphore (a caller holding it may be asleep in its wait), so the
    // hold-off is guarded on its own.
    private readonly Lock _holdOffLock = new();

    private long? _lastStart;
    private long _holdOffFrom;
    private TimeSpan _holdOff = TimeSpan.Zero;

    /// <summary>
    /// Waits until a request may start — <see cref="MinimumSpacing"/> after the last one, and after any
    /// hold-off — and books that moment as the last start. The caller sends its request right after.
    /// </summary>
    public async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            // A loop rather than one delay: a timer may fire a little early on a coarse system clock, and a
            // hold-off may arrive while this caller sleeps. It ends once nothing is left to wait.
            for (var remaining = Remaining(); remaining > TimeSpan.Zero; remaining = Remaining())
                await Task.Delay(remaining, time, cancellationToken);

            _lastStart = time.GetTimestamp();
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// TMDb answered 429: no request starts before <paramref name="retryAfter"/>'s wait is over
    /// (<see cref="RetryDelay"/>). A longer hold-off already running is kept.
    /// </summary>
    public void HoldOff(RetryConditionHeaderValue? retryAfter)
    {
        var delay = RetryDelay(retryAfter, time.GetUtcNow());
        var now = time.GetTimestamp();
        lock (_holdOffLock)
        {
            if (delay > _holdOff - time.GetElapsedTime(_holdOffFrom, now))
            {
                _holdOffFrom = now;
                _holdOff = delay;
            }
        }
    }

    /// <summary>
    /// How long a 429's <c>Retry-After</c> asks to wait: its seconds, or the time until its date (judged
    /// against <paramref name="now"/>), never less than nothing and never more than
    /// <see cref="MaximumRetryDelay"/>; <see cref="DefaultRetryDelay"/> without one.
    /// </summary>
    internal static TimeSpan RetryDelay(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        var asked = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - now,
            _ => DefaultRetryDelay,
        };

        return asked < TimeSpan.Zero ? TimeSpan.Zero
            : asked > MaximumRetryDelay ? MaximumRetryDelay
            : asked;
    }

    /// <summary>What is left of the spacing after the last start and of the hold-off, whichever is longer.</summary>
    private TimeSpan Remaining()
    {
        var now = time.GetTimestamp();
        var spacing = _lastStart is { } last ? MinimumSpacing - time.GetElapsedTime(last, now) : TimeSpan.Zero;
        TimeSpan holdOff;
        lock (_holdOffLock)
            holdOff = _holdOff - time.GetElapsedTime(_holdOffFrom, now);

        return spacing > holdOff ? spacing : holdOff;
    }
}
