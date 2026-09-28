namespace MediaAcquisitionManager.TestInfrastructure.Time;

/// <summary>
/// A clock the tests move by hand: it stands still until <see cref="Advance"/> moves it, so "in 10
/// minutes" is exact instead of racing the wall clock.
/// </summary>
/// <remarks>
/// It starts at the real current time, not a fixed date. Some framework code checks against the
/// system clock rather than the injected <see cref="TimeProvider"/> — a JWT validator, for one — and
/// would reject anything stamped with a date in the past. For the same reason <see cref="Reset"/>
/// returns to the real time, not to the instant the clock was created: in a long run the latter
/// drifts further behind the system clock with every test.
/// </remarks>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow = TimeProvider.System.GetUtcNow();

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _utcNow;
    }

    /// <summary>Moves the clock forward. Time never runs backwards, so a negative step is a bug in the test.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        lock (_gate)
            _utcNow += by;
    }

    /// <summary>Drops every <see cref="Advance"/> and puts the clock back on the real current time.</summary>
    public void Reset()
    {
        lock (_gate)
            _utcNow = TimeProvider.System.GetUtcNow();
    }
}
