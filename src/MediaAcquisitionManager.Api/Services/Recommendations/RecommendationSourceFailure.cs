using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-31 — how a release source's last call for the recommendations failed.</summary>
/// <param name="Status">How the call went.</param>
/// <param name="Error">Why, as the source's message, every secret masked; the owner's to read.</param>
/// <param name="FailedUtc">When.</param>
public sealed record RecommendationSourceFailure(IntegrationCallStatus Status, Message? Error, DateTime FailedUtc);
