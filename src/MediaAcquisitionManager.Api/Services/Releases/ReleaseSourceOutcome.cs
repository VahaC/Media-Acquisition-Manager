using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>How one release source answered one search, before its releases are stored (MAM-10).</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints the two lists as their type names, never the
/// hits (whose own <c>ToString</c> masks the download link), and the error is masked of every secret
/// before it gets here.
/// </remarks>
/// <param name="IntegrationId">The source's integration.</param>
/// <param name="Name">Its name when it was read for the search.</param>
/// <param name="Status">How the call went; <c>null</c> when the app didn't ask it.</param>
/// <param name="NotSearchedReason">Why the app didn't ask it; <c>null</c> when it did.</param>
/// <param name="Error">The sentence saying what went wrong, for the owner; <c>null</c> when nothing did.</param>
/// <param name="Hits">What it listed; empty unless it succeeded.</param>
/// <param name="Categories">Its categories, for naming the ones a release is listed in; empty unless it
/// succeeded.</param>
public sealed record ReleaseSourceOutcome(
    Guid IntegrationId,
    string Name,
    IntegrationCallStatus? Status,
    ReleaseSourceNotSearchedReason? NotSearchedReason,
    string? Error,
    IReadOnlyList<ReleaseSearchHit> Hits,
    IReadOnlyList<ReleaseCategory> Categories);
