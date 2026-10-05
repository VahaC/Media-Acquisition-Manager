using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a collection of films as the catalog lists it, with its parts in order
/// (<see cref="CatalogCollectionPartEntity"/>), each part's summary a <see cref="CatalogWorkEntity"/>. Read
/// again once older than the refresh age (<see cref="ProvenancedEntity.ObservedUtc"/>).
/// </summary>
public class CatalogCollectionEntity : ProvenancedEntity
{
    /// <summary>The catalog's id of the collection.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;

    /// <summary>Its name in the catalog's language, which a recommendation's reason shows.</summary>
    [Required, MaxLength(CatalogWorkEntity.TitleMaximumLength)]
    public string Name { get; set; } = default!;
}
