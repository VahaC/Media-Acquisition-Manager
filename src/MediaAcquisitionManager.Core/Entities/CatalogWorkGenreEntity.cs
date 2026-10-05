namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one genre the catalog files a <see cref="CatalogWorkEntity"/> under, by the catalog's id of it
/// (<see cref="CatalogGenreEntity"/> names it). Part of the work's report: replaced whenever a list or the
/// details report the work, and removed with it; its provenance is the work's.
/// </summary>
public class CatalogWorkGenreEntity : BaseEntity
{
    /// <summary>The work.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>The catalog's id of the genre, in the numbering of the work's kind.</summary>
    public int GenreId { get; set; }
}
