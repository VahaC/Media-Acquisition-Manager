using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — a reason kind the tab's works are recommended for.</summary>
/// <param name="Kind">The reason kind, which the list filters by.</param>
/// <param name="Count">How many of the tab's works have a reason of it.</param>
public sealed record RecommendationReasonFacetDto(RecommendationReasonKind Kind, int Count);
