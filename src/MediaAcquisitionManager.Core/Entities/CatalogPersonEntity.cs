using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Integrations;

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

    /// <summary>
    /// MAM-46 — the catalog language the list of their works was read in (<c>uk-UA</c>, <c>en-US</c>): the catalog
    /// store counts the row as fresh only while it is the catalog's language of now, so a change of language reads it
    /// again on its next use. Every row cached before MAM-46 was read in Ukrainian, which the migration's default says.
    /// </summary>
    [Required, MaxLength(CatalogLanguageRules.MaximumLength)]
    public string Language { get; set; } = default!;
}
