namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — a person and their works (<see cref="IMetadataCatalog.GetPersonAsync"/>).</summary>
/// <param name="Id">The source's id of the person.</param>
/// <param name="Name">Their name in the catalog's language.</param>
/// <param name="Works">The films they directed and the shows they created, each once.</param>
public sealed record CatalogPerson(string Id, string Name, IReadOnlyList<CatalogWorkSummary> Works);
