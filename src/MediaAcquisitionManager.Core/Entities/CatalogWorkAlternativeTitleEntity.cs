using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — another title the catalog knows a film or show by (another country's, another release's), in the catalog's
/// order: a library work named by any of them still finds the work among its suggestions. Part of the details: replaced
/// when they are read again, and removed with the work.
/// </summary>
public class CatalogWorkAlternativeTitleEntity : BaseEntity
{
    /// <summary>The work.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>Where the details list the title, from 0.</summary>
    public int Position { get; set; }

    /// <summary>The title.</summary>
    [Required, MaxLength(CatalogWorkEntity.TitleMaximumLength)]
    public string Title { get; set; } = default!;
}
