using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — what of a release the owner may replace through a destination, and why not where not (ADR
/// 0031–0033).</summary>
/// <param name="Unavailable">Why nothing of the release can be replaced, the first that applies; <c>null</c> when the
/// works below say what can. While it is set, <paramref name="Works"/> and <paramref name="Clients"/> are empty.</param>
/// <param name="DestinationProblem">The app's sentence for <see cref="ReplacementUnavailableReason.DestinationNotUsable"/>
/// — the owner's alone, as the route is —; <c>null</c> otherwise.</param>
/// <param name="ReleaseEditions">MAM-37 — the edition words the release's title holds, its own titles taken out.</param>
/// <param name="Works">The library works the release matched, strongest evidence first, each with its copies.</param>
/// <param name="Clients">How each download client's torrents were read for the held marks, in name order; empty when
/// none was read.</param>
public sealed record ReplacementOptionsResponse(
    ReplacementUnavailableReason? Unavailable,
    string? DestinationProblem,
    IReadOnlyList<MediaEdition> ReleaseEditions,
    IReadOnlyList<ReplacementWorkDto> Works,
    IReadOnlyList<ReplacementClientCheckDto> Clients);
