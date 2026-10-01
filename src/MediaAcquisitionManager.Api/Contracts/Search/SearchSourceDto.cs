using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>How one release source answered a stored search (MAM-12).</summary>
/// <remarks>
/// No address, key or type id, the <see cref="ReleaseSourceResultDto"/> rule: every signed-in user
/// searches, and the integrations are the owner's. A failed source says so here, beside the others'
/// releases.
/// </remarks>
/// <param name="Id">The search's own row for the source: the value of the release list's
/// <c>source</c> filter.</param>
/// <param name="IntegrationId">The release source's integration; <c>null</c> once it is removed.</param>
/// <param name="Name">Its name when it was searched.</param>
/// <param name="Status">How the call to it went (<c>Succeeded</c> when it answered, even with nothing);
/// <c>null</c> when the app didn't ask it, and <paramref name="NotSearchedReason"/> says why.</param>
/// <param name="NotSearchedReason">Why the app didn't ask it; <c>null</c> when it did.</param>
/// <param name="Error">The sentence saying what went wrong, every secret masked. For the installation owner
/// only, <c>null</c> for everyone else and when nothing went wrong.</param>
/// <param name="ReleaseCount">How many releases it listed.</param>
public sealed record SearchSourceDto(
    Guid Id,
    Guid? IntegrationId,
    string Name,
    IntegrationCallStatus? Status,
    ReleaseSourceNotSearchedReason? NotSearchedReason,
    string? Error,
    int ReleaseCount);
