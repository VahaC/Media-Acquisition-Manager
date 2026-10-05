using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — films or shows among the tab's works.</summary>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>, which the list filters by.</param>
/// <param name="Count">How many of the tab's works are one.</param>
public sealed record RecommendationKindFacetDto(MediaWorkKind Kind, int Count);
