namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>What one run of the media inspector gave (MAM-9).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Output">What it printed; <c>null</c> unless it succeeded.</param>
/// <param name="Reason">Why it gave nothing usable, as a clause the inventory puts into its sentence ("it said
/// “…”", "it didn't finish within 2 minutes"); <c>null</c> when it succeeded.</param>
internal sealed record MediaProbeResult(MediaProbeStatus Status, string? Output, string? Reason)
{
    /// <summary>It finished and printed <paramref name="output"/>.</summary>
    public static MediaProbeResult Succeeded(string output) => new(MediaProbeStatus.Succeeded, output, null);

    /// <summary>It ran and gave nothing usable, for <paramref name="reason"/>.</summary>
    public static MediaProbeResult Failed(string reason) => new(MediaProbeStatus.Failed, null, reason);

    /// <summary>It couldn't be started; <paramref name="reason"/> is the operating system's explanation.</summary>
    public static MediaProbeResult Unavailable(string reason) => new(MediaProbeStatus.Unavailable, null, reason);
}
