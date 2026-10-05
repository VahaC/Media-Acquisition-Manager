namespace MediaAcquisitionManager.TestInfrastructure.Time;

/// <summary>
/// A clock the tests move by hand: it stands still until <see cref="Advance"/> moves it, so "in 10
/// minutes" is exact instead of racing the wall clock.
/// </summary>
/// <remarks>
/// It starts at the real current time, not a fixed date. Some framework code checks against the
/// system clock rather than the injected <see cref="TimeProvider"/> and would reject anything stamped
/// with a date in the past — a JWT validator does unless told otherwise (the app tells it to read
/// this clock, MAM-4, so a test can age a token out by advancing it). For the same reason
/// <see cref="Reset"/> returns to the real time, not to the instant the clock was created: in a long
/// run the latter drifts further behind the system clock with every test.
/// </remarks>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow = TimeProvider.System.GetUtcNow();
    private long _timestampOffset;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _utcNow;
    }

    /// <summary>
    /// The system's monotonic timestamp, plus whatever <see cref="AdvanceTimestamps"/> added. Real elapsed time keeps
    /// passing — the TMDb adapter's pacing waits on it, and a timestamp that stood still would hold its every request
    /// back for good — while a test can still make a span look longer than it took.
    /// </summary>
    public override long GetTimestamp() => TimeProvider.System.GetTimestamp() + Interlocked.Read(ref _timestampOffset);

    /// <summary>
    /// MAM-33 — moves the timestamps forward without moving the wall clock: "two seconds passed" for code that measures
    /// elapsed time, such as a recommendation run deciding whether to write its progress. Never rewound, not even by
    /// <see cref="Reset"/>: a timestamp taken later must never be smaller than one taken before.
    /// </summary>
    public void AdvanceTimestamps(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Interlocked.Add(ref _timestampOffset, (long)(by.TotalSeconds * TimestampFrequency));
    }

    /// <summary>Moves the clock forward. Time never runs backwards, so a negative step is a bug in the test.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        lock (_gate)
            _utcNow += by;
    }

    /// <summary>Drops every <see cref="Advance"/> and puts the clock back on the real current time; the timestamps keep
    /// what <see cref="AdvanceTimestamps"/> added.</summary>
    public void Reset()
    {
        lock (_gate)
            _utcNow = TimeProvider.System.GetUtcNow();
    }
}
