namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a running time, shown as today's server clock in every language (<c>1:56:30</c>, hours always
/// shown, seconds truncated), so a running-time reason compares two clocks that look alike.
/// </summary>
/// <param name="Milliseconds">The time in milliseconds.</param>
public sealed record DurationValue(long Milliseconds) : MessageValue
{
    /// <summary><paramref name="time"/> in whole milliseconds, truncated.</summary>
    public static DurationValue From(TimeSpan time) => new(time.Ticks / TimeSpan.TicksPerMillisecond);
}
