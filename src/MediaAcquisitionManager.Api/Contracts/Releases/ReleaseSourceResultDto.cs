using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>How one release source answered a search, and the releases it listed.</summary>
/// <remarks>
/// MAM-10 — no address, key or type id: every signed-in user searches, and integrations are the owner's.
/// The sentence can name the service's address, so only the owner gets it (the MAM-8 rule for a sync
/// run's error); everyone reads the status or the reason, which the page words itself.
/// </remarks>
/// <param name="IntegrationId">The release source's integration.</param>
/// <param name="Name">Its display name — the indexer the releases come from.</param>
/// <param name="Status">How the call to it went (<c>Succeeded</c> when it answered, even with nothing);
/// <c>null</c> when the app didn't ask it, and <paramref name="NotSearchedReason"/> says why.</param>
/// <param name="NotSearchedReason">Why the app didn't ask it; <c>null</c> when it did.</param>
/// <param name="Error">The sentence saying what went wrong, every secret masked. For the installation owner
/// only, <c>null</c> for everyone else and when nothing went wrong.</param>
/// <param name="Releases">What it listed, in its own order; empty unless it succeeded.</param>
public sealed record ReleaseSourceResultDto(
    Guid IntegrationId,
    string Name,
    IntegrationCallStatus? Status,
    ReleaseSourceNotSearchedReason? NotSearchedReason,
    string? Error,
    IReadOnlyList<FoundReleaseDto> Releases);
