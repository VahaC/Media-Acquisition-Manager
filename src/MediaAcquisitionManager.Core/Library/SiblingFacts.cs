using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — a neighbour of a library work that already has a TMDb link, and what the catalog says of the work
/// it is linked to.</summary>
/// <param name="Title">The neighbour's title, for the evidence to name.</param>
/// <param name="CatalogKind">What it is linked to: a film or a show.</param>
/// <param name="CatalogId">The catalog's id of that film or show.</param>
/// <param name="CollectionId">The collection that film belongs to; <c>null</c> for none, and for a show.</param>
/// <param name="DirectorIds">That film's directors, or that show's creators.</param>
public sealed record SiblingFacts(string Title, MediaWorkKind CatalogKind, string CatalogId, string? CollectionId, IReadOnlyList<string> DirectorIds);
