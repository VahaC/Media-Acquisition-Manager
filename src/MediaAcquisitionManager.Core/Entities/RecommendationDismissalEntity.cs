using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a work a user marked "Not interested" (Vasyl's decision 3): kept per user, and left out of that
/// user's lists only. <see cref="AuditableEntity.CreatedUtc"/> is when they marked it.
/// </summary>
/// <remarks>
/// The work by its kind and catalog id, not by a foreign key to its catalog row: a dismissal outlives the
/// row, and a work recommended again after its row was read anew stays dismissed.
/// </remarks>
public class RecommendationDismissalEntity : AuditableEntity
{
    /// <summary>The user who isn't interested.</summary>
    public Guid UserId { get; set; }

    /// <summary>A film or a show: the catalog numbers the two apart.</summary>
    public MediaWorkKind Kind { get; set; }

    /// <summary>The work's catalog id.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;
}
