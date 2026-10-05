namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — a collection or a person a work names, by the source's id and name.</summary>
/// <param name="Id">The source's id.</param>
/// <param name="Name">The name in the catalog's language.</param>
public sealed record CatalogReference(string Id, string Name);
