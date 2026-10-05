namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — a collection of films (<see cref="IMetadataCatalog.GetCollectionAsync"/>).</summary>
/// <param name="Id">The source's id of the collection.</param>
/// <param name="Name">Its name in the catalog's language.</param>
/// <param name="Parts">Every film of it, those still to come included.</param>
public sealed record CatalogCollection(string Id, string Name, IReadOnlyList<CatalogWorkSummary> Parts);
