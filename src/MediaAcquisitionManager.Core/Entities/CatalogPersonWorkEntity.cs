using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a film a <see cref="CatalogPersonEntity"/> directed or a show they created, in the catalog's
/// order. The work by its kind and catalog id: its summary is a <see cref="CatalogWorkEntity"/> stored by
/// the same read. Replaced when the person is read again, and removed with them.
/// </summary>
public class CatalogPersonWorkEntity : BaseEntity
{
    /// <summary>The person.</summary>
    public Guid CatalogPersonId { get; set; }

    /// <summary>Where the catalog lists it, from 0.</summary>
    public int Position { get; set; }

    /// <summary>A film they directed, or a show they created.</summary>
    public MediaWorkKind Kind { get; set; }

    /// <summary>The work's catalog id.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;
}
