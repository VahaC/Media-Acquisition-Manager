using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — one genre of a source's catalog.</summary>
/// <param name="Id">The source's id of the genre.</param>
/// <param name="Name">Its name in the catalog's language.</param>
/// <param name="Role">What the core classifies a work of it as: the source knows its numbering, the core
/// doesn't (the rule the release categories follow, ADR 0004).</param>
public sealed record CatalogGenre(int Id, string Name, CatalogGenreRole Role);
