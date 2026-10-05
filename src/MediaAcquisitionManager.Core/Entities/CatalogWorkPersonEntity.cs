using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a film's director or a show's creator, as the work's details name them, in the catalog's
/// order. Part of the details: replaced when they are read again, and removed with the work.
/// </summary>
public class CatalogWorkPersonEntity : BaseEntity
{
    /// <summary>The work.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>Where the details list the person, from 0.</summary>
    public int Position { get; set; }

    /// <summary>The catalog's id of the person.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string PersonCatalogId { get; set; } = default!;

    /// <summary>Their name as the details give it, which a recommendation's reason shows.</summary>
    [Required, MaxLength(CatalogWorkEntity.TitleMaximumLength)]
    public string Name { get; set; } = default!;
}
