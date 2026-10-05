using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one genre of the catalog's list for a kind of work, with the role it plays for the
/// recommendation tabs (<see cref="CatalogGenreRole"/>). The whole list of a kind is read again once it is
/// older than the refresh age (<see cref="ProvenancedEntity.ObservedUtc"/>).
/// </summary>
public class CatalogGenreEntity : ProvenancedEntity
{
    /// <summary>The kind of work the catalog files under it; films and shows have lists of their own.</summary>
    public MediaWorkKind Kind { get; set; }

    /// <summary>The catalog's id of the genre.</summary>
    public int GenreId { get; set; }

    /// <summary>Its name in the catalog's language.</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = default!;

    /// <summary>What it makes a work, for the tabs: the catalog's adapter decides, the core never reads its
    /// numbering.</summary>
    public CatalogGenreRole Role { get; set; }
}
