using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-31 — a release source whose last call for the recommendations failed — the availability check's search or the
/// newest releases' read —, which the page says beside the cards whose counts it couldn't refresh, as the Search page
/// says a failed source beside the releases. No address, key or type id (the <see cref="SearchSourceDto"/> rule).
/// </summary>
/// <param name="Name">The source's name now.</param>
/// <param name="Status">How the call went: never <c>Succeeded</c>.</param>
/// <param name="Error">The source's sentence saying what went wrong and what to do, every secret masked. For the
/// installation owner only, <c>null</c> for everyone else (the MAM-8 rule).</param>
/// <param name="FailedUtc">When the call failed.</param>
public sealed record RecommendationSourceFailureDto(string Name, IntegrationCallStatus Status, string? Error, DateTime FailedUtc);
