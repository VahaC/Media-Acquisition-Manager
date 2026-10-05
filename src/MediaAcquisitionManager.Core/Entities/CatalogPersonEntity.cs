using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a director or a creator as the catalog lists them, with the films they directed and the shows
/// they created (<see cref="CatalogPersonWorkEntity"/>). Read again once older than the refresh age
/// (<see cref="ProvenancedEntity.ObservedUtc"/>).
/// </summary>
public class CatalogPersonEntity : ProvenancedEntity
{
    /// <summary>The catalog's id of the person.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;

    /// <summary>Their name, which a recommendation's reason shows.</summary>
    [Required, MaxLength(CatalogWorkEntity.TitleMaximumLength)]
    public string Name { get; set; } = default!;
}
