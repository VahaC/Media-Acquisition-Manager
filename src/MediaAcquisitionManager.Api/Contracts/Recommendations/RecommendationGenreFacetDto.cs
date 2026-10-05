namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — a catalog genre the tab's works are filed under.</summary>
/// <param name="Id">The catalog's genre id, which the list filters by.</param>
/// <param name="Name">Its name in the catalog's language.</param>
/// <param name="Count">How many of the tab's works the catalog files under it.</param>
public sealed record RecommendationGenreFacetDto(int Id, string Name, int Count);
