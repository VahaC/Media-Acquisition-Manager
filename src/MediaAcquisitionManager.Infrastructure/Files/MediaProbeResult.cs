using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>What one run of the media inspector gave (MAM-9).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Output">What it printed; <c>null</c> unless it succeeded.</param>
/// <param name="Reason">Why it gave nothing usable, as a clause the inventory nests in its sentence ("it said “…”", "it
/// didn't finish within 2 minutes"); <c>null</c> unless it failed.</param>
/// <param name="StartError">MAM-47 — the operating system's words for why it couldn't be started; <c>null</c> unless it
/// was unavailable. Kept apart from <paramref name="Reason"/>: those are another program's words, which the inventory's
/// sentence quotes as they are (a <see cref="VendorText"/>), where a reason is a clause of the app's own.</param>
internal sealed record MediaProbeResult(MediaProbeStatus Status, string? Output, Message? Reason, string? StartError)
{
    /// <summary>It finished and printed <paramref name="output"/>.</summary>
    public static MediaProbeResult Succeeded(string output) => new(MediaProbeStatus.Succeeded, output, null, null);

    /// <summary>It ran and gave nothing usable, for <paramref name="reason"/>.</summary>
    public static MediaProbeResult Failed(Message reason) => new(MediaProbeStatus.Failed, null, reason, null);

    /// <summary>It couldn't be started; <paramref name="reason"/> is the operating system's explanation.</summary>
    public static MediaProbeResult Unavailable(string reason) => new(MediaProbeStatus.Unavailable, null, null, reason);
}
