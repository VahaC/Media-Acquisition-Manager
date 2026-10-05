namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — a decade the tab's works came out in.</summary>
/// <param name="Decade">Its first year (1990 for the 1990s), which the list filters by.</param>
/// <param name="Count">How many of the tab's works came out in it.</param>
public sealed record RecommendationDecadeFacetDto(int Decade, int Count);
