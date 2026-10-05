using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a work the catalog recommends beside a <see cref="CatalogWorkEntity"/>, best first, as its
/// details list them. Part of the details: replaced when they are read again, and removed with the work.
/// </summary>
/// <remarks>
/// The recommended work by its kind and catalog id, not by a foreign key to its row: a key would tie the
/// two rows' lives together (a cascade or a restriction either way), while the recommended work's summary
/// is stored as a row of its own by the same read anyway, found by the same two values.
/// </remarks>
public class CatalogWorkRecommendationEntity : BaseEntity
{
    /// <summary>The work whose details recommend it.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>Where the details list it, from 0: the catalog's best first.</summary>
    public int Position { get; set; }

    /// <summary>The recommended work's kind; the catalog recommends works of the same kind.</summary>
    public MediaWorkKind Kind { get; set; }

    /// <summary>The recommended work's catalog id.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;
}
