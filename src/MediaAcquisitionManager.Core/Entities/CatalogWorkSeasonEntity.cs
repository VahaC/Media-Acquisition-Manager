namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one season of a show as its details list it, the specials (season 0) included. Part of the
/// details: replaced when they are read again, and removed with the work.
/// </summary>
public class CatalogWorkSeasonEntity : BaseEntity
{
    /// <summary>The show.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>The season's number; 0 for the specials.</summary>
    public int Number { get; set; }

    /// <summary>When its first episode aired; <c>null</c> when not known, as for a season still to come.</summary>
    public DateOnly? AirDate { get; set; }

    /// <summary>How many episodes the catalog lists for it.</summary>
    public int EpisodeCount { get; set; }
}
