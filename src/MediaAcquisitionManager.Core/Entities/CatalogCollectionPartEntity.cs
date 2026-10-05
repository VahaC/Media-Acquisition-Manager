using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one film of a <see cref="CatalogCollectionEntity"/>, those still to come included, in the
/// catalog's order. The film by its catalog id: its summary is a <see cref="CatalogWorkEntity"/> of kind
/// Movie, stored by the same read. Replaced when the collection is read again, and removed with it.
/// </summary>
public class CatalogCollectionPartEntity : BaseEntity
{
    /// <summary>The collection.</summary>
    public Guid CatalogCollectionId { get; set; }

    /// <summary>Where the collection lists it, from 0.</summary>
    public int Position { get; set; }

    /// <summary>The film's catalog id.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string MovieCatalogId { get; set; } = default!;
}
